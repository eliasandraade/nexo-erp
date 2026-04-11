using Nexo.Domain.Entities;

namespace Nexo.Application.Common.Interfaces;

/// <summary>Platform-only access token (no tenant, no store, no refresh).</summary>
public record PlatformTokenResult(string AccessToken, DateTime ExpiresAt);

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
    /// The access token includes: userId, tenantId, tenantSlug, role, activeModules, storeId, store[].
    /// </summary>
    TokenPair GenerateTokenPair(
        User user,
        string tenantSlug,
        IReadOnlyList<string> activeModules,
        Guid storeId,
        IReadOnlyList<Guid> accessibleStoreIds);

    /// <summary>
    /// Validates a refresh token signature and returns its claims.
    /// Returns null if the token is invalid or expired.
    /// </summary>
    RefreshTokenClaims? ValidateRefreshToken(string refreshToken);

    /// <summary>Returns when the access token expires (UTC) from now.</summary>
    DateTime GetAccessTokenExpiration();

    /// <summary>Returns when the refresh token expires (UTC) from now.</summary>
    DateTime GetRefreshTokenExpiration();

    /// <summary>
    /// Generates a short-lived access token for a platform admin (no tenant/store claims).
    /// Audience: "nexo-platform". Expires in 8 hours.
    /// </summary>
    PlatformTokenResult GeneratePlatformToken(PlatformUser user);
}

public record RefreshTokenClaims(
    string Jti,
    Guid UserId,
    Guid TenantId);
