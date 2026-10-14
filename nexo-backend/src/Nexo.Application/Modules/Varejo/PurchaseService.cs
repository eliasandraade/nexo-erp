using Nexo.Application.Common.Interfaces;
using Nexo.Application.Modules.Varejo.Interfaces;
using Nexo.Domain.Entities;
using Nexo.Domain.Enums;
using Nexo.Domain.Exceptions;
using Nexo.Domain.Modules.Varejo;

namespace Nexo.Application.Modules.Varejo;

/// <summary>
/// Compras (Core): pedido → envio ao fornecedor → recebimento. The purchase belongs to the store
/// that receives the goods. Receiving it, atomically and under the purchase lock:
///   - stock entry (PurchaseEntry) for products that track stock;
///   - product cost = last purchase cost;
///   - a conta a pagar to the supplier (pending until its due date, or already paid).
/// Cancelling a received purchase takes the goods back out (never below zero) and reverses the
/// financeiro: a pending payable is cancelled; a paid one becomes a receivable from the supplier.
/// </summary>
public class PurchaseService
{
    public const string PurchaseReference = "Purchase";
    public const string PurchaseCancellationReference = "PurchaseCancellation";

    private readonly IPurchaseRepository _purchases;
    private readonly IProductRepository  _products;
    private readonly IStockRepository    _stock;
    private readonly ISupplierRepository _suppliers;
    private readonly IFinancialRepository _financial;
    private readonly IUnitOfWork         _uow;
    private readonly IAuditWriter        _audit;
    private readonly ICurrentTenant      _currentTenant;
    private readonly ICurrentStore       _currentStore;
    private readonly ICurrentUser        _currentUser;

    public PurchaseService(
        IPurchaseRepository purchases,
        IProductRepository  products,
        IStockRepository    stock,
        ISupplierRepository suppliers,
        IFinancialRepository financial,
        IUnitOfWork         uow,
        IAuditWriter        audit,
        ICurrentTenant      currentTenant,
        ICurrentStore       currentStore,
        ICurrentUser        currentUser)
    {
        _purchases     = purchases;
        _products      = products;
        _stock         = stock;
        _suppliers     = suppliers;
        _financial     = financial;
        _uow           = uow;
        _audit         = audit;
        _currentTenant = currentTenant;
        _currentStore  = currentStore;
        _currentUser   = currentUser;
    }

    // ── Queries ───────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<PurchaseDto>> GetAllAsync(CancellationToken ct = default)
    {
        var list = await _purchases.GetAllAsync(ct);
        return await MapAsync(list, ct);
    }

    public async Task<PurchaseDto> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var purchase = await _purchases.GetByIdWithItemsAsync(id, ct)
            ?? throw new NotFoundException("Purchase", id);
        return (await MapAsync([purchase], ct))[0];
    }

    // ── Commands ──────────────────────────────────────────────────────────────

    public async Task<PurchaseDto> CreateAsync(CreatePurchaseRequest request, CancellationToken ct = default)
    {
        var supplier = await _suppliers.GetByIdAsync(request.SupplierId, ct)
            ?? throw new NotFoundException("Supplier", request.SupplierId);
        if (!supplier.IsActive)
            throw new DomainException("Este fornecedor está inativo.");
        if (!_currentStore.IsResolved)
            throw new DomainException("Selecione a loja que vai receber a compra.");

        await using var tx = await _uow.BeginTransactionAsync(ct);
        await _purchases.LockNumberingAsync(_currentStore.Id, ct);
        var number = await _purchases.GetNextNumberAsync(ct);

        var purchase = RetPurchase.Create(
            tenantId:       _currentTenant.Id,
            supplierId:     request.SupplierId,
            userId:         _currentUser.UserId,
            purchaseNumber: number,
            invoiceNumber:  request.InvoiceNumber,
            receivedAt:     request.ReceivedAt,
            notes:          request.Notes);

        await _purchases.AddAsync(purchase, ct);
        await _purchases.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await GetByIdAsync(purchase.Id, ct);
    }

    public async Task<PurchaseDto> AddItemAsync(Guid purchaseId, AddPurchaseItemRequest request, CancellationToken ct = default)
    {
        // Under the purchase lock: an item can't sneak in while the purchase is being received.
        await using var tx = await _uow.BeginTransactionAsync(ct);
        await _purchases.LockAsync(purchaseId, _currentTenant.Id, ct);
        var purchase = await _purchases.GetByIdWithItemsAsync(purchaseId, ct)
            ?? throw new NotFoundException("Purchase", purchaseId);

        var product = await _products.GetByIdAsync(request.ProductId, ct)
            ?? throw new NotFoundException("Product", request.ProductId);
        if (!product.IsActive)
            throw new DomainException($"O produto '{product.Name}' está inativo.");

        var item = purchase.AddItem(_currentTenant.Id, request.ProductId, request.Quantity, request.UnitCost, request.Notes);
        _purchases.TrackItem(item);
        await _purchases.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await GetByIdAsync(purchaseId, ct);
    }

    public async Task<PurchaseDto> RemoveItemAsync(Guid purchaseId, Guid itemId, CancellationToken ct = default)
    {
        await using var tx = await _uow.BeginTransactionAsync(ct);
        await _purchases.LockAsync(purchaseId, _currentTenant.Id, ct);
        var purchase = await _purchases.GetByIdWithItemsAsync(purchaseId, ct)
            ?? throw new NotFoundException("Purchase", purchaseId);

        purchase.RemoveItem(itemId);
        await _purchases.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await GetByIdAsync(purchaseId, ct);
    }

    /// <summary>Envia o pedido ao fornecedor: itens congelados, sem efeito em estoque ou financeiro.</summary>
    public async Task<PurchaseDto> OrderAsync(Guid purchaseId, CancellationToken ct = default)
    {
        await using var tx = await _uow.BeginTransactionAsync(ct);
        await _purchases.LockAsync(purchaseId, _currentTenant.Id, ct);
        var purchase = await _purchases.GetByIdWithItemsAsync(purchaseId, ct)
            ?? throw new NotFoundException("Purchase", purchaseId);

        purchase.MarkOrdered();
        await _purchases.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await GetByIdAsync(purchaseId, ct);
    }

    /// <summary>Recebimento: estoque + custo + conta a pagar, atomically.</summary>
    public async Task<PurchaseDto> ConfirmAsync(Guid purchaseId, ReceivePurchaseRequest? request = null, CancellationToken ct = default)
    {
        request ??= new ReceivePurchaseRequest();
        await using var tx = await _uow.BeginTransactionAsync(ct);
        try
        {
            await _purchases.LockAsync(purchaseId, _currentTenant.Id, ct);
            var purchase = await _purchases.GetByIdWithItemsAsync(purchaseId, ct)
                ?? throw new NotFoundException("Purchase", purchaseId);

            purchase.Confirm(request.ReceivedAt, request.InvoiceNumber);

            var createdStock = new Dictionary<Guid, StockItem>();
            foreach (var item in purchase.Items)
            {
                var product = await _products.GetByIdAsync(item.ProductId, ct)
                    ?? throw new NotFoundException("Product", item.ProductId);

                if (product.TrackStock)
                {
                    var stockItem = createdStock.GetValueOrDefault(item.ProductId)
                                    ?? await _stock.GetByProductIdAsync(item.ProductId, ct);
                    if (stockItem is null)
                    {
                        stockItem = StockItem.Create(_currentTenant.Id, item.ProductId);
                        await _stock.AddStockItemAsync(stockItem, ct);
                        createdStock[item.ProductId] = stockItem;
                    }

                    var qtyBefore = stockItem.CurrentQuantity;
                    stockItem.ApplyMovement(item.Quantity);
                    await _stock.AddMovementAsync(StockMovement.Create(
                        tenantId:          _currentTenant.Id,
                        productId:         item.ProductId,
                        movementType:      StockMovementType.PurchaseEntry,
                        quantity:          item.Quantity,
                        quantityBefore:    qtyBefore,
                        quantityAfter:     stockItem.CurrentQuantity,
                        createdByUserId:   _currentUser.UserId,
                        referenceType:     PurchaseReference,
                        referenceId:       purchase.Id,
                        notes:             $"Compra #{purchase.PurchaseNumber}",
                        costPriceSnapshot: item.UnitCost), ct);
                }

                // Custo do produto: último custo de compra
                product.UpdatePrices(costPrice: item.UnitCost, salePrice: product.SalePrice);
            }

            // Conta a pagar ao fornecedor
            if (purchase.TotalAmount > 0)
            {
                var account = await _financial.GetDefaultByTypeAsync(FinancialAccountType.Payable, ct)
                    ?? throw new DomainException("No active 'Contas a Pagar' account found for this tenant.");
                var supplier = await _suppliers.GetByIdAsync(purchase.SupplierId, ct);
                var supplierName = supplier?.TradeName ?? supplier?.Name ?? "fornecedor";
                // Vencimento: o informado no recebimento, senão o prazo do fornecedor a partir do recebimento.
                var dueDate = request.DueDate?.ToUniversalTime()
                              ?? (purchase.ReceivedAt ?? DateTime.UtcNow).AddDays(supplier?.PaymentTermsDays ?? 0);
                var payable = FinancialTransaction.Create(
                    _currentTenant.Id, account.Id, TransactionType.Payable, purchase.TotalAmount,
                    $"Compra #{purchase.PurchaseNumber} — {supplierName}" +
                        (purchase.InvoiceNumber is null ? "" : $" (NF {purchase.InvoiceNumber})"),
                    dueDate,
                    _currentUser.UserId,
                    referenceType: PurchaseReference, referenceId: purchase.Id);
                if (request.PaidNow) payable.MarkPaid(DateTime.UtcNow);
                await _financial.AddTransactionAsync(payable, ct);
            }

            _audit.Stage(AuditActions.PurchaseReceived, AuditSeverity.Info, "Purchase", purchase.Id.ToString(),
                $"Compra #{purchase.PurchaseNumber} recebida: {purchase.TotalAmount:F2}",
                tenantId: _currentTenant.Id, actorId: _currentUser.UserId, actorName: _currentUser.Name,
                metadata: new { purchase.PurchaseNumber, purchase.TotalAmount, request.PaidNow });

            await _purchases.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return await GetByIdAsync(purchaseId, ct);
        }
        catch (Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException)
        {
            // Stock of one of the products changed concurrently (a sale, a count) — nothing was saved.
            throw new DomainException("O estoque mudou durante a operação. Tente novamente.");
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    /// <summary>
    /// Cancela a compra. Recebida: a mercadoria sai do estoque (nunca abaixo de zero) e o financeiro
    /// é estornado. Nada é apagado.
    /// </summary>
    public async Task<PurchaseDto> CancelAsync(Guid purchaseId, string? reason = null, CancellationToken ct = default)
    {
        await using var tx = await _uow.BeginTransactionAsync(ct);
        try
        {
            await _purchases.LockAsync(purchaseId, _currentTenant.Id, ct);
            var purchase = await _purchases.GetByIdWithItemsAsync(purchaseId, ct)
                ?? throw new NotFoundException("Purchase", purchaseId);

            var wasReceived = purchase.WasConfirmed;
            purchase.Cancel(reason);

            if (wasReceived)
            {
                // 1. Goods back out — only what entered, and never into negative stock
                var entries = (await _stock.GetMovementsByReferenceAsync(PurchaseReference, purchase.Id, ct))
                    .Where(m => m.MovementType == StockMovementType.PurchaseEntry)
                    .ToList();
                foreach (var entry in entries)
                {
                    var stockItem = await _stock.GetByProductIdAsync(entry.ProductId, ct)
                        ?? throw new DomainException("Estoque do produto não encontrado para estornar a compra.");
                    if (stockItem.CurrentQuantity < entry.Quantity)
                        throw new DomainException(
                            $"Não é possível cancelar: parte da mercadoria já saiu do estoque " +
                            $"(disponível {stockItem.CurrentQuantity:0.####}, a estornar {entry.Quantity:0.####}). " +
                            "Registre uma devolução ao fornecedor ou ajuste o estoque.");

                    var qtyBefore = stockItem.CurrentQuantity;
                    stockItem.ApplyMovement(-entry.Quantity);
                    await _stock.AddMovementAsync(StockMovement.Create(
                        tenantId:        _currentTenant.Id,
                        productId:       entry.ProductId,
                        movementType:    StockMovementType.PurchaseReversal,
                        quantity:        entry.Quantity,
                        quantityBefore:  qtyBefore,
                        quantityAfter:   stockItem.CurrentQuantity,
                        createdByUserId: _currentUser.UserId,
                        referenceType:   PurchaseCancellationReference,
                        referenceId:     purchase.Id,
                        notes:           $"Compra #{purchase.PurchaseNumber} cancelada"), ct);
                }

                // 2. Financeiro: pending payable cancelled; paid payable → receivable from the supplier
                var payables = await _financial.GetTransactionsByReferenceAsync(PurchaseReference, purchase.Id, ct);
                foreach (var payable in payables.Where(t => t.TransactionType == TransactionType.Payable))
                {
                    if (payable.Status is TransactionStatus.Pending or TransactionStatus.Overdue)
                    {
                        payable.Cancel();
                    }
                    else if (payable.Status == TransactionStatus.Paid)
                    {
                        var account = await _financial.GetDefaultByTypeAsync(FinancialAccountType.Receivable, ct)
                            ?? throw new DomainException("No active 'Contas a Receber' account found for this tenant.");
                        await _financial.AddTransactionAsync(FinancialTransaction.Create(
                            _currentTenant.Id, account.Id, TransactionType.Receivable, payable.Amount,
                            $"Estorno — {payable.Description} (a receber do fornecedor)", DateTime.UtcNow,
                            _currentUser.UserId,
                            referenceType: PurchaseCancellationReference, referenceId: purchase.Id), ct);
                    }
                }
            }

            _audit.Stage(AuditActions.PurchaseCancelled, AuditSeverity.Warning, "Purchase", purchase.Id.ToString(),
                $"Compra #{purchase.PurchaseNumber} cancelada" + (purchase.CancellationReason is null ? "" : $": {purchase.CancellationReason}"),
                tenantId: _currentTenant.Id, actorId: _currentUser.UserId, actorName: _currentUser.Name,
                metadata: new { purchase.PurchaseNumber, WasReceived = wasReceived });

            await _purchases.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return await GetByIdAsync(purchaseId, ct);
        }
        catch (Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException)
        {
            // Stock of one of the products changed concurrently (a sale, a count) — nothing was saved.
            throw new DomainException("O estoque mudou durante a operação. Tente novamente.");
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    // ── Mapping ───────────────────────────────────────────────────────────────

    private async Task<IReadOnlyList<PurchaseDto>> MapAsync(IReadOnlyList<RetPurchase> purchases, CancellationToken ct)
    {
        var products = await _purchases.GetProductLabelsAsync(purchases.SelectMany(p => p.Items).Select(i => i.ProductId), ct);
        var suppliers = await _purchases.GetSupplierNamesAsync(purchases.Select(p => p.SupplierId), ct);
        return purchases.Select(p => new PurchaseDto(
            Id:             p.Id,
            PurchaseNumber: p.PurchaseNumber,
            Status:         p.Status.ToString(),
            SupplierId:     p.SupplierId,
            SupplierName:   suppliers.GetValueOrDefault(p.SupplierId) ?? string.Empty,
            TotalAmount:    p.TotalAmount,
            InvoiceNumber:  p.InvoiceNumber,
            ReceivedAt:     p.ReceivedAt,
            ConfirmedAt:    p.ConfirmedAt,
            CancelledAt:    p.CancelledAt,
            Notes:          p.Notes,
            Items:          p.Items.Select(i => new PurchaseItemDto(
                Id:          i.Id,
                ProductId:   i.ProductId,
                ProductName: products.TryGetValue(i.ProductId, out var l) ? l.Name : string.Empty,
                ProductCode: products.TryGetValue(i.ProductId, out var c) ? c.Code : string.Empty,
                Quantity:    i.Quantity,
                UnitCost:    i.UnitCost,
                Total:       i.Total,
                Notes:       i.Notes)).ToList(),
            CreatedAt:          p.CreatedAt,
            OrderedAt:          p.OrderedAt,
            CancellationReason: p.CancellationReason)).ToList();
    }
}
