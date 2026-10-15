using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Nexo.Application.Common.Interfaces;
using Nexo.Infrastructure.Persistence;

namespace Nexo.Api.Controllers;

/// <summary>
/// Authentication endpoint for NexoERP platform administrators (PlatformUser).
/// Separate from tenant user auth (/api/auth) — no tenant/store context.
/// </summary>
[ApiController]
[Route("api/platform/auth")]
public class PlatformAuthController : ControllerBase
{
    private readonly NexoDbContext _context;
    private readonly IPasswordHasher _hasher;
    private readonly IJwtTokenService _jwt;

    public PlatformAuthController(NexoDbContext context, IPasswordHasher hasher, IJwtTokenService jwt)
    {
        _context = context;
        _hasher  = hasher;
        _jwt     = jwt;
    }

    /// <summary>
    /// Authenticates a platform admin and returns a short-lived access token.
    /// </summary>
    [HttpPost("login")]
    [EnableRateLimiting("auth-login")]
    public async Task<IActionResult> Login([FromBody] PlatformLoginRequest request, CancellationToken ct)
    {
        var user = await _context.PlatformUsers
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Email == request.Email.Trim().ToLowerInvariant(), ct);

        if (user is null) return Unauthorized(new { error = "Credenciais inválidas." });

        // per-account lockout, shared with the platform path of /auth/login
        var attempts = HttpContext.RequestServices.GetRequiredService<Nexo.Application.Features.Auth.LoginAttemptGuard>();
        var account = Nexo.Application.Features.Auth.LoginAttemptGuard.ForPlatform(user.Id);
        if (await attempts.IsLockedAsync(account, ct))
            return StatusCode(StatusCodes.Status429TooManyRequests, new { error = "Muitas tentativas com senha errada. Tente de novo em 15 minutos." });
        if (!_hasher.Verify(request.Password, user.PasswordHash))
        {
            await attempts.RegisterFailureAsync(account, ct);
            return Unauthorized(new { error = "Credenciais inválidas." });
        }
        await attempts.ResetAsync(account, ct);

        var result = _jwt.GeneratePlatformToken(user);

        return Ok(new
        {
            accessToken = result.AccessToken,
            expiresAt   = result.ExpiresAt,
            email       = user.Email,
            role        = user.Role,
        });
    }
}

public record PlatformLoginRequest(string Email, string Password);
