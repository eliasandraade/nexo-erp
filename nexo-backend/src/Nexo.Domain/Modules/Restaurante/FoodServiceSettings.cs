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

    // ── Portal de pedidos públicos ────────────────────────────────────────────
    public string? DisplayName      { get; private set; }   // nome de exibição no portal
    public string? LogoUrl          { get; private set; }
    public string? CoverImageUrl    { get; private set; }
    public string? Description      { get; private set; }
    public string? WhatsAppPhone    { get; private set; }
    /// <summary>JSON: array de 7 objetos { dayOfWeek, isOpen, openTime, closeTime }.</summary>
    public string? BusinessHoursJson { get; private set; }
    /// <summary>Abre/fecha o portal para novos pedidos sem remover o slug.</summary>
    public bool    AcceptingOrders   { get; private set; } = true;
    /// <summary>Habilita pedidos de entrega no portal.</summary>
    public bool    DeliveryEnabled   { get; private set; } = true;
    /// <summary>Habilita retirada no balcão no portal.</summary>
    public bool    TakeawayEnabled   { get; private set; } = true;

    // ── Custos operacionais (CMV) ─────────────────────────────────────────────
    public decimal CostPerMinuteGas       { get; private set; }
    public decimal CostPerMinuteLaborRate { get; private set; }

    public static FoodServiceSettings CreateDefault(Guid tenantId)
        => new FoodServiceSettings(tenantId)
        {
            StoreType            = "restaurant",
            CouvertEnabled       = false,
            CouvertAutomatic     = false,
            ServiceFeeEnabled    = false,
            OrderTypesEnabled    = "DineIn,Counter,Takeaway",
            CostPerMinuteGas       = 0m,
            CostPerMinuteLaborRate = 0m,
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

    public void UpdatePortalInfo(
        string? displayName,
        string? logoUrl,
        string? coverImageUrl,
        string? description,
        string? whatsAppPhone,
        string? businessHoursJson,
        bool    acceptingOrders = true,
        bool    deliveryEnabled = true,
        bool    takeawayEnabled = true)
    {
        DisplayName       = displayName?.Trim();
        LogoUrl           = logoUrl?.Trim();
        CoverImageUrl     = coverImageUrl?.Trim();
        Description       = description?.Trim();
        WhatsAppPhone     = whatsAppPhone?.Trim();
        BusinessHoursJson = businessHoursJson;
        AcceptingOrders   = acceptingOrders;
        DeliveryEnabled   = deliveryEnabled;
        TakeawayEnabled   = takeawayEnabled;
        SetUpdatedAt();
    }

    public void UpdateOperationalCosts(decimal costPerMinuteGas, decimal costPerMinuteLaborRate)
    {
        CostPerMinuteGas       = costPerMinuteGas       >= 0 ? costPerMinuteGas       : 0;
        CostPerMinuteLaborRate = costPerMinuteLaborRate  >= 0 ? costPerMinuteLaborRate : 0;
        SetUpdatedAt();
    }
}
