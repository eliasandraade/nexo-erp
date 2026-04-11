using Nexo.Domain.Common;
using Nexo.Domain.Enums;

namespace Nexo.Domain.Entities;

/// <summary>
/// A store (galpão) is an isolated operational unit within a tenant.
/// Each ModuleSubscription can have one or more stores.
/// All store-scoped data (stock, sales, cash) is partitioned by StoreId.
///
/// Examples:
///   Tenant "Redes Lojas" → Store "Filial Centro" + Store "Filial Norte"
///   Tenant "Bar do João" → Store "Bar do João" (single store)
/// </summary>
public class Store : TenantEntity
{
    private Store() { }
    private Store(Guid tenantId) : base(tenantId) { }

    public Guid? ModuleSubscriptionId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Slug { get; private set; } = string.Empty;       // unique per tenant, e.g. "filial-centro"
    public StoreStatus Status { get; private set; }
    public string? SettingsJson { get; private set; }

    // Navigation
    public ModuleSubscription? ModuleSubscription { get; private set; }

    public static Store Create(
        Guid tenantId,
        string name,
        string slug,
        Guid? moduleSubscriptionId = null,
        string? settingsJson = null)
    {
        return new Store(tenantId)
        {
            Name                 = name.Trim(),
            Slug                 = slug.Trim().ToLowerInvariant(),
            ModuleSubscriptionId = moduleSubscriptionId,
            Status               = StoreStatus.Active,
            SettingsJson         = settingsJson,
        };
    }

    public void UpdateName(string name)
    {
        Name = name.Trim();
        SetUpdatedAt();
    }

    public void Deactivate() { Status = StoreStatus.Inactive; SetUpdatedAt(); }
    public void Activate()   { Status = StoreStatus.Active;   SetUpdatedAt(); }
}
