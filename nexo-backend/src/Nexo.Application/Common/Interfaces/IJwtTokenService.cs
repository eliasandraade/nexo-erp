using Nexo.Domain.Entities;

namespace Nexo.Application.Common.Interfaces;

/// <summary>Platform-only access token (no tenant, no store). Refresh goes through the platform session, never the JWT.</summary>
public record PlatformTokenResult(string AccessToken, DateTime ExpiresAt);

/// <summary>
/// A tenant session opened by a platform administrator: the target user's claims plus who really acts and why. Access
/// token only — it never comes with a refresh token, so it can't turn into a normal session.
/// </summary>
public record ImpersonationGrant(
    User User, string TenantSlug, string CompanyName, IReadOnlyList<string> ActiveModules, Guid StoreId, IReadOnlyList<Guid> StoreIds,
    Guid ActorId, string ActorEmail, string Reason, DateTime ExpiresAt);

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
        string companyName,
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
    /// A 15-minute access token for a platform admin (no tenant/store claims), bound to the session (<c>sid</c>) and the
    /// user's security stamp; <c>platform_mfa=true</c> when the sign-in passed two-step verification. Audience: "nexo-platform".
    /// </summary>
    PlatformTokenResult GeneratePlatformToken(PlatformUser user, Guid sessionId, bool mfaVerified);

    /// <summary>The access token of an impersonation (see <see cref="ImpersonationGrant"/>).</summary>
    PlatformTokenResult GenerateImpersonationToken(ImpersonationGrant grant);
}

public record RefreshTokenClaims(
    string Jti,
    Guid UserId,
    Guid TenantId);
