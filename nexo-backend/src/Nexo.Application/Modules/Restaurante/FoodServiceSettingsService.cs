using Nexo.Application.Common.Interfaces;
using Nexo.Application.Modules.Restaurante.Interfaces;
using Nexo.Domain.Modules.Restaurante;

namespace Nexo.Application.Modules.Restaurante;

public class FoodServiceSettingsService
{
    private readonly IFoodServiceSettingsRepository _repo;
    private readonly ICurrentTenant                 _currentTenant;

    public FoodServiceSettingsService(
        IFoodServiceSettingsRepository repo, ICurrentTenant currentTenant)
    {
        _repo          = repo;
        _currentTenant = currentTenant;
    }

    public async Task<FoodServiceSettingsDto> GetOrCreateAsync(CancellationToken ct = default)
    {
        var settings = await _repo.GetCurrentStoreAsync(ct);
        if (settings is null)
        {
            settings = FoodServiceSettings.CreateDefault(_currentTenant.Id);
            await _repo.AddAsync(settings, ct);
            await _repo.SaveChangesAsync(ct);
        }
        return Map(settings);
    }

    public async Task<FoodServiceSettingsDto> UpdateAsync(UpdateFoodServiceSettingsRequest req, CancellationToken ct = default)
    {
        var settings = await _repo.GetCurrentStoreAsync(ct);
        if (settings is null)
        {
            settings = FoodServiceSettings.CreateDefault(_currentTenant.Id);
            await _repo.AddAsync(settings, ct);
        }
        settings.Update(
            req.StoreType, req.CouvertEnabled, req.CouvertPricePerPerson, req.CouvertAutomatic,
            req.ServiceFeeEnabled, req.ServiceFeePercent, req.OrderTypesEnabled);
        await _repo.SaveChangesAsync(ct);
        return Map(settings);
    }

    public async Task<FoodServiceSettingsDto> UpdatePortalInfoAsync(UpdatePortalInfoRequest req, CancellationToken ct = default)
    {
        var settings = await _repo.GetCurrentStoreAsync(ct);
        if (settings is null)
        {
            settings = FoodServiceSettings.CreateDefault(_currentTenant.Id);
            await _repo.AddAsync(settings, ct);
        }
        settings.UpdatePortalInfo(
            req.DisplayName, req.LogoUrl, req.CoverImageUrl,
            req.Description, req.WhatsAppPhone, req.BusinessHoursJson);
        await _repo.SaveChangesAsync(ct);
        return Map(settings);
    }

    private static FoodServiceSettingsDto Map(FoodServiceSettings s) => new(
        s.Id, s.StoreType,
        s.CouvertEnabled, s.CouvertPricePerPerson, s.CouvertAutomatic,
        s.ServiceFeeEnabled, s.ServiceFeePercent,
        s.OrderTypesEnabled,
        s.DisplayName, s.LogoUrl, s.CoverImageUrl,
        s.Description, s.WhatsAppPhone, s.BusinessHoursJson);
}
