namespace Nexo.Application.Features.Settings;

// ── Section DTOs (mirror the frontend AppSettings type exactly) ─────────────

public record CompanySettingsDto(
    string Name,
    string TradeName,
    string Cnpj,
    string Email,
    string Phone);

public record OperationSettingsDto(
    string? DefaultStore,       // nullable: legacy tenants may not have this field in stored JSON
    string DefaultOperator);

public record InventorySettingsDto(
    int NoMovementAlertDays,
    string MinStockBehavior,         // "alert" | "block" | "ignore"
    bool EnableLowStockAlerts,
    bool EnableZeroStockAlerts,
    bool EnableHighRotationAlerts);

public record CommissionSettingsDto(
    decimal DefaultCommissionRate,
    bool EnableProductCommission,
    string PolicyNotes);

public record PosSettingsDto(
    bool AllowValueDiscount,
    bool AllowPercentDiscount,
    bool RequireManagerAuth,
    decimal MaxDiscountPercent);

public record SystemSettingsDto(
    string Language,
    string DateFormat,
    string CurrencySymbol);

// ── Composite ────────────────────────────────────────────────────────────────

public record SettingsDto(
    CompanySettingsDto Company,
    OperationSettingsDto Operation,
    InventorySettingsDto Inventory,
    CommissionSettingsDto Commission,
    PosSettingsDto Pos,
    SystemSettingsDto System);

public record UpdateSettingsRequest(
    CompanySettingsDto Company,
    OperationSettingsDto Operation,
    InventorySettingsDto Inventory,
    CommissionSettingsDto Commission,
    PosSettingsDto Pos,
    SystemSettingsDto System);
