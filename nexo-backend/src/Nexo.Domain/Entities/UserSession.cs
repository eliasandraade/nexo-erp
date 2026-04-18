using Nexo.Domain.Common;

namespace Nexo.Domain.Entities;

/// <summary>
/// Tracks an active login session for a tenant user.
/// Created on login, updated on token refresh, revoked on forced logout or stamp rotation.
///
/// Note: extends BaseEntity (not TenantEntity) — no global query filter.
/// The platform admin queries this table with IgnoreQueryFilters().
/// </summary>
public class UserSession : BaseEntity
{
    private UserSession() { } // EF Core

    public Guid UserId { get; private set; }
    public Guid TenantId { get; private set; }

    /// <summary>
    /// JTI of the current refresh token for this session.
    /// Updated on every token rotation so the platform can revoke the Redis entry.
    /// </summary>
    public string RefreshJti { get; private set; } = string.Empty;

    public string? IpAddress { get; private set; }
    public string? UserAgent { get; private set; }

    /// <summary>UTC timestamp updated on every successful token refresh.</summary>
    public DateTime LastUsedAt { get; private set; }

    /// <summary>When the refresh token (and therefore this session) expires.</summary>
    public DateTime ExpiresAt { get; private set; }

    public bool IsRevoked { get; private set; }

    // Navigation
    public Tenant? Tenant { get; private set; }

    public static UserSession Create(
        Guid userId,
        Guid tenantId,
        string refreshJti,
        string? ipAddress,
        string? userAgent,
        DateTime expiresAt)
    {
        return new UserSession
        {
            UserId      = userId,
            TenantId    = tenantId,
            RefreshJti  = refreshJti,
            IpAddress   = ipAddress,
            UserAgent   = userAgent?.Length > 500 ? userAgent[..500] : userAgent,
            LastUsedAt  = DateTime.UtcNow,
            ExpiresAt   = expiresAt,
            IsRevoked   = false,
            CreatedAt   = DateTime.UtcNow,
            UpdatedAt   = DateTime.UtcNow,
        };
    }

    /// <summary>
    /// Called on token rotation: updates the JTI to the new refresh token
    /// so the platform can revoke it if needed.
    /// </summary>
    public void Touch(string newRefreshJti)
    {
        RefreshJti = newRefreshJti;
        LastUsedAt = DateTime.UtcNow;
        SetUpdatedAt();
    }

    /// <summary>Marks the session as revoked. The caller must also remove the Redis entry.</summary>
    public void Revoke()
    {
        IsRevoked = true;
        SetUpdatedAt();
    }
}
