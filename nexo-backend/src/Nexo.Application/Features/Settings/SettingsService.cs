using System.Text.Json;
using Nexo.Application.Common.Interfaces;

namespace Nexo.Application.Features.Settings;

public class SettingsService
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly IAppSettingsRepository _repo;

    public SettingsService(IAppSettingsRepository repo) => _repo = repo;

    public async Task<SettingsDto> GetAsync(CancellationToken ct = default)
    {
        var entity = await _repo.GetOrCreateAsync(ct);
        return Deserialize(entity);
    }

    public async Task<SettingsDto> UpdateAsync(
        UpdateSettingsRequest request,
        CancellationToken ct = default)
    {
        var entity = await _repo.GetOrCreateAsync(ct);

        entity.Update(
            companyJson:     Serialize(request.Company),
            operationJson:   Serialize(request.Operation),
            inventoryJson:   Serialize(request.Inventory),
            commissionJson:  Serialize(request.Commission),
            posJson:         Serialize(request.Pos),
            systemJson:      Serialize(request.System));

        await _repo.SaveChangesAsync(ct);
        return Deserialize(entity);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static string Serialize<T>(T value) =>
        JsonSerializer.Serialize(value, JsonOpts);

    private static SettingsDto Deserialize(Domain.Entities.AppSettings entity)
    {
        return new SettingsDto(
            Company:    DeserializeSection(entity.CompanySettingsJson,    DefaultCompany()),
            Operation:  DeserializeSection(entity.OperationSettingsJson,  DefaultOperation()),
            Inventory:  DeserializeSection(entity.InventorySettingsJson,  DefaultInventory()),
            Commission: DeserializeSection(entity.CommissionSettingsJson, DefaultCommission()),
            Pos:        DeserializeSection(entity.PosSettingsJson,        DefaultPos()),
            System:     DeserializeSection(entity.SystemSettingsJson,     DefaultSystem()));
    }

    private static T DeserializeSection<T>(string json, T fallback)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(json, JsonOpts) ?? fallback;
        }
        catch
        {
            return fallback;
        }
    }

    // ── Defaults (match frontend defaultSettings.ts) ─────────────────────────

    private static CompanySettingsDto DefaultCompany() => new(
        Name: "Andrade Systems",
        TradeName: "Andrade Corp",
        Cnpj: "",
        Email: "",
        Phone: "");

    private static OperationSettingsDto DefaultOperation() => new(
        DefaultStore: "",
        DefaultOperator: "");

    private static InventorySettingsDto DefaultInventory() => new(
        NoMovementAlertDays: 30,
        MinStockBehavior: "alert",
        EnableLowStockAlerts: true,
        EnableZeroStockAlerts: true,
        EnableHighRotationAlerts: false);

    private static CommissionSettingsDto DefaultCommission() => new(
        DefaultCommissionRate: 3m,
        EnableProductCommission: false,
        PolicyNotes: "");

    private static PosSettingsDto DefaultPos() => new(
        AllowValueDiscount: true,
        AllowPercentDiscount: true,
        RequireManagerAuth: true,
        MaxDiscountPercent: 20m);

    private static SystemSettingsDto DefaultSystem() => new(
        Language: "pt-BR",
        DateFormat: "dd/MM/yyyy",
        CurrencySymbol: "R$");
}
