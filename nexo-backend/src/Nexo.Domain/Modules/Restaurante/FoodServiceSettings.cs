using Nexo.Domain.Common;

namespace Nexo.Domain.Modules.Restaurante;

/// <summary>
/// Per-store operational configuration for the Food Service module.
/// One record per (TenantId, StoreId) — enforced by unique index.
/// Inherits StoreEntity: auto-injected StoreId, filtered by current store.
/// </summary>
public class FoodServiceSettings : StoreEntity
{
    private FoodServiceSettings() { }
    private FoodServiceSettings(Guid tenantId) : base(tenantId) { }

    public string StoreType          { get; private set; } = "restaurant"; // "restaurant"|"bar"|"pub"

    public bool     CouvertEnabled        { get; private set; }
    public decimal? CouvertPricePerPerson { get; private set; }
    public bool     CouvertAutomatic      { get; private set; }

    public bool     ServiceFeeEnabled  { get; private set; }
    public decimal? ServiceFeePercent  { get; private set; }

    /// <summary>Comma-separated enabled order types, e.g. "DineIn,Counter,Takeaway".</summary>
    public string OrderTypesEnabled    { get; private set; } = "DineIn,Counter,Takeaway";

    public static FoodServiceSettings CreateDefault(Guid tenantId)
        => new FoodServiceSettings(tenantId)
        {
            StoreType            = "restaurant",
            CouvertEnabled       = false,
            CouvertAutomatic     = false,
            ServiceFeeEnabled    = false,
            OrderTypesEnabled    = "DineIn,Counter,Takeaway",
        };

    public void Update(
        string storeType,
        bool couvertEnabled, decimal? couvertPricePerPerson, bool couvertAutomatic,
        bool serviceFeeEnabled, decimal? serviceFeePercent,
        string orderTypesEnabled)
    {
        StoreType             = storeType;
        CouvertEnabled        = couvertEnabled;
        CouvertPricePerPerson = couvertEnabled ? couvertPricePerPerson : null;
        CouvertAutomatic      = couvertEnabled && couvertAutomatic;
        ServiceFeeEnabled     = serviceFeeEnabled;
        ServiceFeePercent     = serviceFeeEnabled ? serviceFeePercent : null;
        OrderTypesEnabled     = orderTypesEnabled;
        SetUpdatedAt();
    }
}
