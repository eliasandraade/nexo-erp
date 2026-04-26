using Nexo.Application.Common.Interfaces;
using Nexo.Application.Modules.Restaurante.Interfaces;
using Nexo.Domain.Exceptions;
using Nexo.Domain.Modules.Restaurante;

namespace Nexo.Application.Modules.Restaurante;

/// <summary>
/// Serves the public restaurant menu for a given store slug.
/// No authentication context — all queries bypass EF query filters.
/// </summary>
public class PublicMenuService
{
    private readonly IStoreRepository                _stores;
    private readonly IFoodServiceSettingsRepository  _settings;
    private readonly IProductRepository              _products;
    private readonly IModifierGroupRepository        _modifiers;

    public PublicMenuService(
        IStoreRepository               stores,
        IFoodServiceSettingsRepository settings,
        IProductRepository             products,
        IModifierGroupRepository       modifiers)
    {
        _stores    = stores;
        _settings  = settings;
        _products  = products;
        _modifiers = modifiers;
    }

    public async Task<PublicMenuDto> GetMenuAsync(string slug, CancellationToken ct = default)
    {
        var store = await _stores.GetByPublicSlugAsync(slug, ct)
            ?? throw new NotFoundException("Store", slug);

        var foodSettings = await _settings.GetByStoreIdAsync(store.Id, store.TenantId, ct);

        var products = await _products.GetAllMenuItemsAsync(store.Id, store.TenantId, ct);

        // Group by category — products are already sorted by category.SortOrder then name
        var grouped = products
            .GroupBy(p => p.CategoryId)
            .Select(g =>
            {
                var cat     = g.First().Category;
                var catId   = g.Key;
                var catName = cat?.Name ?? "Outros";
                var catSort = cat?.SortOrder ?? int.MaxValue;
                return (catId, catName, catSort, products: g.ToList());
            })
            .OrderBy(x => x.catSort)
            .ThenBy(x => x.catName)
            .ToList();

        // Build modifier groups per product in one pass (N+1 avoided via per-product loading)
        var categories = new List<PublicMenuCategoryDto>();
        foreach (var (catId, catName, catSort, prods) in grouped)
        {
            var menuProducts = new List<PublicMenuProductDto>();
            foreach (var p in prods)
            {
                var groups = await _modifiers.GetByProductIdAsync(p.Id, store.TenantId, ct);
                var modGroups = groups.Select(g => new PublicModifierGroupDto(
                    g.Id, g.Name, g.IsRequired,
                    (int)g.MinSelections, g.MaxSelections,
                    g.Modifiers
                        .Where(m => m.IsActive)
                        .OrderBy(m => m.SortOrder)
                        .Select(m => new PublicModifierDto(m.Id, m.Name, m.PriceAdjustment))
                        .ToList()))
                    .ToList();

                menuProducts.Add(new PublicMenuProductDto(
                    p.Id, p.Name, p.Description, p.SalePrice, p.ImageUrl, modGroups));
            }

            categories.Add(new PublicMenuCategoryDto(catId, catName, catSort, menuProducts));
        }

        var storeName       = foodSettings?.DisplayName ?? store.Name;
        var acceptingOrders = foodSettings?.AcceptingOrders ?? true;
        var deliveryEnabled = foodSettings?.DeliveryEnabled ?? true;
        var takeawayEnabled = foodSettings?.TakeawayEnabled ?? true;

        return new PublicMenuDto(
            StoreName:        storeName,
            Description:      foodSettings?.Description,
            LogoUrl:          foodSettings?.LogoUrl,
            CoverImageUrl:    foodSettings?.CoverImageUrl,
            WhatsAppPhone:    foodSettings?.WhatsAppPhone,
            BusinessHoursJson: foodSettings?.BusinessHoursJson,
            AcceptingOrders:  acceptingOrders,
            DeliveryEnabled:  deliveryEnabled,
            TakeawayEnabled:  takeawayEnabled,
            Categories:       categories);
    }
}
