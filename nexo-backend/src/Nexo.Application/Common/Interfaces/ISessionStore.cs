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

    /// <summary>Marks the session of this refresh token as ended (logout) — it leaves the device list.</summary>
    Task RevokeAsync(string refreshJti, CancellationToken ct = default);

    /// <summary>
    /// True when this is the current refresh token of a session that was not ended. The database's answer, for when the cache
    /// that normally says this is not answering.
    /// </summary>
    Task<bool> IsActiveAsync(string refreshJti, CancellationToken ct = default);

    /// <summary>
    /// True when the row of this token says the session ended. Not the opposite of <see cref="IsActiveAsync"/>: the row follows the token by a best-effort step
    /// and may still hold an earlier one, so a token with no row of its own is simply not known to have ended.
    /// </summary>
    Task<bool> IsRevokedAsync(string refreshJti, CancellationToken ct = default);
}
