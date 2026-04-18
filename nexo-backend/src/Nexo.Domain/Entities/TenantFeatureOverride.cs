using Nexo.Domain.Common;

namespace Nexo.Domain.Entities;

/// <summary>
/// Tenant-specific override for a feature flag.
/// When this row exists, IsEnabled takes precedence over FeatureFlag.DefaultEnabled.
/// When this row is deleted, the tenant reverts to the global default.
///
/// UNIQUE constraint: (TenantId, FlagKey)
/// </summary>
public class TenantFeatureOverride : BaseEntity
{
    private TenantFeatureOverride() { }

    public Guid TenantId { get; private set; }
    public string FlagKey { get; private set; } = string.Empty;

    /// <summary>The resolved value for this tenant. Overrides FeatureFlag.DefaultEnabled.</summary>
    public bool IsEnabled { get; private set; }

    /// <summary>Optional note explaining why this override was set.</summary>
    public string? Notes { get; private set; }

    // Navigation
    public Tenant? Tenant { get; private set; }
    public FeatureFlag? Flag { get; private set; }

    public static TenantFeatureOverride Create(
        Guid tenantId,
        string flagKey,
        bool isEnabled,
        string? notes = null)
    {
        return new TenantFeatureOverride
        {
            TenantId  = tenantId,
            FlagKey   = flagKey.Trim().ToLowerInvariant(),
            IsEnabled = isEnabled,
            Notes     = notes?.Trim(),
        };
    }

    public void SetEnabled(bool isEnabled, string? notes = null)
    {
        IsEnabled = isEnabled;
        if (notes is not null) Notes = notes.Trim();
        SetUpdatedAt();
    }
}
