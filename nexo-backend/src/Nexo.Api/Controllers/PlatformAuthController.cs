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

    public PlatformAuthController(PlatformAuthService auth) => _auth = auth;

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
    [EnableRateLimiting("auth-login")]
    public async Task<IActionResult> Refresh([FromBody] PlatformRefreshRequest request, CancellationToken ct)
    {
        var (tokens, error) = await _auth.RefreshAsync(request.RefreshToken ?? "", ct);
        return tokens is not null ? Ok(TokensView(tokens)) : RefreshRefused(error);
    }

    [HttpPost("logout")]
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
    public async Task<IActionResult> StartMfaSetup([FromBody] PlatformPasswordRequest request, CancellationToken ct)
    {
        var setup = await _auth.StartMfaSetupAsync(UserId, request.Password, ct);
        return Ok(new { secret = setup.Secret, otpauthUri = setup.OtpAuthUri });
    }

    /// <summary>Turns it on; answers the recovery codes once. Every session ends (sign in again with the code).</summary>
    [HttpPost("mfa/enable")]
    public async Task<IActionResult> EnableMfa([FromBody] PlatformCodeRequest request, CancellationToken ct)
        => Ok(new { recoveryCodes = await _auth.EnableMfaAsync(UserId, request.Code, ct) });

    [HttpPost("mfa/disable")]
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
        await _auth.AdminResetMfaAsync(id, UserId, User.FindFirstValue("email"), ct);
        return NoContent();
    }

    internal static object TokensView(PlatformTokens t) => new
    {
        accessToken = t.AccessToken,
        expiresAt = t.AccessTokenExpiresAt,
        refreshToken = t.RefreshToken,
        refreshTokenExpiresAt = t.RefreshTokenExpiresAt,
    };

    internal static IActionResult RefreshRefusedResult(ControllerBase c, string? error) => error == "refresh_already_rotated"
        ? c.Conflict(new { error = "Esta sessão acabou de ser renovada em outra aba.", code = error })
        : c.Unauthorized(new { error = "Sessão da plataforma encerrada. Entre de novo.", code = error ?? "session_ended" });

    private IActionResult RefreshRefused(string? error) => RefreshRefusedResult(this, error);

    private IActionResult Answer(PlatformSignIn result)
    {
        if (result.Tokens is { } tokens)
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
