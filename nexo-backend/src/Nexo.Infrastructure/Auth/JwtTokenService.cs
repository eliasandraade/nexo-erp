using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using Nexo.Application.Common.Interfaces;
using Nexo.Domain.Entities;

namespace Nexo.Infrastructure.Auth;

/// <summary>
/// Generates signed HS256 JWT access tokens (15min) and refresh tokens (7d).
///
/// Access token claims:
///   sub, jti, userId, tenantId, tenantSlug, name, role, activeModules
///
/// Refresh token claims:
///   sub, jti, userId, tenantId (minimal — only what's needed for refresh)
/// </summary>
public class JwtTokenService : IJwtTokenService
{
    private readonly IConfiguration _config;

    public JwtTokenService(IConfiguration config) => _config = config;

    public TokenPair GenerateTokenPair(
        User user,
        string tenantSlug,
        IReadOnlyList<string> activeModules)
    {
        var accessExpiry = GetAccessTokenExpiration();
        var refreshExpiry = GetRefreshTokenExpiration();

        var accessToken = BuildAccessToken(user, tenantSlug, activeModules, accessExpiry);
        var refreshToken = BuildRefreshToken(user, refreshExpiry);

        return new TokenPair(accessToken, refreshToken, accessExpiry, refreshExpiry);
    }

    public RefreshTokenClaims? ValidateRefreshToken(string refreshToken)
    {
        try
        {
            var key = GetSigningKey();
            var handler = new JwtSecurityTokenHandler();

            var principal = handler.ValidateToken(refreshToken, new TokenValidationParameters
            {
                ValidateIssuer           = true,
                ValidateAudience         = true,
                ValidateLifetime         = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer              = GetIssuer(),
                ValidAudience            = GetRefreshAudience(),
                IssuerSigningKey         = key,
                ClockSkew                = TimeSpan.FromMinutes(1),
            }, out _);

            var jti      = principal.FindFirstValue(JwtRegisteredClaimNames.Jti);
            var userId   = principal.FindFirstValue("userId");
            var tenantId = principal.FindFirstValue("tenantId");

            if (jti is null || userId is null || tenantId is null) return null;

            return new RefreshTokenClaims(
                Jti:      jti,
                UserId:   Guid.Parse(userId),
                TenantId: Guid.Parse(tenantId));
        }
        catch
        {
            return null;
        }
    }

    public DateTime GetAccessTokenExpiration()
    {
        var minutes = _config.GetValue<int>("Jwt:AccessTokenMinutes", 15);
        return DateTime.UtcNow.AddMinutes(minutes);
    }

    public DateTime GetRefreshTokenExpiration()
    {
        var days = _config.GetValue<int>("Jwt:RefreshTokenDays", 7);
        return DateTime.UtcNow.AddDays(days);
    }

    // ── Private builders ─────────────────────────────────────────────────────

    private string BuildAccessToken(
        User user,
        string tenantSlug,
        IReadOnlyList<string> activeModules,
        DateTime expiry)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new("userId",      user.Id.ToString()),
            new("tenantId",    user.TenantId.ToString()),
            new("tenantSlug",  tenantSlug),
            new("name",        user.FullName),
            new("role",        user.Role.ToString().ToLowerInvariant()),
            new(ClaimTypes.Role, user.Role.ToString()),
        };

        // Active modules embedded in JWT — avoids a Redis call on every request
        // for the first 15 minutes. On refresh, the list is re-evaluated.
        foreach (var module in activeModules)
            claims.Add(new Claim("module", module));

        return BuildToken(claims, GetAudience(), expiry);
    }

    private string BuildRefreshToken(User user, DateTime expiry)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new("userId",   user.Id.ToString()),
            new("tenantId", user.TenantId.ToString()),
        };

        return BuildToken(claims, GetRefreshAudience(), expiry);
    }

    private string BuildToken(IEnumerable<Claim> claims, string audience, DateTime expiry)
    {
        var key = GetSigningKey();
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer:             GetIssuer(),
            audience:           audience,
            claims:             claims,
            notBefore:          DateTime.UtcNow,
            expires:            expiry,
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private SymmetricSecurityKey GetSigningKey()
    {
        var secret = _config["Jwt:Secret"]
            ?? throw new InvalidOperationException("JWT secret is not configured (Jwt:Secret).");

        if (secret.Length < 32)
            throw new InvalidOperationException("JWT secret must be at least 32 characters.");

        return new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
    }

    private string GetIssuer()        => _config["Jwt:Issuer"]   ?? "nexo-api";
    private string GetAudience()      => _config["Jwt:Audience"] ?? "nexo-frontend";
    private string GetRefreshAudience() => _config["Jwt:RefreshAudience"] ?? "nexo-refresh";
}
