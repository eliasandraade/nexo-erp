namespace Nexo.Application.Features.Settings;

// Only settings that change something (PR 71). The fields the page used to show without any effect
// (company data, default store/operator, rotation and no-movement alerts, retail commissions,
// language/date/currency) were removed from the API and the page; their stored JSON columns are kept
// untouched — no destructive migration. Company data for notas and receipts lives in Fiscal.

/// <summary>Which stock alerts the dashboard (Visão geral) raises.</summary>
public record InventorySettingsDto(
    bool EnableLowStockAlerts,
    bool EnableZeroStockAlerts);

/// <summary>
/// PDV discount rules. The limit and the manager authorization are enforced by the server when the
/// sale is closed at the PDV; the allowed modes (R$ / %) shape how the PDV offers the discount.
/// </summary>
public record PosSettingsDto(
    bool AllowValueDiscount,
    bool AllowPercentDiscount,
    bool RequireManagerAuth,
    decimal MaxDiscountPercent);

public record SettingsDto(
    InventorySettingsDto Inventory,
    PosSettingsDto Pos);

public record UpdateSettingsRequest(
    InventorySettingsDto Inventory,
    PosSettingsDto Pos);
