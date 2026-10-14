using Nexo.Application.Common.Interfaces;
using Nexo.Application.Modules.Varejo.Interfaces;
using Nexo.Domain.Exceptions;
using Nexo.Domain.Modules.Varejo;

namespace Nexo.Application.Modules.Varejo;

/// <summary>
/// Listas de preço — and the server-side price of every sale line (<see cref="IPriceResolver"/>).
///
/// Resolução:
///   candidatas = lista vinculada ao cliente + lista padrão, só as ATIVAS, DENTRO DA VIGÊNCIA e que
///   tenham o produto; vence a de maior prioridade (empate: a do cliente — mais específica);
///   sem candidata → product.SalePrice.
/// </summary>
public class PriceListService : IPriceResolver
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
        return await MapToDetailAsync(pl, ct);
    }

    // ── Resolve price ─────────────────────────────────────────────────────────

    public async Task<ResolvedPriceDto> ResolvePriceAsync(Guid productId, Guid? customerId, CancellationToken ct = default)
    {
        var product = await _products.GetByIdAsync(productId, ct)
            ?? throw new NotFoundException("Product", productId);

        var now = DateTime.UtcNow;
        var candidates = new List<(RetPriceList List, decimal Price, bool IsCustomers)>();

        async Task Consider(RetPriceList? list, bool isCustomers)
        {
            if (list is null || !list.IsValidAt(now)) return;
            var withItems = await _priceLists.GetByIdWithItemsAsync(list.Id, ct);
            var item = withItems?.Items.FirstOrDefault(i => i.ProductId == productId);
            if (item is not null) candidates.Add((list, item.Price, isCustomers));
        }

        if (customerId.HasValue)
            await Consider(await _priceLists.GetByCustomerIdAsync(customerId.Value, ct), isCustomers: true);
        await Consider(await _priceLists.GetDefaultAsync(ct), isCustomers: false);

        var winner = candidates
            .OrderByDescending(c => c.List.Priority)
            .ThenByDescending(c => c.IsCustomers)
            .Select(c => ((RetPriceList, decimal)?)(c.List, c.Price))
            .FirstOrDefault();

        return winner is { } w
            ? new ResolvedPriceDto(product.Id, product.Name, product.Code, w.Item2, "PriceList", w.Item1.Id, w.Item1.Name)
            : new ResolvedPriceDto(product.Id, product.Name, product.Code, product.SalePrice, "Default", null, null);
    }

    public async Task<decimal> ResolveUnitPriceAsync(Guid productId, Guid? customerId, CancellationToken ct = default)
        => (await ResolvePriceAsync(productId, customerId, ct)).ResolvedPrice;

    // ── Commands ──────────────────────────────────────────────────────────────

    public async Task<PriceListDto> CreateAsync(CreatePriceListRequest request, CancellationToken ct = default)
    {
        if (request.IsDefault)
        {
            var current = await _priceLists.GetDefaultAsync(ct);
            current?.UnsetDefault();
        }

        var pl = RetPriceList.Create(
            tenantId:    _currentTenant.Id,
            name:        request.Name,
            description: request.Description,
            isDefault:   request.IsDefault,
            validFrom:   request.ValidFrom,
            validUntil:  request.ValidUntil,
            priority:    request.Priority);

        await _priceLists.AddAsync(pl, ct);
        await _priceLists.SaveChangesAsync(ct);
        return MapToDto(pl);
    }

    public async Task<PriceListDto> UpdateAsync(Guid id, UpdatePriceListRequest request, CancellationToken ct = default)
    {
        var pl = await _priceLists.GetByIdAsync(id, ct)
            ?? throw new NotFoundException("PriceList", id);

        pl.Update(request.Name, request.Description, request.ValidFrom, request.ValidUntil, request.Priority);
        await _priceLists.SaveChangesAsync(ct);
        return MapToDto(pl);
    }

    public async Task<PriceListDto> SetAsDefaultAsync(Guid id, CancellationToken ct = default)
    {
        var current = await _priceLists.GetDefaultAsync(ct);
        if (current is not null && current.Id != id)
            current.UnsetDefault();

        var pl = await _priceLists.GetByIdAsync(id, ct)
            ?? throw new NotFoundException("PriceList", id);

        pl.SetAsDefault();
        await _priceLists.SaveChangesAsync(ct);
        return MapToDto(pl);
    }

    public async Task<PriceListDto> SetActiveAsync(Guid id, bool active, CancellationToken ct = default)
    {
        var pl = await _priceLists.GetByIdAsync(id, ct)
            ?? throw new NotFoundException("PriceList", id);
        if (active) pl.Activate(); else pl.Deactivate();
        await _priceLists.SaveChangesAsync(ct);
        return MapToDto(pl);
    }

    public async Task<PriceListDetailDto> SetProductPriceAsync(Guid id, SetProductPriceRequest request, CancellationToken ct = default)
    {
        var pl = await _priceLists.GetByIdWithItemsAsync(id, ct)
            ?? throw new NotFoundException("PriceList", id);

        _ = await _products.GetByIdAsync(request.ProductId, ct)
            ?? throw new NotFoundException("Product", request.ProductId);

        var isNew = pl.Items.All(i => i.ProductId != request.ProductId);
        var item = pl.SetProductPrice(_currentTenant.Id, request.ProductId, request.Price);
        if (isNew) _priceLists.TrackItem(item);   // readonly backing field: mark the new line Added
        await _priceLists.SaveChangesAsync(ct);
        return await MapToDetailAsync(pl, ct);
    }

    public async Task<PriceListDetailDto> RemoveProductPriceAsync(Guid id, Guid productId, CancellationToken ct = default)
    {
        var pl = await _priceLists.GetByIdWithItemsAsync(id, ct)
            ?? throw new NotFoundException("PriceList", id);

        pl.RemoveProductPrice(productId);
        await _priceLists.SaveChangesAsync(ct);
        return await MapToDetailAsync(pl, ct);
    }

    /// <summary>Links a customer to this list (a customer has at most one list — this replaces it).</summary>
    public async Task<PriceListDetailDto> LinkCustomerAsync(Guid id, Guid customerId, CancellationToken ct = default)
    {
        var pl = await _priceLists.GetByIdWithItemsAsync(id, ct)
            ?? throw new NotFoundException("PriceList", id);

        var existing = await _priceLists.GetCustomerLinkAsync(customerId, ct);
        if (existing is not null && existing.PriceListId != id)
        {
            _priceLists.RemoveCustomerLink(existing);
            await _priceLists.SaveChangesAsync(ct);
            existing = null;
        }
        if (existing is null)
        {
            await _priceLists.AddCustomerLinkAsync(RetCustomerPriceList.Create(_currentTenant.Id, customerId, id), ct);
            await _priceLists.SaveChangesAsync(ct);
        }
        return await MapToDetailAsync(pl, ct);
    }

    public async Task<PriceListDetailDto> UnlinkCustomerAsync(Guid id, Guid customerId, CancellationToken ct = default)
    {
        var pl = await _priceLists.GetByIdWithItemsAsync(id, ct)
            ?? throw new NotFoundException("PriceList", id);

        var link = await _priceLists.GetCustomerLinkAsync(customerId, ct);
        if (link is not null && link.PriceListId == id)
        {
            _priceLists.RemoveCustomerLink(link);
            await _priceLists.SaveChangesAsync(ct);
        }
        return await MapToDetailAsync(pl, ct);
    }

    // ── Mapping ───────────────────────────────────────────────────────────────

    private static PriceListDto MapToDto(RetPriceList pl) => new(
        Id:          pl.Id,
        Name:        pl.Name,
        Description: pl.Description,
        IsDefault:   pl.IsDefault,
        IsActive:    pl.IsActive,
        ItemCount:   pl.Items.Count,
        CreatedAt:   pl.CreatedAt,
        ValidFrom:   pl.ValidFrom,
        ValidUntil:  pl.ValidUntil,
        Priority:    pl.Priority);

    private async Task<PriceListDetailDto> MapToDetailAsync(RetPriceList pl, CancellationToken ct)
    {
        var labels = await _priceLists.GetProductLabelsAsync(pl.Items.Select(i => i.ProductId), ct);
        var customers = await _priceLists.GetLinkedCustomersAsync(pl.Id, ct);
        return new PriceListDetailDto(
            Id:          pl.Id,
            Name:        pl.Name,
            Description: pl.Description,
            IsDefault:   pl.IsDefault,
            IsActive:    pl.IsActive,
            Items:       pl.Items
                .Select(i => new PriceListItemDto(
                    i.Id, i.ProductId,
                    ProductName: labels.TryGetValue(i.ProductId, out var l) ? l.Name : string.Empty,
                    ProductCode: labels.TryGetValue(i.ProductId, out var c) ? c.Code : string.Empty,
                    Price:       i.Price))
                .OrderBy(i => i.ProductName)
                .ToList(),
            CreatedAt:   pl.CreatedAt,
            ValidFrom:   pl.ValidFrom,
            ValidUntil:  pl.ValidUntil,
            Priority:    pl.Priority,
            Customers:   customers.Select(x => new PriceListCustomerDto(x.CustomerId, x.Name)).ToList());
    }
}
