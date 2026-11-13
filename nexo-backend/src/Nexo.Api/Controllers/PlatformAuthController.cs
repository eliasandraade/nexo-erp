using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Nexo.Infrastructure.Auth;

namespace Nexo.Api.Controllers;

/// <summary>
/// Authentication of platform administrators (PlatformUser): sign-in with two-step verification, revocable sessions,
/// refresh with rotation, logout. Separate from tenant user auth (/api/auth) — no tenant/store context. The shared login
/// form (/api/auth/login) reaches the same service for e-mail logins of platform accounts.
/// </summary>
[ApiController]
[Route("api/platform/auth")]
[Authorize(Policy = "Platform")]
public class PlatformAuthController : ControllerBase
{
    private readonly PlatformAuthService _auth;
    private readonly Nexo.Api.Middleware.BrowserSession _browser;

    public PlatformAuthController(PlatformAuthService auth, Nexo.Api.Middleware.BrowserSession browser)
    {
        _auth = auth;
        _browser = browser;
    }

    private bool CookieMode => Nexo.Api.Middleware.BrowserSession.IsCookieMode(Request);

    private Guid UserId => Guid.Parse(User.FindFirstValue("platformUserId")!);
    private Guid? SessionId => Guid.TryParse(User.FindFirstValue("sid"), out var id) ? id : null;

    /// <summary>Password; then either the session (tokens) or a two-step challenge (<c>mfaRequired</c>).</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("auth-login")]
    public async Task<IActionResult> Login([FromBody] PlatformLoginRequest request, CancellationToken ct)
    {
        var result = await _auth.SignInAsync(request.Email ?? "", request.Password ?? "", HttpContext.Connection.RemoteIpAddress?.ToString(),
            Request.Headers.UserAgent.ToString(), ct);
        return Answer(result ?? new PlatformSignIn(null, null, "invalid_credentials", null));
    }

    [HttpPost("mfa/verify")]
    [AllowAnonymous]
    [EnableRateLimiting("auth-login")]
    public async Task<IActionResult> VerifyMfa([FromBody] PlatformMfaVerifyRequest request, CancellationToken ct)
        => Answer(await _auth.VerifyMfaAsync(request.MfaChallenge ?? "", request.Code, request.RecoveryCode, ct));

    [HttpPost("refresh")]
    [AllowAnonymous]
    [EnableRateLimiting(Nexo.Api.Middleware.AuthRefreshRateLimit.Policy)]
    [RequestSizeLimit(AuthController.TokenBodyLimitBytes)]
    [Nexo.Api.Filters.RefusesLargeBody(AuthController.TokenBodyLimitBytes)]
    public async Task<IActionResult> Refresh(
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] PlatformRefreshRequest? request,
        CancellationToken ct)
    {
        var clientIp = HttpContext.RequestServices.GetRequiredService<Nexo.Api.Middleware.ClientIpResolver>().Resolve(HttpContext);
        var (tokens, error, retryAfter) = await _auth.RefreshAsync(_browser.PresentedToken(Request, request?.RefreshToken) ?? "", clientIp, ct);
        if (tokens is null) return RefreshRefusedResult(this, error, retryAfter);
        if (!CookieMode) return Ok(TokensView(tokens));
        _browser.SetCookie(Response, tokens.RefreshToken, tokens.RefreshTokenExpiresAt);
        return Ok(new { accessToken = tokens.AccessToken, expiresAt = tokens.AccessTokenExpiresAt });
    }

    [HttpPost("logout")]
    [Nexo.Api.Filters.EndsBrowserSession]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        if (SessionId is { } sid) await _auth.LogoutAsync(UserId, sid, ct);
        return NoContent();
    }

    /// <summary>Two-step verification status and the open sessions of the signed-in administrator.</summary>
    [HttpGet("security")]
    public async Task<IActionResult> Security(CancellationToken ct)
    {
        var (enabled, pending, left) = await _auth.MfaStatusAsync(UserId, ct);
        return Ok(new
        {
            mfa = new { enabled, setupPending = pending, recoveryCodesLeft = left },
            currentSessionMfaVerified = User.HasClaim("platform_mfa", "true"),
            sessions = await _auth.SessionsAsync(UserId, SessionId, ct),
        });
    }

    [HttpPost("mfa/setup")]
    [EnableRateLimiting("credential-check")]
    public async Task<IActionResult> StartMfaSetup([FromBody] PlatformPasswordRequest request, CancellationToken ct)
    {
        var setup = await _auth.StartMfaSetupAsync(UserId, request.Password, ct);
        return Ok(new { secret = setup.Secret, otpauthUri = setup.OtpAuthUri });
    }

    /// <summary>Turns it on; answers the recovery codes once. Every session ends (sign in again with the code).</summary>
    [HttpPost("mfa/enable")]
    [Nexo.Api.Filters.EndsBrowserSession]
    public async Task<IActionResult> EnableMfa([FromBody] PlatformCodeRequest request, CancellationToken ct)
        => Ok(new { recoveryCodes = await _auth.EnableMfaAsync(UserId, request.Code, ct) });

    [HttpPost("mfa/disable")]
    [EnableRateLimiting("credential-check")]
    [Nexo.Api.Filters.EndsBrowserSession]
    public async Task<IActionResult> DisableMfa([FromBody] PlatformMfaDisableRequest request, CancellationToken ct)
    {
        await _auth.DisableMfaAsync(UserId, request.Password, request.Code, request.RecoveryCode, ct);
        return NoContent();
    }

    [HttpDelete("sessions/{id:guid}")]
    public async Task<IActionResult> RevokeSession(Guid id, CancellationToken ct)
    {
        await _auth.RevokeSessionAsync(UserId, id, ct);
        return NoContent();
    }

    /// <summary>Ends every session of the signed-in administrator, this one included.</summary>
    [HttpPost("sessions/revoke-all")]
    [Nexo.Api.Filters.EndsBrowserSession]
    public async Task<IActionResult> RevokeAll(CancellationToken ct)
    {
        await _auth.RevokeAllAsync(UserId, UserId, "encerradas pelo próprio administrador", ct);
        return NoContent();
    }

    /// <summary>Another super administrator turns two-step verification off for someone who lost the phone and the codes.</summary>
    [HttpPost("users/{id:guid}/mfa/reset")]
    [Authorize(Policy = "PlatformAdmin")]
    public async Task<IActionResult> ResetMfa(Guid id, CancellationToken ct)
    {
        await _auth.AdminResetMfaAsync(id, UserId, User.FindFirstValue(ClaimTypes.Email) ?? User.FindFirstValue("email"), ct);
        return NoContent();
    }

    internal static object TokensView(PlatformTokens t) => new
    {
        accessToken = t.AccessToken,
        expiresAt = t.AccessTokenExpiresAt,
        refreshToken = t.RefreshToken,
        refreshTokenExpiresAt = t.RefreshTokenExpiresAt,
    };

    internal static IActionResult RefreshRefusedResult(ControllerBase c, string? error, TimeSpan? retryAfter)
    {
        if (error == "refresh_already_rotated") return Nexo.Api.Middleware.RateLimitRejection.AlreadyRotatedResult(c, retryAfter ?? PlatformAuthService.RotationGrace);
        if (retryAfter is { } wait) return Nexo.Api.Middleware.RateLimitRejection.OverBudgetResult(c, wait);
        return c.Unauthorized(new { error = "Sessão da plataforma encerrada. Entre de novo.", code = error ?? "session_ended" });
    }

    private IActionResult Answer(PlatformSignIn result)
    {
        if (result.Tokens is { } tokens)
        {
            if (!CookieMode)
                return Ok(new
                {
                    accessToken = tokens.AccessToken,
                    expiresAt = tokens.AccessTokenExpiresAt,
                    refreshToken = tokens.RefreshToken,
                    refreshTokenExpiresAt = tokens.RefreshTokenExpiresAt,
                    email = result.User!.Email,
                    role = result.User.Role,
                    mfaVerified = result.User.MfaEnabled,
                });
            _browser.SetCookie(Response, tokens.RefreshToken, tokens.RefreshTokenExpiresAt);
            return Ok(new
            {
                accessToken = tokens.AccessToken,
                expiresAt = tokens.AccessTokenExpiresAt,
                email = result.User!.Email,
                role = result.User.Role,
                mfaVerified = result.User.MfaEnabled,
            });
        }
        if (result.MfaChallenge is { } challenge) return Ok(new { mfaRequired = true, mfaChallenge = challenge });
        return result.ErrorCode switch
        {
            "account_locked" => StatusCode(StatusCodes.Status429TooManyRequests, new { error = "Muitas tentativas. Tente de novo em 15 minutos.", code = "account_locked" }),
            "invalid_challenge" => Unauthorized(new { error = "A verificação expirou. Entre com sua senha de novo.", code = "invalid_challenge" }),
            "invalid_code" => Unauthorized(new { error = "Código inválido.", code = "invalid_code" }),
            _ => Unauthorized(new { error = "Credenciais inválidas." }),
        };
    }
}

public record PlatformLoginRequest(string? Email, string? Password);
public record PlatformMfaVerifyRequest(string? MfaChallenge, string? Code, string? RecoveryCode);
public record PlatformRefreshRequest(string? RefreshToken);
public record PlatformPasswordRequest(string? Password);
public record PlatformCodeRequest(string? Code);
public record PlatformMfaDisableRequest(string? Password, string? Code, string? RecoveryCode);
