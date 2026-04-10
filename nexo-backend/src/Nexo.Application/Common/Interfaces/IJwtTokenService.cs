using Nexo.Domain.Entities;

namespace Nexo.Application.Common.Interfaces;

/// <summary>Result of a token generation — access token + refresh token.</summary>
public record TokenPair(
    string AccessToken,
    string RefreshToken,
    DateTime AccessTokenExpiresAt,
    DateTime RefreshTokenExpiresAt);

public interface IJwtTokenService
{
    /// <summary>
    /// Generates an access token (15min) + refresh token (7d) for the given user.
    /// The access token includes: userId, tenantId, tenantSlug, role, activeModules, jti.
    /// </summary>
    TokenPair GenerateTokenPair(User user, string tenantSlug, IReadOnlyList<string> activeModules);

    /// <summary>
    /// Validates a refresh token signature and returns its claims.
    /// Returns null if the token is invalid or expired.
    /// </summary>
    RefreshTokenClaims? ValidateRefreshToken(string refreshToken);

    /// <summary>Returns when the access token expires (UTC) from now.</summary>
    DateTime GetAccessTokenExpiration();

    /// <summary>Returns when the refresh token expires (UTC) from now.</summary>
    DateTime GetRefreshTokenExpiration();
}

public record RefreshTokenClaims(
    string Jti,
    Guid UserId,
    Guid TenantId);
