using Nexo.Application.Common.Interfaces;
using Nexo.Application.Modules.Service.Interfaces;
using Nexo.Domain.Exceptions;
using Nexo.Domain.Modules.Service;

namespace Nexo.Application.Modules.Service;

/// <summary>
/// Use cases for the SvcPackage template aggregate: CRUD + items. Item names snapshot the catalog
/// at add time; the catalog must exist and be active (422 if inactive). A catalog item cannot be
/// added twice to the same package. Editing a template never touches already-assigned packages.
/// </summary>
public class SvcPackageService
{
    private readonly ISvcPackageRepository     _packages;
    private readonly ISvcPackageItemRepository _items;
    private readonly ISvcCatalogItemRepository _catalog;
    private readonly ICurrentTenant            _currentTenant;

    public SvcPackageService(
        ISvcPackageRepository packages, ISvcPackageItemRepository items,
        ISvcCatalogItemRepository catalog, ICurrentTenant currentTenant)
    {
        _packages = packages; _items = items; _catalog = catalog; _currentTenant = currentTenant;
    }

    public async Task<IReadOnlyList<SvcPackageDto>> GetAllAsync(bool? active, CancellationToken ct = default)
    {
        var list = await _packages.GetAllAsync(active, ct);
        var dtos = new List<SvcPackageDto>(list.Count);
        foreach (var p in list)
            dtos.Add(MapToDto(p, await _items.GetByPackageAsync(p.Id, ct)));
        return dtos;
    }

    public async Task<SvcPackageDto> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var p = await _packages.GetByIdWithItemsAsync(id, ct) ?? throw new NotFoundException("SvcPackage", id);
        return MapToDto(p, p.Items);
    }

    public async Task<SvcPackageDto> CreateAsync(CreateSvcPackageRequest r, CancellationToken ct = default)
    {
        var p = SvcPackage.Create(_currentTenant.Id, r.Name, r.Price, r.Description, r.ValidityDays);
        await _packages.AddAsync(p, ct);
        await _packages.SaveChangesAsync(ct);
        return MapToDto(p, []);
    }

    public async Task<SvcPackageDto> UpdateAsync(Guid id, UpdateSvcPackageRequest r, CancellationToken ct = default)
    {
        var p = await _packages.GetByIdAsync(id, ct) ?? throw new NotFoundException("SvcPackage", id);
        p.UpdateDetails(r.Name, r.Description, r.ValidityDays);
        _packages.Update(p);
        await _packages.SaveChangesAsync(ct);
        return MapToDto(p, await _items.GetByPackageAsync(id, ct));
    }

    public async Task<SvcPackageDto> UpdatePriceAsync(Guid id, UpdateSvcPackagePriceRequest r, CancellationToken ct = default)
    {
        var p = await _packages.GetByIdAsync(id, ct) ?? throw new NotFoundException("SvcPackage", id);
        p.UpdatePrice(r.Price);
        _packages.Update(p);
        await _packages.SaveChangesAsync(ct);
        return MapToDto(p, await _items.GetByPackageAsync(id, ct));
    }

    public async Task<SvcPackageDto> ActivateAsync(Guid id, CancellationToken ct = default)
        => await ToggleAsync(id, activate: true, ct);

    public async Task<SvcPackageDto> DeactivateAsync(Guid id, CancellationToken ct = default)
        => await ToggleAsync(id, activate: false, ct);

    private async Task<SvcPackageDto> ToggleAsync(Guid id, bool activate, CancellationToken ct)
    {
        var p = await _packages.GetByIdAsync(id, ct) ?? throw new NotFoundException("SvcPackage", id);
        if (activate) p.Activate(); else p.Deactivate();
        _packages.Update(p);
        await _packages.SaveChangesAsync(ct);
        return MapToDto(p, await _items.GetByPackageAsync(id, ct));
    }

    public async Task<SvcPackageDto> AddItemAsync(Guid packageId, AddSvcPackageItemRequest r, CancellationToken ct = default)
    {
        var p = await _packages.GetByIdAsync(packageId, ct) ?? throw new NotFoundException("SvcPackage", packageId);

        var catalog = await _catalog.GetByIdAsync(r.CatalogItemId, ct)
            ?? throw new NotFoundException("SvcCatalogItem", r.CatalogItemId);
        if (!catalog.IsActive) throw new DomainException("Catalog item is not active.");
        if (await _items.ExistsForCatalogAsync(packageId, r.CatalogItemId, ct))
            throw new DomainException("This catalog item is already in the package.");

        var item = SvcPackageItem.Create(_currentTenant.Id, packageId, r.CatalogItemId, catalog.Name, r.IncludedQuantity);
        await _items.AddAsync(item, ct);
        await _items.SaveChangesAsync(ct);
        return MapToDto(p, await _items.GetByPackageAsync(packageId, ct));
    }

    public async Task<SvcPackageDto> UpdateItemAsync(Guid packageId, Guid itemId, UpdateSvcPackageItemRequest r, CancellationToken ct = default)
    {
        var p = await _packages.GetByIdAsync(packageId, ct) ?? throw new NotFoundException("SvcPackage", packageId);
        var item = await _items.GetByIdAsync(itemId, ct);
        if (item is null || item.PackageId != packageId) throw new NotFoundException("SvcPackageItem", itemId);
        item.UpdateQuantity(r.IncludedQuantity);
        _items.Update(item);
        await _items.SaveChangesAsync(ct);
        return MapToDto(p, await _items.GetByPackageAsync(packageId, ct));
    }

    public async Task<SvcPackageDto> RemoveItemAsync(Guid packageId, Guid itemId, CancellationToken ct = default)
    {
        var p = await _packages.GetByIdAsync(packageId, ct) ?? throw new NotFoundException("SvcPackage", packageId);
        var item = await _items.GetByIdAsync(itemId, ct);
        if (item is null || item.PackageId != packageId) throw new NotFoundException("SvcPackageItem", itemId);
        _items.Remove(item);
        await _items.SaveChangesAsync(ct);
        return MapToDto(p, (await _items.GetByPackageAsync(packageId, ct)).Where(i => i.Id != itemId).ToList());
    }

    internal static SvcPackageDto MapToDto(SvcPackage p, IEnumerable<SvcPackageItem> items) => new(
        Id: p.Id, StoreId: p.StoreId, Name: p.Name, Description: p.Description, Price: p.Price,
        ValidityDays: p.ValidityDays, IsActive: p.IsActive,
        Items: items.Select(MapItemToDto).ToList(), CreatedAt: p.CreatedAt, UpdatedAt: p.UpdatedAt);

    private static SvcPackageItemDto MapItemToDto(SvcPackageItem i) => new(
        Id: i.Id, PackageId: i.PackageId, CatalogItemId: i.CatalogItemId, NameSnapshot: i.NameSnapshot,
        IncludedQuantity: i.IncludedQuantity, CreatedAt: i.CreatedAt, UpdatedAt: i.UpdatedAt);
}
