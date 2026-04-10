using Nexo.Domain.Common;

namespace Nexo.Domain.Entities;

/// <summary>
/// Per-tenant application settings.
/// Each logical section (company, inventory, pos...) is stored as a JSON column
/// to avoid schema migrations for minor settings changes.
///
/// One record per tenant (TenantId is unique and required).
/// </summary>
public class AppSettings : TenantEntity
{
    private AppSettings() { } // EF Core constructor

    // Each section stored as JSON — deserialized by the Application layer into typed DTOs
    public string CompanySettingsJson { get; private set; } = "{}";
    public string OperationSettingsJson { get; private set; } = "{}";
    public string InventorySettingsJson { get; private set; } = "{}";
    public string CommissionSettingsJson { get; private set; } = "{}";
    public string PosSettingsJson { get; private set; } = "{}";
    public string SystemSettingsJson { get; private set; } = "{}";

    // Navigation
    public Tenant? Tenant { get; private set; }

    public static AppSettings CreateForTenant(
        Guid tenantId,
        string companyJson,
        string operationJson,
        string inventoryJson,
        string commissionJson,
        string posJson,
        string systemJson)
    {
        return new AppSettings(tenantId)
        {
            CompanySettingsJson = companyJson,
            OperationSettingsJson = operationJson,
            InventorySettingsJson = inventoryJson,
            CommissionSettingsJson = commissionJson,
            PosSettingsJson = posJson,
            SystemSettingsJson = systemJson,
        };
    }

    private AppSettings(Guid tenantId) : base(tenantId) { }

    public void Update(
        string companyJson,
        string operationJson,
        string inventoryJson,
        string commissionJson,
        string posJson,
        string systemJson)
    {
        CompanySettingsJson = companyJson;
        OperationSettingsJson = operationJson;
        InventorySettingsJson = inventoryJson;
        CommissionSettingsJson = commissionJson;
        PosSettingsJson = posJson;
        SystemSettingsJson = systemJson;
        SetUpdatedAt();
    }
}
