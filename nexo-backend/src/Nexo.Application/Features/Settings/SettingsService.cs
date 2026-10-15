using System.Text.Json;
using Nexo.Application.Common.Interfaces;

namespace Nexo.Application.Features.Settings;

/// <summary>
/// The tenant's settings that have an effect: stock alerts (dashboard, reports) and PDV discount
/// rules (PDV screen, sale confirmation). Stored as JSON sections of <c>AppSettings</c>.
/// </summary>
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
        return new SettingsDto(Inventory(entity), Pos(entity));
    }

    public async Task<PosSettingsDto> GetPosAsync(CancellationToken ct = default)
        => Pos(await _repo.GetOrCreateAsync(ct));

    public async Task<InventorySettingsDto> GetInventoryAsync(CancellationToken ct = default)
        => Inventory(await _repo.GetOrCreateAsync(ct));

    public async Task<SettingsDto> UpdateAsync(UpdateSettingsRequest request, CancellationToken ct = default)
    {
        var entity = await _repo.GetOrCreateAsync(ct);
        entity.UpdateEffective(
            inventoryJson: JsonSerializer.Serialize(request.Inventory, JsonOpts),
            posJson:       JsonSerializer.Serialize(request.Pos, JsonOpts));
        await _repo.SaveChangesAsync(ct);
        return new SettingsDto(Inventory(entity), Pos(entity));
    }

    // Older rows keep extra fields in the same JSON (minStockBehavior, noMovementAlertDays…): they
    // are ignored on read and dropped on the next save of the section.
    private static InventorySettingsDto Inventory(Domain.Entities.AppSettings e)
        => Read(e.InventorySettingsJson, new InventorySettingsDto(EnableLowStockAlerts: true, EnableZeroStockAlerts: true));

    private static PosSettingsDto Pos(Domain.Entities.AppSettings e)
        => Read(e.PosSettingsJson, new PosSettingsDto(
            AllowValueDiscount: true, AllowPercentDiscount: true, RequireManagerAuth: true, MaxDiscountPercent: 20m));

    private static T Read<T>(string json, T fallback)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(json, JsonOpts) ?? fallback;
        }
        catch (JsonException)
        {
            return fallback;
        }
    }
}
