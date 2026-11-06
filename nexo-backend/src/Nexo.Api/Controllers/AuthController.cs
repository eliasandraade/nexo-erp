using System.Security.Claims;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Nexo.Application.Common.Interfaces;
using Nexo.Application.Features.Auth;
using Nexo.Application.Validators.Auth;
using Nexo.Infrastructure.Auth;
using Nexo.Infrastructure.Persistence;

namespace Nexo.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly AuthService _authService;
    private readonly RegistrationService _registration;
    private readonly IValidator<LoginRequest> _loginValidator;
    private readonly ICurrentUser _currentUser;
    private readonly IUserRepository _users;
    private readonly ITenantRepository _tenants;
    private readonly IStoreRepository _stores;
    private readonly NexoDbContext _db;
    private readonly IPasswordHasher _hasher;
    private readonly IJwtTokenService _jwt;
    private readonly Nexo.Api.Middleware.BrowserSession _browser;

    public AuthController(
        AuthService authService,
        RegistrationService registration,
        IValidator<LoginRequest> loginValidator,
        ICurrentUser currentUser,
        IUserRepository users,
        ITenantRepository tenants,
        IStoreRepository stores,
        NexoDbContext db,
        IPasswordHasher hasher,
        IJwtTokenService jwt,
        Nexo.Api.Middleware.BrowserSession browser)
    {
        _authService = authService;
        _registration = registration;
        _loginValidator = loginValidator;
        _currentUser = currentUser;
        _users = users;
        _tenants = tenants;
        _stores = stores;
        _db = db;
        _hasher = hasher;
        _jwt = jwt;
        _browser = browser;
    }

    private bool CookieMode => Nexo.Api.Middleware.BrowserSession.IsCookieMode(Request);

    /// <summary>Authenticate and receive access + refresh tokens.</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("auth-login")]
    public async Task<ActionResult<LoginResponse>> Login(
        [FromBody] LoginRequest request,
        CancellationToken ct)
    {
        await _loginValidator.ValidateAndThrowAsync(request, ct);

        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
        var userAgentRaw = Request.Headers.UserAgent.ToString();
        var userAgent = string.IsNullOrWhiteSpace(userAgentRaw) ? null
            : userAgentRaw.Length > 500 ? userAgentRaw[..500] : userAgentRaw;
        // An e-mail login may be a platform operator: check that first, with its own lockout —
        // otherwise every platform sign-in counted as a wrong password for a same-named tenant
        // user (and could lock the operator out), and platform logins had no per-account limit.
        if (request.Login.Contains('@'))
        {
            var platformAttempt = await TryPlatformLoginAsync(request, ct);
            if (platformAttempt is not null) return platformAttempt;
        }

        var outcome = await _authService.LoginAsync(request, ipAddress, userAgent, ct);

        if (outcome.MfaChallenge is not null)
            return Ok(new { mfaRequired = true, mfaChallenge = outcome.MfaChallenge });
        if (outcome.IsSuccess)
            return SignedIn(outcome.Response!);

        if (outcome.ErrorCode == "account_locked")
            return StatusCode(StatusCodes.Status429TooManyRequests, new
            {
                error = $"Muitas tentativas com senha errada. Tente de novo em {AuthService.LockoutWindow.TotalMinutes:0} minutos ou use \"Esqueci a senha\".",
                code = "account_locked",
            });

        if (outcome.ErrorCode == "email_not_verified")
            return Unauthorized(new { error = "E-mail não verificado. Verifique sua caixa de entrada.", code = "email_not_verified" });

        if (outcome.ErrorCode == "account_blocked")
            return Unauthorized(new { error = "Conta bloqueada. Entre em contato com o suporte." });

        return Unauthorized(new { error = "Login ou senha incorretos." });
    }

    /// <summary>
    /// The answer of a sign-in, whichever route and kind of account got here. In the cookie mode the refresh token goes in the cookie
    /// and the body does not carry it; otherwise it goes in the body and nothing is set.
    /// </summary>
    private ActionResult<LoginResponse> SignedIn(LoginResponse response)
    {
        if (!CookieMode) return Ok(response);
        _browser.SetCookie(Response, response.RefreshToken, response.RefreshTokenExpiresAt);
        return Ok(new { response.AccessToken, response.AccessTokenExpiresAt, response.Session });
    }

    /// <summary>The answer of a renewal: same rule as <see cref="SignedIn"/>.</summary>
    private ActionResult<RefreshResponse> Renewed(RefreshResponse tokens)
    {
        if (!CookieMode) return Ok(tokens);
        _browser.SetCookie(Response, tokens.RefreshToken, tokens.RefreshTokenExpiresAt);
        return Ok(new { tokens.AccessToken, tokens.AccessTokenExpiresAt });
    }

    /// <summary>Second step of the sign-in: the challenge plus a code from the authenticator app (or a recovery code).</summary>
    [HttpPost("mfa/verify")]
    [AllowAnonymous]
    [EnableRateLimiting("auth-login")]
    public async Task<ActionResult<LoginResponse>> VerifyMfa([FromBody] MfaVerifyRequest request, CancellationToken ct)
    {
        if (Nexo.Infrastructure.Auth.PlatformAuthService.IsPlatformToken(request.Challenge))
        {
            var platform = await HttpContext.RequestServices.GetRequiredService<Nexo.Infrastructure.Auth.PlatformAuthService>()
                .VerifyMfaAsync(request.Challenge, request.Code, request.RecoveryCode, ct);
            if (platform.Tokens is not null) return SignedIn(PlatformLoginResponse(platform.User!, platform.Tokens));
            return platform.ErrorCode switch
            {
                "account_locked" => StatusCode(StatusCodes.Status429TooManyRequests, new { error = "Muitas tentativas. Tente de novo em 15 minutos.", code = "account_locked" }),
                "invalid_challenge" => Unauthorized(new { error = "A verificação expirou. Entre com sua senha de novo.", code = "invalid_challenge" }),
                _ => Unauthorized(new { error = "Código inválido.", code = "invalid_code" }),
            };
        }
        var userAgentRaw = Request.Headers.UserAgent.ToString();
        var outcome = await _authService.VerifyMfaAsync(request, HttpContext.Connection.RemoteIpAddress?.ToString(),
            string.IsNullOrWhiteSpace(userAgentRaw) ? null : userAgentRaw.Length > 500 ? userAgentRaw[..500] : userAgentRaw, ct);
        if (outcome.IsSuccess) return SignedIn(outcome.Response!);
        return outcome.ErrorCode switch
        {
            "account_locked" => StatusCode(StatusCodes.Status429TooManyRequests, new { error = "Muitas tentativas. Tente de novo em 15 minutos.", code = "account_locked" }),
            "invalid_challenge" => Unauthorized(new { error = "A verificação expirou. Entre com sua senha de novo.", code = "invalid_challenge" }),
            _ => Unauthorized(new { error = "Código inválido.", code = "invalid_code" }),
        };
    }

    /// <summary>The signed-in user's two-step verification status.</summary>
    [HttpGet("mfa")]
    [Authorize]
    public async Task<ActionResult<MfaStatusDto>> MfaStatus([FromServices] MfaService mfa, CancellationToken ct)
        => Ok(await mfa.GetStatusAsync(_currentUser.UserId, ct));

    /// <summary>Starts the setup: a new secret (manual key + otpauth link for the QR code).</summary>
    [HttpPost("mfa/setup")]
    [Nexo.Api.Filters.DenyDuringImpersonation]
    [Authorize]
    [EnableRateLimiting("credential-check")]
    public async Task<ActionResult<MfaSetupDto>> MfaSetup([FromBody] MfaSetupRequest request, [FromServices] MfaService mfa, CancellationToken ct)
        => Ok(await mfa.StartSetupAsync(_currentUser.UserId, request.Password, ct));

    /// <summary>Turns it on with a first code; returns the recovery codes (shown once).</summary>
    [HttpPost("mfa/enable")]
    [Nexo.Api.Filters.DenyDuringImpersonation]
    [Nexo.Api.Filters.EndsBrowserSession]
    [Authorize]
    [EnableRateLimiting("credential-check")]
    public async Task<ActionResult<MfaEnabledDto>> MfaEnable([FromBody] MfaEnableRequest request, [FromServices] MfaService mfa, CancellationToken ct)
        => Ok(await mfa.EnableAsync(_currentUser.UserId, request.Code, ct));

    /// <summary>Turns it off: password plus a code (or a recovery code).</summary>
    [HttpPost("mfa/disable")]
    [Nexo.Api.Filters.DenyDuringImpersonation]
    [Authorize]
    [EnableRateLimiting("credential-check")]
    public async Task<IActionResult> MfaDisable([FromBody] MfaDisableRequest request, [FromServices] MfaService mfa, CancellationToken ct)
    {
        await mfa.DisableAsync(_currentUser.UserId, request, ct);
        return NoContent();
    }

    /// <summary>
    /// Platform operator sign-in through the shared login form. Null = not a platform account
    /// (or wrong password: the tenant login runs next). Locked operators get 429.
    /// </summary>
    private async Task<ActionResult<LoginResponse>?> TryPlatformLoginAsync(LoginRequest request, CancellationToken ct)
    {
        var platform = HttpContext.RequestServices.GetRequiredService<Nexo.Infrastructure.Auth.PlatformAuthService>();
        var result = await platform.SignInAsync(request.Login, request.Password, HttpContext.Connection.RemoteIpAddress?.ToString(),
            Request.Headers.UserAgent.ToString(), ct);
        if (result is null) return null;                                  // not a platform account: the tenant login runs
        if (result.ErrorCode == "account_locked")
            return StatusCode(StatusCodes.Status429TooManyRequests, new
            {
                error = $"Muitas tentativas com senha errada. Tente de novo em {AuthService.LockoutWindow.TotalMinutes:0} minutos.",
                code = "account_locked",
            });
        if (result.ErrorCode is not null) return null;                    // wrong password: same answer as any wrong login
        if (result.MfaChallenge is { } challenge) return Ok(new { mfaRequired = true, mfaChallenge = challenge });
        return SignedIn(PlatformLoginResponse(result.User!, result.Tokens!));
    }

    private static LoginResponse PlatformLoginResponse(Nexo.Domain.Entities.PlatformUser user, Nexo.Infrastructure.Auth.PlatformTokens tokens)
        => new(tokens.AccessToken, tokens.RefreshToken, tokens.AccessTokenExpiresAt, new SessionDto(
            UserId:        user.Id.ToString(),
            TenantId:      string.Empty,
            Name:          user.Email,
            Role:          user.Role,
            Login:         user.Email,
            Email:         user.Email,
            ActiveModules: [],
            StoreId:       null,
            StoreIds:      [],
            CompanyName:   "Orken Platform",
            Type:          "platform")) { RefreshTokenExpiresAt = tokens.RefreshTokenExpiresAt };

    /// <summary>
    /// Rotate a refresh token. Returns a new access token (and keeps the same refresh token
    /// window until expiry — or issues a new one via token rotation).
    /// </summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    [EnableRateLimiting(Nexo.Api.Middleware.AuthRefreshRateLimit.Policy)]
    public async Task<ActionResult<RefreshResponse>> Refresh(
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] RefreshTokenRequest? request,
        CancellationToken ct)
    {
        var refreshToken = _browser.PresentedToken(Request, request?.RefreshToken);
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            // In the cookie mode, someone with no session: a verdict the client understands, not a malformed request.
            return CookieMode
                ? Unauthorized(new { error = "Sessão inválida ou encerrada. Entre de novo.", code = RefreshErrors.Invalid })
                : BadRequest(new { error = "Refresh token is required." });
        }

        var clientIp = HttpContext.RequestServices.GetRequiredService<Nexo.Api.Middleware.ClientIpResolver>().Resolve(HttpContext);

        if (Nexo.Infrastructure.Auth.PlatformAuthService.IsPlatformToken(refreshToken))
        {
            var (tokens, error, retryAfter) = await HttpContext.RequestServices.GetRequiredService<Nexo.Infrastructure.Auth.PlatformAuthService>()
                .RefreshAsync(refreshToken, clientIp, ct);
            return tokens is not null
                ? Renewed(new RefreshResponse(tokens.AccessToken, tokens.AccessTokenExpiresAt, tokens.RefreshToken, tokens.RefreshTokenExpiresAt))
                : (ActionResult)PlatformAuthController.RefreshRefusedResult(this, error, retryAfter);
        }

        var outcome = await _authService.RefreshAsync(new RefreshTokenRequest(refreshToken), clientIp, ct);

        // Neither of these says anything about the session: the client keeps it and comes back after the time given.
        if (outcome.ErrorCode == RefreshErrors.OverBudget)
            return Nexo.Api.Middleware.RateLimitRejection.OverBudgetResult(this, outcome.RetryAfter!.Value);
        if (outcome.ErrorCode == RefreshErrors.Unavailable)
            return Nexo.Api.Middleware.RateLimitRejection.RenewalUnavailableResult(this, outcome.RetryAfter!.Value);

        // 409: outra aba trocou este token há instantes e grava o par novo no armazenamento compartilhado — o
        // cliente usa esse par em vez de encerrar a sessão. Qualquer outra recusa é 401.
        if (outcome.ErrorCode == RefreshErrors.AlreadyRotated)
            return Conflict(new { error = "Esta sessão acabou de ser renovada em outra aba.", code = outcome.ErrorCode });
        if (outcome.Tokens is not { } result)
        {
            // A tab that just reloaded has no access token to hear "session_revoked" from: this answer is where it learns why.
            var code = CookieMode && outcome.PersonChanged ? "session_revoked" : outcome.ErrorCode ?? RefreshErrors.Invalid;
            return Unauthorized(new { error = "Invalid or expired refresh token.", code });
        }

        return Renewed(result);
    }

    /// <summary>Returns the current user's session info from the database.</summary>
    [HttpGet("me")]
    [Authorize]
    public async Task<ActionResult<SessionDto>> Me(CancellationToken ct)
    {
        // Platform token path — no tenant context
        var tokenType = User.FindFirstValue("type");
        if (tokenType == "platform")
        {
            var platformUserId = User.FindFirstValue("platformUserId");
            if (!Guid.TryParse(platformUserId, out var pid)) return Unauthorized();
            var pu = await _db.PlatformUsers.AsNoTracking().FirstOrDefaultAsync(u => u.Id == pid, ct);
            if (pu is null) return Unauthorized();
            return Ok(new SessionDto(
                UserId: pu.Id.ToString(), TenantId: "", Name: pu.Email, Role: pu.Role,
                Login: pu.Email, Email: pu.Email, ActiveModules: new List<string>(),
                StoreId: null, StoreIds: new List<string>(), CompanyName: "Orken", Type: "platform"));
        }

        var user = await _users.GetByIdAsync(_currentUser.UserId, ct);
        if (user is null) return Unauthorized();

        var tenant        = await _tenants.GetByIdAsync(_currentUser.TenantId, ct);
        var activeModules = await _tenants.GetActiveModuleKeysAsync(_currentUser.TenantId, ct);
        var expiredModules = await _tenants.GetExpiredModuleKeysAsync(_currentUser.TenantId, ct);
        var trialEndsAt   = await _tenants.GetTrialEndsAtAsync(_currentUser.TenantId, ct);
        var stores        = await _stores.GetByTenantIdAsync(_currentUser.TenantId, ct);

        return Ok(new SessionDto(
            UserId:        user.Id.ToString(),
            TenantId:      user.TenantId.ToString(),
            Name:          user.FullName,
            Role:          user.Role.ToString().ToLowerInvariant(),
            Login:         user.Login,
            Email:         user.Email,
            ActiveModules: activeModules.ToList(),
            StoreId:       _currentUser.StoreId == Guid.Empty ? null : _currentUser.StoreId.ToString(),
            StoreIds:      stores.Select(s => s.Id.ToString()).ToList(),
            CompanyName:   tenant?.TradeName ?? tenant?.CompanyName ?? "",
            TrialEndsAt:   trialEndsAt,
            RequirePasswordChange: user.RequirePasswordChange,
            ReadOnlyModules: expiredModules.ToList()));
    }

    /// <summary>
    /// Switch the active store context. Issues a new token pair with the requested storeId.
    /// The old refresh token is revoked; a new one is issued.
    /// </summary>
    [HttpPost("switch-store")]
    [Nexo.Api.Filters.DenyDuringImpersonation]
    [Authorize]
    public async Task<ActionResult<SwitchStoreResponse>> SwitchStore(
        [FromBody] SwitchStoreRequest request,
        CancellationToken ct)
    {
        if (!Guid.TryParse(request.StoreId, out var storeId))
            return BadRequest(new { error = "Invalid storeId format." });

        var refreshToken = _browser.PresentedToken(Request, request.RefreshToken) ?? "";

        var outcome = await _authService.SwitchStoreAsync(
            _currentUser.UserId,
            _currentUser.TenantId,
            storeId,
            refreshToken,
            ct);

        if (outcome.ErrorCode == RefreshErrors.AlreadyRotated)
            return Conflict(new { error = "Esta sessão acabou de ser renovada em outra aba. Tente de novo.", code = outcome.ErrorCode });
        // sem o refresh token válido da própria sessão não há par novo (o access token sozinho não abre sessão)
        if (outcome.ErrorCode == RefreshErrors.Invalid)
            return Unauthorized(new { error = "Sessão inválida ou encerrada. Entre de novo.", code = outcome.ErrorCode });
        if (outcome.Response is not { } result)
            return Forbid();

        if (!CookieMode) return Ok(result);
        _browser.SetCookie(Response, result.RefreshToken, result.RefreshTokenExpiresAt);
        return Ok(new { result.AccessToken, result.AccessTokenExpiresAt, result.StoreId, result.Session });
    }

    /// <summary>
    /// Revokes the provided refresh token. Safe to call multiple times.
    /// The access token will expire naturally within its 15-minute window.
    /// </summary>
    /// <summary>
    /// The platform administrator leaves the tenant session: audited with who acted. The access token is also refused from
    /// now on (until it expires anyway, at most 30 minutes after it was issued).
    /// </summary>
    [HttpPost("impersonation/end")]
    [Authorize]
    public async Task<IActionResult> EndImpersonation(CancellationToken ct)
    {
        if (!Nexo.Api.Filters.DenyDuringImpersonationAttribute.IsImpersonation(User))
            return BadRequest(new { error = "Esta sessão não é um acesso da plataforma.", code = "not_impersonation" });
        var jti = User.FindFirstValue(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Jti) ?? User.FindFirstValue("jti");
        if (jti is not null)
        {
            await HttpContext.RequestServices.GetRequiredService<ICacheService>().SetAsync(ImpersonationClaims.EndedKey(jti), new EndedImpersonation(DateTime.UtcNow),
                TimeSpan.FromMinutes(35), ct);
            // nothing about this ends in the database, so no commit tells the hub connections of this token
            await HttpContext.RequestServices.GetRequiredService<Nexo.Infrastructure.Hubs.HubSessionSignals>()
                .PublishAsync(new Nexo.Infrastructure.Hubs.HubSessionChange([], [], [jti]), CancellationToken.None);
        }
        var audit = HttpContext.RequestServices.GetRequiredService<IAuditWriter>();
        Guid.TryParse(User.FindFirstValue("tenantId"), out var tenantId);
        audit.Stage(AuditActions.PlatformImpersonationEnded, AuditSeverity.Warning, "Tenant", tenantId.ToString(),
            $"Platform admin {User.FindFirstValue(ImpersonationClaims.ActorEmail)} left the tenant session.",
            tenantId: tenantId == Guid.Empty ? null : tenantId,
            actorId: Guid.TryParse(User.FindFirstValue(ImpersonationClaims.ActorId), out var actor) ? actor : null,
            actorName: User.FindFirstValue(ImpersonationClaims.ActorEmail), actorType: "platform");
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    public sealed record EndedImpersonation(DateTime At);

    /// <summary>A refresh token fits with room to spare, and nothing bigger is worth binding, hashing and looking up for someone with no session.</summary>
    private const int LogoutBodyLimitBytes = 16 * 1024;

    /// <summary>
    /// Ends the session of the token presented, and its whole family. It takes an access token or, without one, the refresh token itself: whoever holds it could
    /// renew it anyway. That is the way out of a client that lost the race for its own token and is about to give up: its access token has usually expired by then,
    /// and inside the grace this is the only moment the server can hear that someone else holds the new one. In the cookie mode the cookie is that proof, so a tab
    /// that just reloaded, or whose token expired, signs out like any other.
    /// </summary>
    [HttpPost("logout")]
    [AllowAnonymous]
    [EnableRateLimiting(Nexo.Api.Middleware.AuthLogoutRateLimit.Policy)]
    [RequestSizeLimit(LogoutBodyLimitBytes)]
    [Nexo.Api.Filters.RefusesLargeBody(LogoutBodyLimitBytes)]
    public async Task<IActionResult> Logout(
        [FromBody] LogoutRequest? request,
        CancellationToken ct)
    {
        var refreshToken = _browser.PresentedToken(Request, request?.RefreshToken);

        var signedIn = User.Identity?.IsAuthenticated == true;
        if (!signedIn && !CookieMode && string.IsNullOrWhiteSpace(refreshToken)) return Challenge();

        if (User.FindFirstValue("type") == "platform")
        {
            if (Guid.TryParse(User.FindFirstValue("platformUserId"), out var platformUserId) && Guid.TryParse(User.FindFirstValue("sid"), out var sid))
                await HttpContext.RequestServices.GetRequiredService<Nexo.Infrastructure.Auth.PlatformAuthService>().LogoutAsync(platformUserId, sid, ct);
        }
        else if (Nexo.Infrastructure.Auth.PlatformAuthService.IsPlatformToken(refreshToken))
        {
            await HttpContext.RequestServices.GetRequiredService<Nexo.Infrastructure.Auth.PlatformAuthService>().LogoutByRefreshTokenAsync(refreshToken!, ct);
        }
        else if (!string.IsNullOrWhiteSpace(refreshToken))
        {
            await _authService.LogoutAsync(refreshToken, ct);
        }

        _browser.ExpireOldCookies(Response);
        if (CookieMode) _browser.ExpireCookie(Response);
        return NoContent();
    }

    // ── Account recovery & sessions ──────────────────────────────────────────

    /// <summary>
    /// Asks for a password-reset link by e-mail. Always answers the same (202), whether or not an
    /// account uses that address — no account enumeration. Rate-limited like login.
    /// </summary>
    [HttpPost("forgot-password")]
    [EnableRateLimiting("auth-login")]
    [AllowAnonymous]
    public async Task<IActionResult> ForgotPassword(
        [FromBody] ForgotPasswordRequest request, [FromServices] AccountSecurityService accounts, CancellationToken ct)
    {
        await accounts.RequestPasswordResetAsync(request.Email, ct);
        return Accepted(new { message = "Se houver uma conta com este e-mail, enviamos um link para criar uma nova senha." });
    }

    /// <summary>Sets a new password from the e-mailed link (one use, 1 h). Ends every session.</summary>
    [HttpPost("reset-password")]
    [EnableRateLimiting("auth-login")]
    [AllowAnonymous]
    public async Task<IActionResult> ResetPassword(
        [FromBody] ResetPasswordRequest request, [FromServices] AccountSecurityService accounts, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.NewPassword) || request.NewPassword.Length < AccountSecurityService.MinPasswordLength)
            return BadRequest(new { error = $"A senha deve ter no mínimo {AccountSecurityService.MinPasswordLength} caracteres." });
        if (!await accounts.ResetPasswordAsync(request.Token, request.NewPassword, ct))
            return BadRequest(new { error = "Este link é inválido ou expirou. Peça um novo." });
        return Ok(new { message = "Senha alterada. Entre com a nova senha." });
    }

    /// <summary>The signed-in user's active sessions (devices).</summary>
    [HttpGet("sessions")]
    [Authorize]
    public async Task<ActionResult<IReadOnlyList<UserSessionDto>>> Sessions(
        [FromServices] AccountSecurityService accounts, CancellationToken ct)
        => Ok(await accounts.ListSessionsAsync(_currentUser.UserId, ct));

    /// <summary>"Sair de todos os dispositivos": this one included, cookies removed.</summary>
    [HttpPost("sessions/revoke-all")]
    [Nexo.Api.Filters.DenyDuringImpersonation]
    [Authorize]
    public async Task<IActionResult> RevokeAllSessions(
        [FromBody] RevokeAllSessionsRequest request, [FromServices] AccountSecurityService accounts, CancellationToken ct)
    {
        // The JSON body forces a preflight: a cross-site <form> cannot sign the user out everywhere. The action also takes an access
        // token, which a browser never attaches by itself.
        _ = request;
        await accounts.SignOutEverywhereAsync(_currentUser.UserId, ct);
        _browser.ExpireOldCookies(Response);
        if (CookieMode) _browser.ExpireCookie(Response);
        return NoContent();
    }

    /// <summary>
    /// Self-registration: creates tenant + store + user + subscription + settings atomically.
    /// Sends a verification email. User must verify before logging in.
    /// </summary>
    [HttpPost("register")]
    [EnableRateLimiting("auth-login")]
    [AllowAnonymous]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length < 2)
            return BadRequest(new { error = "Informe seu nome completo." });
        if (request.Name.Trim().Length > 150)
            return BadRequest(new { error = "Use um nome de até 150 caracteres." });
        if (string.IsNullOrWhiteSpace(request.Email))
            return BadRequest(new { error = "Informe um e-mail válido." });
        // The login of this account is the e-mail, and the column takes 50: past that the insert fails with the company already saved.
        if (request.Email.Trim().Length > 50)
            return BadRequest(new { error = "Use um e-mail de até 50 caracteres." });
        if (string.IsNullOrWhiteSpace(request.Password) || request.Password.Length < 6)
            return BadRequest(new { error = "A senha deve ter no mínimo 6 caracteres." });

        var errorCode = await _registration.RegisterAsync(request, ct);
        if (errorCode == "email_already_registered")
            return Conflict(new { error = "Este e-mail já está cadastrado.", code = errorCode });
        if (errorCode is not null)
            return BadRequest(new { error = "Erro ao criar conta.", code = errorCode });

        return Ok(new { message = "verification_email_sent" });
    }

    /// <summary>
    /// Email verification: validates the one-time token and activates the user.
    /// Returns access + refresh tokens for auto-login.
    /// </summary>
    [HttpGet("verify-email")]
    [AllowAnonymous]
    public async Task<ActionResult<LoginResponse>> VerifyEmail([FromQuery] string token, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token))
            return BadRequest(new { error = "Token inválido." });

        var result = await _registration.VerifyEmailAsync(token, ct);
        if (result is null)
            return BadRequest(new { error = "Link inválido ou expirado." });

        return SignedIn(result);
    }

    /// <summary>
    /// Resends the verification email. Silently succeeds even if email not found
    /// to prevent email enumeration attacks.
    /// </summary>
    [HttpPost("resend-verification")]
    [EnableRateLimiting("auth-login")]
    [AllowAnonymous]
    public async Task<IActionResult> ResendVerification([FromBody] ResendVerificationRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Email))
            return BadRequest(new { error = "Informe o e-mail." });
        await _registration.ResendVerificationAsync(request.Email.Trim().ToLowerInvariant(), ct);
        return Ok(new { message = "Email reenviado se o endereço estiver cadastrado." });
    }

    /// <summary>
    /// Manager challenge — validates manager credentials without creating a session.
    /// Used before sensitive operations (sale cancellation, high discounts, etc).
    /// </summary>
    [HttpPost("verify-manager")]
    [EnableRateLimiting("credential-check")]
    [Authorize]
    public async Task<ActionResult<VerifyManagerResponse>> VerifyManager(
        [FromBody] VerifyManagerRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Login) || string.IsNullOrWhiteSpace(request.Password))
            return BadRequest(new { error = "Login and password are required." });

        var result = await _authService.VerifyManagerAsync(request, ct);
        return Ok(result);
    }
}

public sealed record ForgotPasswordRequest(string? Email);
public sealed record ResetPasswordRequest(string? Token, string NewPassword);
public sealed record RevokeAllSessionsRequest(bool Confirm = true);
