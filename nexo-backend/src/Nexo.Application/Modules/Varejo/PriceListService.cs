using Nexo.Application.Common.Interfaces;
using Nexo.Application.Modules.Varejo.Interfaces;
using Nexo.Domain.Exceptions;
using Nexo.Domain.Modules.Varejo;

namespace Nexo.Application.Modules.Varejo;

/// <summary>
/// Gerencia listas de preço e resolve o preço correto para o PDV.
///
/// Regra de resolução (ResolvePrice):
///   1. Lista vinculada ao cliente
///   2. Lista padrão do tenant
///   3. Fallback: product.SalePrice
/// </summary>
public class PriceListService
{
    private readonly IPriceListRepository _priceLists;
    private readonly IProductRepository   _products;
    private readonly ICurrentTenant       _currentTenant;

    public PriceListService(
        IPriceListRepository priceLists,
        IProductRepository   products,
        ICurrentTenant       currentTenant)
    {
        _priceLists    = priceLists;
        _products      = products;
        _currentTenant = currentTenant;
    }

    // ── Queries ───────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<PriceListDto>> GetAllAsync(bool includeInactive = false, CancellationToken ct = default)
    {
        var lists = await _priceLists.GetAllAsync(includeInactive, ct);
        return lists.Select(MapToDto).ToList();
    }

    public async Task<PriceListDetailDto> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var pl = await _priceLists.GetByIdWithItemsAsync(id, ct)
            ?? throw new NotFoundException("PriceList", id);
        return MapToDetailDto(pl);
    }

    // ── PDV: Resolve price ────────────────────────────────────────────────────

    /// <summary>
    /// Resolve o preço de venda de um produto para o PDV.
    /// Prioridade: lista do cliente → lista padrão → product.SalePrice
    /// </summary>
    public async Task<ResolvedPriceDto> ResolvePriceAsync(
        Guid productId,
        Guid? customerId,
        CancellationToken ct = default)
    {
        var product = await _products.GetByIdAsync(productId, ct)
            ?? throw new NotFoundException("Product", productId);

        RetPriceList? priceList = null;

        // 1. Lista vinculada ao cliente
        if (customerId.HasValue)
            priceList = await _priceLists.GetByCustomerIdAsync(customerId.Value, ct);

        // 2. Lista padrão do tenant
        if (priceList is null)
            priceList = await _priceLists.GetDefaultAsync(ct);

        // Encontrou lista com item para este produto?
        if (priceList is not null)
        {
            // Reload with items if needed
            var plWithItems = await _priceLists.GetByIdWithItemsAsync(priceList.Id, ct);
            var listItem = plWithItems?.Items.FirstOrDefault(i => i.ProductId == productId);

            if (listItem is not null)
            {
                return new ResolvedPriceDto(
                    ProductId:     product.Id,
                    ProductName:   product.Name,
                    ProductCode:   product.Code,
                    ResolvedPrice: listItem.Price,
                    Source:        "PriceList",
                    PriceListId:   priceList.Id,
                    PriceListName: priceList.Name);
            }
        }

        // 3. Fallback: preço padrão do produto
        return new ResolvedPriceDto(
            ProductId:     product.Id,
            ProductName:   product.Name,
            ProductCode:   product.Code,
            ResolvedPrice: product.SalePrice,
            Source:        "Default",
            PriceListId:   null,
            PriceListName: null);
    }

    // ── Commands ──────────────────────────────────────────────────────────────

    public async Task<PriceListDto> CreateAsync(CreatePriceListRequest request, CancellationToken ct = default)
    {
        // Se esta será default, remove o flag da lista atual
        if (request.IsDefault)
        {
            var current = await _priceLists.GetDefaultAsync(ct);
            current?.UnsetDefault();
        }

        var pl = RetPriceList.Create(
            tenantId:    _currentTenant.Id,
            name:        request.Name,
            description: request.Description,
            isDefault:   request.IsDefault);

        await _priceLists.AddAsync(pl, ct);
        await _priceLists.SaveChangesAsync(ct);
        return MapToDto(pl);
    }

    public async Task<PriceListDto> UpdateAsync(Guid id, UpdatePriceListRequest request, CancellationToken ct = default)
    {
        var pl = await _priceLists.GetByIdAsync(id, ct)
            ?? throw new NotFoundException("PriceList", id);

        pl.Update(request.Name, request.Description);
        await _priceLists.SaveChangesAsync(ct);
        return MapToDto(pl);
    }

    public async Task<PriceListDto> SetAsDefaultAsync(Guid id, CancellationToken ct = default)
    {
        // Remove default de lista anterior
        var current = await _priceLists.GetDefaultAsync(ct);
        if (current is not null && current.Id != id)
            current.UnsetDefault();

        var pl = await _priceLists.GetByIdAsync(id, ct)
            ?? throw new NotFoundException("PriceList", id);

        pl.SetAsDefault();
        await _priceLists.SaveChangesAsync(ct);
        return MapToDto(pl);
    }

    public async Task<PriceListDetailDto> SetProductPriceAsync(
        Guid id,
        SetProductPriceRequest request,
        CancellationToken ct = default)
    {
        var pl = await _priceLists.GetByIdWithItemsAsync(id, ct)
            ?? throw new NotFoundException("PriceList", id);

        // Valida que o produto existe
        _ = await _products.GetByIdAsync(request.ProductId, ct)
            ?? throw new NotFoundException("Product", request.ProductId);

        pl.SetProductPrice(_currentTenant.Id, request.ProductId, request.Price);
        await _priceLists.SaveChangesAsync(ct);
        return MapToDetailDto(pl);
    }

    public async Task<PriceListDetailDto> RemoveProductPriceAsync(
        Guid id,
        Guid productId,
        CancellationToken ct = default)
    {
        var pl = await _priceLists.GetByIdWithItemsAsync(id, ct)
            ?? throw new NotFoundException("PriceList", id);

        pl.RemoveProductPrice(productId);
        await _priceLists.SaveChangesAsync(ct);
        return MapToDetailDto(pl);
    }

    // ── Mapping ───────────────────────────────────────────────────────────────

    private static PriceListDto MapToDto(RetPriceList pl) => new(
        Id:          pl.Id,
        Name:        pl.Name,
        Description: pl.Description,
        IsDefault:   pl.IsDefault,
        IsActive:    pl.IsActive,
        ItemCount:   pl.Items.Count,
        CreatedAt:   pl.CreatedAt);

    private static PriceListDetailDto MapToDetailDto(RetPriceList pl) => new(
        Id:          pl.Id,
        Name:        pl.Name,
        Description: pl.Description,
        IsDefault:   pl.IsDefault,
        IsActive:    pl.IsActive,
        Items:       pl.Items.Select(i => new PriceListItemDto(
            i.Id,
            i.ProductId,
            ProductName: string.Empty,
            ProductCode: string.Empty,
            i.Price)).ToList(),
        CreatedAt:   pl.CreatedAt);
}
