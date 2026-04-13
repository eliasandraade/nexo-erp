using Microsoft.EntityFrameworkCore;
using Nexo.Application.Common.Interfaces;
using Nexo.Domain.Entities;
using Nexo.Domain.Enums;
using Nexo.Domain.Exceptions;

namespace Nexo.Application.Features.Sales;

public class SaleService
{
    private readonly ISaleRepository _sales;
    private readonly IProductRepository _products;
    private readonly IStockRepository _stock;
    private readonly ICashRepository _cash;
    private readonly IFinancialRepository _financial;
    private readonly IUnitOfWork _uow;
    private readonly ICurrentTenant _currentTenant;
    private readonly ICurrentUser _currentUser;

    public SaleService(
        ISaleRepository sales,
        IProductRepository products,
        IStockRepository stock,
        ICashRepository cash,
        IFinancialRepository financial,
        IUnitOfWork uow,
        ICurrentTenant currentTenant,
        ICurrentUser currentUser)
    {
        _sales         = sales;
        _products      = products;
        _stock         = stock;
        _cash          = cash;
        _financial     = financial;
        _uow           = uow;
        _currentTenant = currentTenant;
        _currentUser   = currentUser;
    }

    // ── Queries ───────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<SaleDto>> GetAllAsync(CancellationToken ct = default)
    {
        var list = await _sales.GetAllAsync(ct);
        return list.Select(s => MapToDto(s, [], [])).ToList();
    }

    public async Task<SaleDto> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var sale = await _sales.GetByIdWithItemsAsync(id, ct)
            ?? throw new NotFoundException("Sale", id);
        return MapToDto(sale, sale.Items.ToList(), sale.Payments.ToList());
    }

    // ── Commands ──────────────────────────────────────────────────────────────

    public async Task<SaleDto> CreateAsync(CreateSaleRequest request, CancellationToken ct = default)
    {
        var number = await _sales.GetNextNumberAsync(ct);

        var sale = Sale.Create(
            _currentTenant.Id,
            number,
            _currentUser.UserId,
            request.CustomerId,
            request.CashSessionId,
            request.Notes);

        await _sales.AddAsync(sale, ct);
        await _sales.SaveChangesAsync(ct);
        return MapToDto(sale, [], []);
    }

    public async Task<SaleDto> AddItemAsync(Guid saleId, AddSaleItemRequest request, CancellationToken ct = default)
    {
        var sale = await _sales.GetByIdWithItemsAsync(saleId, ct)
            ?? throw new NotFoundException("Sale", saleId);

        if (!sale.IsInDraft)
            throw new DomainException($"Sale #{sale.Number} is not in Draft. Cannot add items.");

        var product = await _products.GetByIdAsync(request.ProductId, ct)
            ?? throw new NotFoundException("Product", request.ProductId);

        if (!product.IsActive)
            throw new DomainException($"Product '{product.Name}' is inactive.");

        var item = SaleItem.Create(
            _currentTenant.Id,
            sale.Id,
            product.Id,
            request.Quantity,
            request.UnitPrice,
            product.CostPrice,       // snapshot cost at time of sale
            request.DiscountAmount,
            request.Notes);

        _sales.TrackItem(item);

        var allItems = sale.Items.ToList();
        allItems.Add(item);
        sale.RecalculateTotals(allItems);

        await _sales.SaveChangesAsync(ct);
        return MapToDto(sale, allItems, []);
    }

    /// <summary>
    /// Confirms a Draft sale atomically:
    ///   1. Validates payment sum == sale total
    ///   2. Deducts stock (with optimistic concurrency guard)
    ///   3. Creates SalePayment records
    ///   4. Generates CashMovement for each Cash payment
    ///   5. Generates FinancialTransaction for each Credit payment
    ///   6. Transitions sale: Draft → Confirmed (→ Paid if fully cash)
    ///
    /// Throws DbUpdateConcurrencyException if stock was modified by a concurrent transaction.
    /// Caller should retry on concurrency conflicts.
    /// </summary>
    public async Task<SaleDto> ConfirmAsync(Guid saleId, ConfirmSaleRequest request, CancellationToken ct = default)
    {
        if (request.Payments is null || request.Payments.Count == 0)
            throw new DomainException("At least one payment is required to confirm a sale.");

        await using var tx = await _uow.BeginTransactionAsync(ct);
        try
        {
            var sale = await _sales.GetByIdWithItemsAsync(saleId, ct)
                ?? throw new NotFoundException("Sale", saleId);

            if (!sale.IsInDraft)
                throw new DomainException($"Sale #{sale.Number} cannot be confirmed. Current status: {sale.Status}.");

            if (!sale.Items.Any())
                throw new DomainException("Cannot confirm a sale with no items.");

            // Recalculate totals with final discount/tax
            sale.RecalculateTotals(sale.Items, request.DiscountAmount, request.TaxAmount, request.SurchargesAmount);

            // Validate: sum of payments must equal sale total
            var paymentTotal = request.Payments.Sum(p => p.Amount);
            if (paymentTotal != sale.Total)
                throw new DomainException(
                    $"Payment total ({paymentTotal:C}) does not match sale total ({sale.Total:C}).");

            // Validate individual payment inputs
            foreach (var p in request.Payments)
            {
                if (p.Amount <= 0)
                    throw new DomainException("Payment amounts must be positive.");

                if (string.Equals(p.Type, "Credit", StringComparison.OrdinalIgnoreCase) && p.DueDate is null)
                    throw new DomainException("DueDate is required for credit (a prazo) payments.");
            }

            // 1. Deduct stock for all tracked products
            foreach (var item in sale.Items)
            {
                // Skip products whose stock is managed by a recipe card (restaurant flow).
                if (request.SkipStockProductIds?.Contains(item.ProductId) == true) continue;

                var product = await _products.GetByIdAsync(item.ProductId, ct);
                if (product?.TrackStock != true) continue;

                var stockItem = await _stock.GetByProductIdAsync(item.ProductId, ct)
                    ?? throw new DomainException($"No stock record found for product '{product.Name}'.");

                if (stockItem.AvailableQuantity < item.Quantity)
                    throw new DomainException(
                        $"Insufficient stock for '{product.Name}': available {stockItem.AvailableQuantity}, required {item.Quantity}.");

                var before = stockItem.CurrentQuantity;
                stockItem.ApplyMovement(-item.Quantity);

                var movement = StockMovement.Create(
                    _currentTenant.Id,
                    item.ProductId,
                    StockMovementType.SaleOutput,
                    item.Quantity,
                    before,
                    stockItem.CurrentQuantity,
                    _currentUser.UserId,
                    referenceType: "Sale",
                    referenceId: sale.Id);

                await _stock.AddMovementAsync(movement, ct);
            }

            // 2. Auto-link open cash session of the selling user (if not already set)
            if (sale.CashSessionId is null)
            {
                var openSession = await _cash.GetOpenSessionByUserAsync(_currentUser.UserId, ct);
                if (openSession is not null)
                    sale.LinkCashSession(openSession.Id);
            }

            // 3. Create SalePayment records + generate CashMovements / FinancialTransactions
            var hasCreditPayment = false;

            foreach (var p in request.Payments)
            {
                var method      = Enum.Parse<PaymentMethod>(p.Method, ignoreCase: true);
                var paymentType = Enum.Parse<PaymentType>(p.Type, ignoreCase: true);

                var payment = SalePayment.Create(
                    _currentTenant.Id,
                    sale.Id,
                    method,
                    paymentType,
                    p.Amount,
                    p.DueDate);

                await _sales.AddPaymentAsync(payment, ct);

                if (paymentType == PaymentType.Cash)
                {
                    // À vista: generate CashMovement in the linked session
                    if (sale.CashSessionId.HasValue)
                    {
                        var cashMovement = CashMovement.Create(
                            _currentTenant.Id,
                            sale.CashSessionId.Value,
                            CashMovementType.SaleReceipt,
                            p.Amount,
                            $"Venda #{sale.Number} — {method}",
                            _currentUser.UserId,
                            referenceType: "Sale",
                            referenceId: sale.Id);

                        await _cash.AddMovementAsync(cashMovement, ct);
                    }
                }
                else // Credit
                {
                    hasCreditPayment = true;

                    // A prazo: generate FinancialTransaction (Receivable)
                    var receivableAccount = await _financial.GetDefaultByTypeAsync(FinancialAccountType.Receivable, ct)
                        ?? throw new DomainException(
                            "No active 'Contas a Receber' account found for this tenant. " +
                            "Please set up a default Receivable account in financial settings.");

                    var transaction = FinancialTransaction.Create(
                        _currentTenant.Id,
                        receivableAccount.Id,
                        TransactionType.Receivable,
                        p.Amount,
                        $"Venda #{sale.Number} — {method} a prazo",
                        p.DueDate!.Value,
                        _currentUser.UserId,
                        referenceType: "Sale",
                        referenceId: sale.Id);

                    await _financial.AddTransactionAsync(transaction, ct);
                }
            }

            // 4. Transition state machine
            sale.Confirm();
            if (!hasCreditPayment)
                sale.MarkPaid();  // All cash → immediately Paid

            await _sales.SaveChangesAsync(ct);
            await _stock.SaveChangesAsync(ct);

            await tx.CommitAsync(ct);

            return MapToDto(sale, sale.Items.ToList(), sale.Payments.ToList());
        }
        catch (DbUpdateConcurrencyException)
        {
            // Stock was modified by a concurrent transaction — surface as a domain conflict
            throw new DomainException(
                "Stock quantity changed concurrently. Please retry the sale confirmation.");
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    /// <summary>
    /// Cancels a sale atomically.
    /// - Draft cancellation: no side-effects.
    /// - Confirmed cancellation: reverses stock, reverses CashMovements, cancels FinancialTransactions.
    /// </summary>
    public async Task CancelAsync(Guid saleId, CancellationToken ct = default)
    {
        await using var tx = await _uow.BeginTransactionAsync(ct);
        try
        {
            var sale = await _sales.GetByIdWithItemsAsync(saleId, ct)
                ?? throw new NotFoundException("Sale", saleId);

            // Domain enforces: cannot cancel Paid sales
            sale.Cancel();

            // Reverse side-effects only if stock was already deducted (sale was Confirmed)
            if (sale.WasConfirmed)
            {
                // 1. Reverse stock
                foreach (var item in sale.Items)
                {
                    var product = await _products.GetByIdAsync(item.ProductId, ct);
                    if (product?.TrackStock != true) continue;

                    var stockItem = await _stock.GetByProductIdAsync(item.ProductId, ct);
                    if (stockItem is null) continue;

                    var before = stockItem.CurrentQuantity;
                    stockItem.ApplyMovement(item.Quantity);  // restore

                    var reversal = StockMovement.Create(
                        _currentTenant.Id,
                        item.ProductId,
                        StockMovementType.ReturnEntry,
                        item.Quantity,
                        before,
                        stockItem.CurrentQuantity,
                        _currentUser.UserId,
                        referenceType: "SaleCancellation",
                        referenceId: sale.Id);

                    await _stock.AddMovementAsync(reversal, ct);
                }

                // 2. Reverse CashMovements (compensating entry)
                if (sale.CashSessionId.HasValue)
                {
                    var cashPayments = sale.Payments.Where(p => p.Type == PaymentType.Cash);
                    foreach (var p in cashPayments)
                    {
                        var reversal = CashMovement.Create(
                            _currentTenant.Id,
                            sale.CashSessionId.Value,
                            CashMovementType.Withdrawal,
                            p.Amount,
                            $"Estorno Venda #{sale.Number}",
                            _currentUser.UserId,
                            referenceType: "SaleCancellation",
                            referenceId: sale.Id);

                        await _cash.AddMovementAsync(reversal, ct);
                    }
                }

                // 3. Cancel open FinancialTransactions linked to this sale
                var openTransactions = await _financial.GetTransactionsBySaleAsync(sale.Id, ct);
                foreach (var finTx in openTransactions.Where(t => t.Status == TransactionStatus.Pending || t.Status == TransactionStatus.Overdue))
                    finTx.Cancel();
            }

            await _sales.SaveChangesAsync(ct);
            await _stock.SaveChangesAsync(ct);

            await tx.CommitAsync(ct);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    // ── Mapping ───────────────────────────────────────────────────────────────

    private static SaleDto MapToDto(Sale s, IReadOnlyList<SaleItem> items, IReadOnlyList<SalePayment> payments) => new(
        s.Id,
        s.Number,
        s.Status.ToString(),
        s.CustomerId,
        s.Customer?.Name,
        s.SoldByUserId,
        s.SoldBy?.FullName ?? string.Empty,
        s.CashSessionId,
        s.Subtotal,
        s.DiscountAmount,
        s.TaxAmount,
        s.Total,
        s.Notes,
        s.ConfirmedAt,
        s.PaidAt,
        s.CancelledAt,
        items.Select(i => new SaleItemDto(
            i.Id,
            i.ProductId,
            i.Product?.Name ?? string.Empty,
            i.Product?.Code ?? string.Empty,
            i.Quantity,
            i.UnitPrice,
            i.CostPrice,
            i.DiscountAmount,
            i.Total,
            i.Notes)).ToList(),
        payments.Select(p => new SalePaymentDto(
            p.Id,
            p.Method.ToString(),
            p.Type.ToString(),
            p.Amount,
            p.DueDate)).ToList(),
        s.CreatedAt,
        s.UpdatedAt);
}
