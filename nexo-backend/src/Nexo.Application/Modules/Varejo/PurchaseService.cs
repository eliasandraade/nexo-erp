using Microsoft.EntityFrameworkCore;
using Nexo.Application.Common.Interfaces;
using Nexo.Application.Modules.Varejo.Interfaces;
using Nexo.Domain.Entities;
using Nexo.Domain.Enums;
using Nexo.Domain.Exceptions;
using Nexo.Domain.Modules.Varejo;

namespace Nexo.Application.Modules.Varejo;

/// <summary>
/// Gerencia o ciclo de vida das compras (entrada de mercadoria).
///
/// ConfirmAsync: atômica — StockMovement(PurchaseEntry) + UpdateCostPrice na mesma transação.
/// CancelAsync:  atômica — reverte estoque com StockMovement(ManualExit) compensatório.
/// </summary>
public class PurchaseService
{
    private readonly IPurchaseRepository _purchases;
    private readonly IProductRepository  _products;
    private readonly IStockRepository    _stock;
    private readonly IUnitOfWork         _uow;
    private readonly ICurrentTenant      _currentTenant;
    private readonly ICurrentUser        _currentUser;

    public PurchaseService(
        IPurchaseRepository purchases,
        IProductRepository  products,
        IStockRepository    stock,
        IUnitOfWork         uow,
        ICurrentTenant      currentTenant,
        ICurrentUser        currentUser)
    {
        _purchases     = purchases;
        _products      = products;
        _stock         = stock;
        _uow           = uow;
        _currentTenant = currentTenant;
        _currentUser   = currentUser;
    }

    // ── Queries ───────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<PurchaseDto>> GetAllAsync(CancellationToken ct = default)
    {
        var list = await _purchases.GetAllAsync(ct);
        return list.Select(p => MapToDto(p)).ToList();
    }

    public async Task<PurchaseDto> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var purchase = await _purchases.GetByIdWithItemsAsync(id, ct)
            ?? throw new NotFoundException("Purchase", id);
        return MapToDto(purchase);
    }

    // ── Commands ──────────────────────────────────────────────────────────────

    public async Task<PurchaseDto> CreateAsync(CreatePurchaseRequest request, CancellationToken ct = default)
    {
        var number = await _purchases.GetNextNumberAsync(ct);

        var purchase = RetPurchase.Create(
            tenantId:      _currentTenant.Id,
            supplierId:    request.SupplierId,
            userId:        _currentUser.UserId,
            purchaseNumber: number,
            invoiceNumber: request.InvoiceNumber,
            receivedAt:    request.ReceivedAt,
            notes:         request.Notes);

        await _purchases.AddAsync(purchase, ct);
        await _purchases.SaveChangesAsync(ct);
        return MapToDto(purchase);
    }

    public async Task<PurchaseDto> AddItemAsync(Guid purchaseId, AddPurchaseItemRequest request, CancellationToken ct = default)
    {
        var purchase = await _purchases.GetByIdWithItemsAsync(purchaseId, ct)
            ?? throw new NotFoundException("Purchase", purchaseId);

        var product = await _products.GetByIdAsync(request.ProductId, ct)
            ?? throw new NotFoundException("Product", request.ProductId);

        purchase.AddItem(
            tenantId:  _currentTenant.Id,
            productId: product.Id,
            quantity:  request.Quantity,
            unitCost:  request.UnitCost,
            notes:     request.Notes);

        await _purchases.SaveChangesAsync(ct);
        return MapToDto(purchase);
    }

    public async Task<PurchaseDto> RemoveItemAsync(Guid purchaseId, Guid itemId, CancellationToken ct = default)
    {
        var purchase = await _purchases.GetByIdWithItemsAsync(purchaseId, ct)
            ?? throw new NotFoundException("Purchase", purchaseId);

        purchase.RemoveItem(itemId);
        await _purchases.SaveChangesAsync(ct);
        return MapToDto(purchase);
    }

    /// <summary>
    /// Confirma a compra.
    /// Para cada item:
    ///   1. Registra StockMovement(PurchaseEntry)
    ///   2. Atualiza product.CostPrice com o custo de compra (último custo)
    /// Tudo em uma única transação.
    /// </summary>
    public async Task<PurchaseDto> ConfirmAsync(Guid purchaseId, CancellationToken ct = default)
    {
        await using var tx = await _uow.BeginTransactionAsync(ct);
        try
        {
            var purchase = await _purchases.GetByIdWithItemsAsync(purchaseId, ct)
                ?? throw new NotFoundException("Purchase", purchaseId);

            // Confirma a compra (valida estado e itens)
            purchase.Confirm();

            foreach (var item in purchase.Items)
            {
                var product = await _products.GetByIdAsync(item.ProductId, ct)
                    ?? throw new NotFoundException("Product", item.ProductId);

                // Garante que o StockItem existe
                var stockItem = await _stock.GetByProductIdAsync(item.ProductId, ct);
                if (stockItem is null)
                {
                    stockItem = StockItem.Create(_currentTenant.Id, item.ProductId);
                    await _stock.AddStockItemAsync(stockItem, ct);
                }

                var qtyBefore = stockItem.CurrentQuantity;
                stockItem.ApplyMovement(item.Quantity); // entrada = positivo

                var movement = StockMovement.Create(
                    tenantId:        _currentTenant.Id,
                    productId:       item.ProductId,
                    movementType:    StockMovementType.PurchaseEntry,
                    quantity:        item.Quantity,
                    quantityBefore:  qtyBefore,
                    quantityAfter:   stockItem.CurrentQuantity,
                    createdByUserId: _currentUser.UserId,
                    referenceType:   "Purchase",
                    referenceId:     purchase.Id,
                    notes:           $"Compra #{purchase.PurchaseNumber} – item {item.Id}");

                await _stock.AddMovementAsync(movement, ct);

                // Atualiza custo do produto: último custo de compra
                product.UpdatePrices(costPrice: item.UnitCost, salePrice: product.SalePrice);
            }

            await _purchases.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return MapToDto(purchase);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    /// <summary>
    /// Cancela a compra.
    /// Se a compra já foi confirmada, reverte o estoque via movimentos compensatórios.
    /// </summary>
    public async Task<PurchaseDto> CancelAsync(Guid purchaseId, CancellationToken ct = default)
    {
        await using var tx = await _uow.BeginTransactionAsync(ct);
        try
        {
            var purchase = await _purchases.GetByIdWithItemsAsync(purchaseId, ct)
                ?? throw new NotFoundException("Purchase", purchaseId);

            var wasConfirmed = purchase.WasConfirmed;

            purchase.Cancel();

            if (wasConfirmed)
            {
                foreach (var item in purchase.Items)
                {
                    var stockItem = await _stock.GetByProductIdAsync(item.ProductId, ct);
                    if (stockItem is null) continue;  // produto sem controle de estoque

                    var qtyBefore = stockItem.CurrentQuantity;
                    stockItem.ApplyMovement(-item.Quantity); // saída = negativo

                    var movement = StockMovement.Create(
                        tenantId:        _currentTenant.Id,
                        productId:       item.ProductId,
                        movementType:    StockMovementType.ManualExit,
                        quantity:        item.Quantity,
                        quantityBefore:  qtyBefore,
                        quantityAfter:   stockItem.CurrentQuantity,
                        createdByUserId: _currentUser.UserId,
                        referenceType:   "Purchase",
                        referenceId:     purchase.Id,
                        notes:           $"Estorno compra #{purchase.PurchaseNumber} cancelada");

                    await _stock.AddMovementAsync(movement, ct);
                }
            }

            await _purchases.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return MapToDto(purchase);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    // ── Mapping ───────────────────────────────────────────────────────────────

    private static PurchaseDto MapToDto(RetPurchase p) => new(
        Id:           p.Id,
        PurchaseNumber: p.PurchaseNumber,
        Status:       p.Status.ToString(),
        SupplierId:   p.SupplierId,
        SupplierName: string.Empty,  // populado pelo controller via join se necessário
        TotalAmount:  p.TotalAmount,
        InvoiceNumber: p.InvoiceNumber,
        ReceivedAt:   p.ReceivedAt,
        ConfirmedAt:  p.ConfirmedAt,
        CancelledAt:  p.CancelledAt,
        Notes:        p.Notes,
        Items:        p.Items.Select(MapItemToDto).ToList(),
        CreatedAt:    p.CreatedAt);

    private static PurchaseItemDto MapItemToDto(RetPurchaseItem i) => new(
        Id:          i.Id,
        ProductId:   i.ProductId,
        ProductName: string.Empty,  // populado pelo join
        ProductCode: string.Empty,
        Quantity:    i.Quantity,
        UnitCost:    i.UnitCost,
        Total:       i.Total,
        Notes:       i.Notes);
}
