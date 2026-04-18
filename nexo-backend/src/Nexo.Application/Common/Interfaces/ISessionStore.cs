namespace Nexo.Application.Common.Interfaces;

/// <summary>
/// Tracks user login sessions for the platform admin dashboard.
/// Sessions are created on login and updated on refresh.
/// Revocation is handled by PlatformController (which has direct DB access).
/// </summary>
public interface ISessionStore
{
    /// <summary>
    /// Records a new session when a user successfully logs in.
    /// </summary>
    Task CreateAsync(
        Guid userId,
        Guid tenantId,
        string refreshJti,
        string? ipAddress,
        string? userAgent,
        DateTime expiresAt,
        CancellationToken ct = default);

    /// <summary>
    /// Updates the session's RefreshJti and LastUsedAt when the token is rotated.
    /// </summary>
    Task TouchAsync(string oldRefreshJti, string newRefreshJti, CancellationToken ct = default);
}
