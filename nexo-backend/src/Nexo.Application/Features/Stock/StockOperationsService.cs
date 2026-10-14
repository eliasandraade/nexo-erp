using Nexo.Application.Common.Interfaces;
using Nexo.Domain.Entities;
using Nexo.Domain.Enums;
using Nexo.Domain.Exceptions;

namespace Nexo.Application.Features.Stock;

/// <summary>
/// Estoque avançado: inventário (contagem física → ajuste) and transferência between the tenant's
/// stores. Every change of quantity happens under row locks taken in a deterministic order and
/// produces movements referencing the operation, so stock never drifts under concurrency and the
/// history explains every unit.
/// </summary>
public class StockOperationsService
{
    public const string CountReference    = "InventoryCount";
    public const string TransferReference = "StockTransfer";

    private readonly IStockOperationsRepository _ops;
    private readonly IUnitOfWork _uow;
    private readonly IAuditWriter _audit;
    private readonly ICurrentTenant _tenant;
    private readonly ICurrentStore _store;
    private readonly ICurrentUser _user;

    public StockOperationsService(
        IStockOperationsRepository ops, IUnitOfWork uow, IAuditWriter audit,
        ICurrentTenant tenant, ICurrentStore store, ICurrentUser user)
    {
        _ops    = ops;
        _uow    = uow;
        _audit  = audit;
        _tenant = tenant;
        _store  = store;
        _user   = user;
    }

    // ── Inventário ──────────────────────────────────────────────────────────

    public async Task<InventoryCountDto> OpenCountAsync(CreateInventoryCountRequest request, CancellationToken ct = default)
    {
        var productIds = request.ProductIds is { Count: > 0 }
            ? request.ProductIds.Distinct().ToList()
            : (await _ops.GetTrackedProductIdsAsync(ct)).ToList();
        if (productIds.Count == 0)
            throw new DomainException("Nenhum produto com controle de estoque para contar.");
        if (productIds.Count > 2000)
            throw new DomainException("Um inventário aceita no máximo 2.000 produtos. Divida a contagem.");

        var products = await _ops.GetProductsAsync(productIds, ct);
        var notTracked = productIds.Where(id => !products.TryGetValue(id, out var p) || !p.TrackStock).ToList();
        if (notTracked.Count > 0)
            throw new DomainException("Só entram no inventário produtos com controle de estoque.");

        await using var tx = await _uow.BeginTransactionAsync(ct);
        await _ops.LockSequenceAsync(StoreId(), "inventory-count", ct);
        if (await _ops.HasOpenCountAsync(ct))
            throw new ConflictException("Já existe um inventário em contagem nesta loja. Aplique ou cancele antes de abrir outro.");
        var stock = await _ops.GetStockInStoreAsync(_tenant.Id, StoreId(), productIds, ct);
        var count = InventoryCount.Open(_tenant.Id, await _ops.GetNextCountNumberAsync(ct), _user.UserId, request.Notes);
        foreach (var id in productIds)
            count.AddProduct(id, stock.GetValueOrDefault(id)?.CurrentQuantity ?? 0m);

        await _ops.AddCountAsync(count, ct);
        await _ops.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return MapCount(count, products);
    }

    public async Task<IReadOnlyList<InventoryCountDto>> ListCountsAsync(CancellationToken ct = default)
    {
        var counts = await _ops.ListCountsAsync(ct);
        var products = await _ops.GetProductsAsync(counts.SelectMany(c => c.Items).Select(i => i.ProductId), ct);
        return counts.Select(c => MapCount(c, products)).ToList();
    }

    public async Task<InventoryCountDto> GetCountAsync(Guid id, CancellationToken ct = default)
    {
        var count = await _ops.GetCountAsync(id, ct) ?? throw new NotFoundException("InventoryCount", id);
        return MapCount(count, await _ops.GetProductsAsync(count.Items.Select(i => i.ProductId), ct));
    }

    /// <summary>Saves what was counted (partial saves are fine while the count is open).</summary>
    public async Task<InventoryCountDto> UpdateCountAsync(Guid id, UpdateInventoryCountRequest request, CancellationToken ct = default)
    {
        await using var tx = await _uow.BeginTransactionAsync(ct);
        await _ops.LockCountAsync(id, _tenant.Id, ct);
        var count = await _ops.GetCountAsync(id, ct) ?? throw new NotFoundException("InventoryCount", id);

        var entries = request.Items ?? [];
        var systemNow = await _ops.GetStockInStoreAsync(_tenant.Id, StoreId(), entries.Select(e => e.ProductId), ct);
        foreach (var entry in entries)
            count.Count(entry.ProductId, entry.CountedQuantity, systemNow.GetValueOrDefault(entry.ProductId)?.CurrentQuantity ?? 0m);

        await _ops.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return MapCount(count, await _ops.GetProductsAsync(count.Items.Select(i => i.ProductId), ct));
    }

    /// <summary>
    /// Applies the count: each counted product receives (counted − system when it was counted) on
    /// top of its current, locked stock — so sales made between typing the count and applying it
    /// are kept, not undone.
    /// </summary>
    public async Task<InventoryCountDto> ApplyCountAsync(Guid id, CancellationToken ct = default)
    {
        await using var tx = await _uow.BeginTransactionAsync(ct);
        await _ops.LockCountAsync(id, _tenant.Id, ct);
        var count = await _ops.GetCountAsync(id, ct) ?? throw new NotFoundException("InventoryCount", id);

        var counted = count.Items.Where(i => i.CountedQuantity.HasValue).ToList();
        var ids = counted.Select(i => i.ProductId).ToList();
        await _ops.LockStockAsync(_tenant.Id, StoreId(), ids, ct);
        var stock = await _ops.GetStockInStoreAsync(_tenant.Id, StoreId(), ids, ct);

        count.MarkApplied(_user.UserId, ids.ToDictionary(pid => pid, pid => stock.GetValueOrDefault(pid)?.CurrentQuantity ?? 0m));

        var adjusted = 0;
        foreach (var item in counted.Where(i => i.Difference != 0))
        {
            var stockItem = stock.GetValueOrDefault(item.ProductId);
            if (stockItem is null)
            {
                stockItem = StockItem.CreateInStore(_tenant.Id, StoreId(), item.ProductId);
                await _ops.AddStockItemAsync(stockItem, ct);
            }

            var before = stockItem.CurrentQuantity;
            stockItem.ApplyMovement(item.Difference!.Value);
            await _ops.AddMovementAsync(StockMovement.Create(
                _tenant.Id, item.ProductId, StockMovementType.Adjustment, item.Difference.Value,
                before, stockItem.CurrentQuantity, _user.UserId,
                referenceType: CountReference, referenceId: count.Id,
                notes: $"Inventário #{count.Number}: contado {item.CountedQuantity:0.####}, sistema na contagem {item.SystemQuantityAtCount:0.####}"), ct);
            adjusted++;
        }

        _audit.Stage(AuditActions.StockAdjustment, adjusted > 0 ? AuditSeverity.Warning : AuditSeverity.Info,
            "InventoryCount", count.Id.ToString(),
            $"Inventário #{count.Number} aplicado: {counted.Count} contados, {adjusted} ajustados",
            tenantId: _tenant.Id, actorId: _user.UserId, actorName: _user.Name,
            metadata: new { count.Number, Counted = counted.Count, Adjusted = adjusted });

        await _ops.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return MapCount(count, await _ops.GetProductsAsync(count.Items.Select(i => i.ProductId), ct));
    }

    public async Task<InventoryCountDto> CancelCountAsync(Guid id, CancellationToken ct = default)
    {
        await using var tx = await _uow.BeginTransactionAsync(ct);
        await _ops.LockCountAsync(id, _tenant.Id, ct);
        var count = await _ops.GetCountAsync(id, ct) ?? throw new NotFoundException("InventoryCount", id);
        count.Cancel(_user.UserId);
        await _ops.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return MapCount(count, await _ops.GetProductsAsync(count.Items.Select(i => i.ProductId), ct));
    }

    // ── Transferência ───────────────────────────────────────────────────────

    /// <summary>
    /// Moves stock from the current store to another store of the tenant the user has access to —
    /// atomically: both stores' rows are locked (deterministic order), the origin must hold the
    /// quantity, and the TransferOut/TransferIn movements commit together or not at all.
    /// </summary>
    public async Task<StockTransferDto> TransferAsync(CreateStockTransferRequest request, CancellationToken ct = default)
    {
        var from = StoreId();
        var to = request.ToStoreId;
        if (to == from)
            throw new DomainException("A loja de destino deve ser diferente da loja atual.");
        if (!_user.StoreIds.Contains(to) || !await _ops.IsActiveStoreOfTenantAsync(to, ct))
            throw new ForbiddenException("Você não tem acesso à loja de destino, ou ela está inativa.");
        if (request.Items is null || request.Items.Count == 0)
            throw new DomainException("Informe ao menos um produto para transferir.");

        var ids = request.Items.Select(i => i.ProductId).ToList();
        var products = await _ops.GetProductsAsync(ids, ct);
        if (ids.Any(id => !products.TryGetValue(id, out var p) || !p.TrackStock || !p.IsActive))
            throw new DomainException("Só produtos ativos com controle de estoque podem ser transferidos.");

        // Each store has its own catalogue: the destination must have the same product (same code).
        var counterparts = await _ops.GetProductsInStoreByCodeAsync(_tenant.Id, to, products.Values.Select(p => p.Code), ct);
        var missing = products.Values
            .Where(p => !counterparts.TryGetValue(p.Code, out var d) || !d.IsActive || !d.TrackStock)
            .Select(p => $"{p.Name} ({p.Code})")
            .ToList();
        if (missing.Count > 0)
            throw new DomainException(
                $"A loja de destino não tem estes produtos cadastrados com o mesmo código e controle de estoque: {string.Join(", ", missing)}.");
        Guid DestinationOf(Guid productId) => counterparts[products[productId].Code].Id;
        var destinationIds = ids.Select(DestinationOf).ToList();

        await using var tx = await _uow.BeginTransactionAsync(ct);

        // Always lock the lower store id first: two opposite transfers can't deadlock.
        foreach (var storeId in new[] { from, to }.OrderBy(s => s))
            await _ops.LockStockAsync(_tenant.Id, storeId, storeId == from ? ids : destinationIds, ct);

        var origin = await _ops.GetStockInStoreAsync(_tenant.Id, from, ids, ct);
        var destination = await _ops.GetStockInStoreAsync(_tenant.Id, to, destinationIds, ct);

        await _ops.LockSequenceAsync(_tenant.Id, "stock-transfer", ct);
        var transfer = StockTransfer.Create(
            _tenant.Id, await _ops.GetNextTransferNumberAsync(ct), from, to, _user.UserId, request.Notes);

        foreach (var line in request.Items)
        {
            var destinationProductId = DestinationOf(line.ProductId);
            var item = transfer.AddItem(line.ProductId, destinationProductId, line.Quantity);
            var product = products[line.ProductId];

            var source = origin.GetValueOrDefault(line.ProductId);
            var available = source?.AvailableQuantity ?? 0m;
            if (source is null || available < item.Quantity)
                throw new DomainException(
                    $"Estoque insuficiente de '{product.Name}' na origem: disponível {available:0.####}, solicitado {item.Quantity:0.####}.");

            var sourceBefore = source.CurrentQuantity;
            source.ApplyMovement(-item.Quantity);
            await _ops.AddMovementAsync(StockMovement.Create(
                _tenant.Id, line.ProductId, StockMovementType.TransferOut, item.Quantity,
                sourceBefore, source.CurrentQuantity, _user.UserId,
                referenceType: TransferReference, referenceId: transfer.Id,
                notes: $"Transferência #{transfer.Number}").InStore(from), ct);

            var target = destination.GetValueOrDefault(destinationProductId);
            if (target is null)
            {
                target = StockItem.CreateInStore(_tenant.Id, to, destinationProductId);
                await _ops.AddStockItemAsync(target, ct);
            }
            var targetBefore = target.CurrentQuantity;
            target.ApplyMovement(item.Quantity);
            await _ops.AddMovementAsync(StockMovement.Create(
                _tenant.Id, destinationProductId, StockMovementType.TransferIn, item.Quantity,
                targetBefore, target.CurrentQuantity, _user.UserId,
                referenceType: TransferReference, referenceId: transfer.Id,
                notes: $"Transferência #{transfer.Number}").InStore(to), ct);
        }

        await _ops.AddTransferAsync(transfer, ct);
        _audit.Stage(AuditActions.StockTransfer, AuditSeverity.Info, "StockTransfer", transfer.Id.ToString(),
            $"Transferência #{transfer.Number}: {transfer.Items.Count} produto(s) para outra loja",
            tenantId: _tenant.Id, actorId: _user.UserId, actorName: _user.Name,
            metadata: new { transfer.Number, From = from, To = to });

        await _ops.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return MapTransfer(transfer, products, from);
    }

    public async Task<IReadOnlyList<StockTransferDto>> ListTransfersAsync(CancellationToken ct = default)
    {
        var store = StoreId();
        var transfers = await _ops.ListTransfersAsync(store, ct);
        var products = await _ops.GetProductsAsync(
            transfers.SelectMany(t => t.Items).SelectMany(i => new[] { i.ProductId, i.DestinationProductId }), ct);
        return transfers.Select(t => MapTransfer(t, products, store)).ToList();
    }

    // ── helpers ─────────────────────────────────────────────────────────────

    private Guid StoreId() => _store.IsResolved
        ? _store.Id
        : throw new DomainException("Selecione uma loja para operar o estoque.");

    private static InventoryCountDto MapCount(InventoryCount c, IReadOnlyDictionary<Guid, Product> products) => new(
        c.Id, c.Number, c.Status.ToString(), c.Notes, c.CreatedAt, c.ClosedAt,
        c.Items.Count(i => i.CountedQuantity.HasValue),
        c.Items
            .Select(i => new InventoryCountItemDto(
                i.ProductId,
                products.GetValueOrDefault(i.ProductId)?.Name ?? string.Empty,
                products.GetValueOrDefault(i.ProductId)?.Code ?? string.Empty,
                i.ExpectedQuantity, i.CountedQuantity, i.SystemQuantityAtCount, i.SystemQuantityAtApply, i.Difference))
            .OrderBy(i => i.ProductName)
            .ToList());

    private static StockTransferDto MapTransfer(StockTransfer t, IReadOnlyDictionary<Guid, Product> products, Guid currentStore) => new(
        t.Id, t.Number, t.FromStoreId, t.ToStoreId, t.FromStoreId == currentStore ? "out" : "in", t.Notes, t.CreatedAt,
        t.Items.Select(i =>
        {
            // Each store only sees its own catalogue: show the side that belongs to the current store.
            var mine = t.FromStoreId == currentStore ? i.ProductId : i.DestinationProductId;
            return new StockTransferItemDto(mine, products.GetValueOrDefault(mine)?.Name ?? string.Empty, i.Quantity);
        }).ToList());
}
