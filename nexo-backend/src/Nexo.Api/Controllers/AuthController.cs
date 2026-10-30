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
        IJwtTokenService jwt)
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
    }

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

    /// <summary>Sets the auth cookies and returns the session (login and the second step share this).</summary>
    private ActionResult<LoginResponse> SignedIn(LoginResponse response)
    {
        // Set httpOnly cookies for tokens
        // Secure=true always except local Development — Railway terminates TLS at proxy,
        // so IsHttps is false inside the container even on production HTTPS.
        var isSecure = !string.Equals(
            HttpContext.RequestServices.GetRequiredService<IWebHostEnvironment>().EnvironmentName,
            "Development", StringComparison.OrdinalIgnoreCase);
        var cookieOptions = new CookieOptions
        {
            HttpOnly = true,
            Secure   = isSecure,
            // SameSite=None required for cross-site cookies (frontend on orken.com.br, backend on railway.app)
            // Strict would silently drop cookies on cross-origin requests even with credentials:include
            SameSite = isSecure ? SameSiteMode.None : SameSiteMode.Strict,
            Path     = "/",
            Domain   = null
        };

        var accessCookieOptions = new CookieOptions(cookieOptions)
        {
            Expires = response.AccessTokenExpiresAt
        };
        var refreshCookieOptions = new CookieOptions(cookieOptions)
        {
            // Refresh token cookie expires after 7 days (server controls actual expiry via Redis)
            Expires = DateTimeOffset.UtcNow.AddDays(7)
        };
        
        Response.Cookies.Append("nexo_access", response.AccessToken, accessCookieOptions);
        Response.Cookies.Append("nexo_refresh", response.RefreshToken, refreshCookieOptions);
        return Ok(new LoginResponse(response.AccessToken, response.RefreshToken, response.AccessTokenExpiresAt, response.Session));
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
            if (platform.Tokens is not null) return Ok(PlatformLoginResponse(platform.User!, platform.Tokens));
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
        return Ok(PlatformLoginResponse(result.User!, result.Tokens!));
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
            Type:          "platform"));

    /// <summary>
    /// Rotate a refresh token. Returns a new access token (and keeps the same refresh token
    /// window until expiry — or issues a new one via token rotation).
    /// </summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    [EnableRateLimiting("auth-login")]
    public async Task<ActionResult<RefreshResponse>> Refresh(
        [FromBody] RefreshTokenRequest request,
        CancellationToken ct)
    {
        string refreshToken = request.RefreshToken;
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            // Try to get from cookie
            refreshToken = Request.Cookies["nexo_refresh"];
            if (string.IsNullOrWhiteSpace(refreshToken))
                return BadRequest(new { error = "Refresh token is required." });
        }

        if (Nexo.Infrastructure.Auth.PlatformAuthService.IsPlatformToken(refreshToken))
        {
            var (tokens, error) = await HttpContext.RequestServices.GetRequiredService<Nexo.Infrastructure.Auth.PlatformAuthService>()
                .RefreshAsync(refreshToken, ct);
            return tokens is not null
                ? Ok(new RefreshResponse(tokens.AccessToken, tokens.AccessTokenExpiresAt, tokens.RefreshToken, tokens.RefreshTokenExpiresAt))
                : (ActionResult)PlatformAuthController.RefreshRefusedResult(this, error);
        }

        var result = await _authService.RefreshAsync(new RefreshTokenRequest(refreshToken), ct);

        if (result is null)
            return Unauthorized(new { error = "Invalid or expired refresh token." });

        // Update cookies — same Secure/SameSite logic as login
        var isSecure = !string.Equals(
            HttpContext.RequestServices.GetRequiredService<IWebHostEnvironment>().EnvironmentName,
            "Development", StringComparison.OrdinalIgnoreCase);
        var cookieOptions = new CookieOptions
        {
            HttpOnly = true,
            Secure   = isSecure,
            SameSite = isSecure ? SameSiteMode.None : SameSiteMode.Strict,
            Path     = "/",
            Domain   = null
        };

        var accessCookieOptions = new CookieOptions(cookieOptions)
        {
            Expires = result.AccessTokenExpiresAt
        };
        var refreshCookieOptions = new CookieOptions(cookieOptions)
        {
            Expires = result.RefreshTokenExpiresAt
        };

        Response.Cookies.Append("nexo_access", result.AccessToken, accessCookieOptions);
        Response.Cookies.Append("nexo_refresh", result.RefreshToken, refreshCookieOptions);

        return Ok(new RefreshResponse(result.AccessToken, result.AccessTokenExpiresAt, result.RefreshToken, result.RefreshTokenExpiresAt));
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
            RequirePasswordChange: user.RequirePasswordChange));
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

        // Prefer body refresh token; fall back to cookie (cookie-based auth flow)
        var refreshToken = request.RefreshToken;
        if (string.IsNullOrWhiteSpace(refreshToken))
            refreshToken = Request.Cookies["nexo_refresh"] ?? "";

        var result = await _authService.SwitchStoreAsync(
            _currentUser.UserId,
            _currentUser.TenantId,
            storeId,
            refreshToken,
            ct);

        if (result is null)
            return Forbid();

        // Issue new cookies so the SPA immediately uses the store-scoped tokens
        var isSecure = !string.Equals(
            HttpContext.RequestServices.GetRequiredService<IWebHostEnvironment>().EnvironmentName,
            "Development", StringComparison.OrdinalIgnoreCase);
        var cookieOptions = new CookieOptions
        {
            HttpOnly = true,
            Secure   = isSecure,
            SameSite = isSecure ? SameSiteMode.None : SameSiteMode.Strict,
            Path     = "/",
            Domain   = null
        };
        Response.Cookies.Append("nexo_access",  result.AccessToken,
            new CookieOptions(cookieOptions) { Expires = result.AccessTokenExpiresAt });
        Response.Cookies.Append("nexo_refresh", result.RefreshToken,
            new CookieOptions(cookieOptions) { Expires = result.RefreshTokenExpiresAt });

        return Ok(result);
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
            await HttpContext.RequestServices.GetRequiredService<ICacheService>().SetAsync($"impersonation:ended:{jti}", new EndedImpersonation(DateTime.UtcNow),
                TimeSpan.FromMinutes(35), ct);
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

    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout(
        [FromBody] LogoutRequest? request,
        CancellationToken ct)
    {
        string refreshToken = request?.RefreshToken;
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            // Try to get from cookie
            refreshToken = Request.Cookies["nexo_refresh"];
        }
        
        if (User.FindFirstValue("type") == "platform")
        {
            if (Guid.TryParse(User.FindFirstValue("platformUserId"), out var platformUserId) && Guid.TryParse(User.FindFirstValue("sid"), out var sid))
                await HttpContext.RequestServices.GetRequiredService<Nexo.Infrastructure.Auth.PlatformAuthService>().LogoutAsync(platformUserId, sid, ct);
        }
        else if (!string.IsNullOrWhiteSpace(refreshToken))
        {
            await _authService.LogoutAsync(refreshToken, ct);
        }
        
        // Clear cookies — same Secure/SameSite logic as login/refresh
        var isSecure = !string.Equals(
            HttpContext.RequestServices.GetRequiredService<IWebHostEnvironment>().EnvironmentName,
            "Development", StringComparison.OrdinalIgnoreCase);
        var cookieOptions = new CookieOptions
        {
            HttpOnly = true,
            Secure   = isSecure,
            SameSite = isSecure ? SameSiteMode.None : SameSiteMode.Strict,
            Path     = "/",
            Domain   = null,
            Expires  = DateTimeOffset.UtcNow.AddSeconds(-1)
        };
        Response.Cookies.Append("nexo_access", "", cookieOptions);
        Response.Cookies.Append("nexo_refresh", "", cookieOptions);
        
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

    /// <summary>"Sair de todos os dispositivos" — including this one (cookies cleared).</summary>
    [HttpPost("sessions/revoke-all")]
    [Nexo.Api.Filters.DenyDuringImpersonation]
    [Authorize]
    public async Task<IActionResult> RevokeAllSessions(
        [FromBody] RevokeAllSessionsRequest request, [FromServices] AccountSecurityService accounts, CancellationToken ct)
    {
        // The JSON body is the CSRF guard: with SameSite=None cookies, a cross-site <form> could
        // otherwise sign the user out everywhere; it can't send application/json without preflight.
        _ = request;
        await accounts.SignOutEverywhereAsync(_currentUser.UserId, ct);
        // Secure is fine on localhost too (browsers treat it as a secure context)
        var expired = new CookieOptions
        {
            HttpOnly = true, Secure = true, SameSite = SameSiteMode.None,
            Path = "/", Expires = DateTimeOffset.UtcNow.AddSeconds(-1),
        };
        Response.Cookies.Append("nexo_access", "", expired);
        Response.Cookies.Append("nexo_refresh", "", expired);
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
        if (string.IsNullOrWhiteSpace(request.Email))
            return BadRequest(new { error = "Informe um e-mail válido." });
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

        return Ok(result);
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
