using Microsoft.EntityFrameworkCore;
using Nexo.Application.Common;
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
    private readonly IAuditWriter _audit;
    private readonly ICurrentTenant _currentTenant;
    private readonly ICurrentUser _currentUser;

    public SaleService(
        ISaleRepository sales,
        IProductRepository products,
        IStockRepository stock,
        ICashRepository cash,
        IFinancialRepository financial,
        IUnitOfWork uow,
        IAuditWriter audit,
        ICurrentTenant currentTenant,
        ICurrentUser currentUser)
    {
        _audit         = audit;
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

    public Task<PagedResult<SaleListItemDto>> GetPagedAsync(
        int page, int pageSize, string? search, string? status, string? paymentMethod, CancellationToken ct = default)
        => _sales.GetPagedAsync(page, pageSize, search, status, paymentMethod, ct);

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

        // When the sale is already tracked (e.g. created a moment ago in the same request, as the
        // restaurant does on close), EF fix-up has ALREADY put the new item into sale.Items — so
        // de-duplicate by id instead of blindly appending, or the item is counted twice.
        var allItems = sale.Items.Where(i => i.Id != item.Id).Append(item).ToList();
        sale.RecalculateTotals(allItems);

        await _sales.SaveChangesAsync(ct);
        return MapToDto(sale, allItems, []);
    }

    /// <summary>ReferenceType of the financeiro lançamentos that mirror a sale (credit and settled non-cash).</summary>
    public const string SaleReference = "Sale";
    /// <summary>ReferenceType of the counter-entries posted when a sale is cancelled.</summary>
    public const string SaleCancellationReference = "SaleCancellation";

    /// <summary>
    /// Confirms a Draft sale atomically:
    ///   1. Locks the sale row — concurrent confirmations serialise; a retry of an already
    ///      confirmed sale with the same payments is answered with the sale (idempotent replay)
    ///   2. Builds the payment plan (<see cref="SalePaymentPlan"/>): validation + troco
    ///   3. Deducts stock (optimistic concurrency guard)
    ///   4. Records SalePayments and their effects — physical cash → CashMovement in an OPEN
    ///      session (net of troco); Pix/card/transfer à vista → settled revenue in the financeiro;
    ///      a prazo → pending receivable
    ///   5. Draft → Confirmed (→ Paid when nothing is a prazo)
    /// </summary>
    public async Task<SaleDto> ConfirmAsync(Guid saleId, ConfirmSaleRequest request, CancellationToken ct = default)
    {
        if (request.Payments is null || request.Payments.Count == 0)
            throw new DomainException("Informe ao menos um pagamento para concluir a venda.");

        await using var tx = await _uow.BeginTransactionAsync(ct);
        try
        {
            await _sales.LockAsync(saleId, ct);
            var sale = await _sales.GetByIdWithItemsAsync(saleId, ct)
                ?? throw new NotFoundException("Sale", saleId);

            if (!sale.IsInDraft)
            {
                if (IsReplayOf(sale, request))
                {
                    await tx.CommitAsync(ct);
                    return MapToDto(sale, sale.Items.ToList(), sale.Payments.ToList());
                }
                throw new ConflictException($"A venda #{sale.Number} já foi {DescribeStatus(sale.Status)}.");
            }

            if (!sale.Items.Any())
                throw new DomainException("Cannot confirm a sale with no items.");

            // Recalculate totals with final discount/tax, then plan the payments against it
            sale.RecalculateTotals(sale.Items, request.DiscountAmount, request.TaxAmount, request.SurchargesAmount);
            var plan = SalePaymentPlan.Build(request.Payments, sale.Total);

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

            // 2. Physical cash needs an open drawer to land in
            if (plan.Any(p => p.MovesCashDrawer))
            {
                var sessionId = await ResolveOpenCashSessionAsync(sale, ct);
                if (sale.CashSessionId != sessionId) sale.LinkCashSession(sessionId);
            }

            // 3. Payments and their effects
            var hasCreditPayment = false;
            foreach (var p in plan)
            {
                var payment = SalePayment.Create(
                    _currentTenant.Id, sale.Id, p.Method, p.Type, p.Applied, p.DueDate, p.Change);
                await _sales.AddPaymentAsync(payment, ct);

                if (p.MovesCashDrawer)
                {
                    // Net of troco: the drawer keeps exactly what was applied to the sale.
                    await _cash.AddMovementAsync(CashMovement.Create(
                        _currentTenant.Id,
                        sale.CashSessionId!.Value,
                        CashMovementType.SaleReceipt,
                        p.Applied,
                        p.Change > 0
                            ? $"Venda #{sale.Number} — Dinheiro (recebido {p.Tendered:F2}, troco {p.Change:F2})"
                            : $"Venda #{sale.Number} — Dinheiro",
                        _currentUser.UserId,
                        referenceType: "Sale",
                        referenceId: sale.Id), ct);
                }
                else if (p.IsSettledNonCash)
                {
                    // Pix / card / transfer: money already received, outside the drawer.
                    var transaction = await NewSaleTransactionAsync(
                        FinancialAccountType.Receivable, TransactionType.Receivable, p.Applied,
                        $"Venda #{sale.Number} — {DescribeMethod(p.Method)}", DateTime.UtcNow,
                        SaleReference, sale.Id, ct);
                    transaction.MarkPaid(DateTime.UtcNow);
                    await _financial.AddTransactionAsync(transaction, ct);
                }
                else // a prazo
                {
                    hasCreditPayment = true;
                    var transaction = await NewSaleTransactionAsync(
                        FinancialAccountType.Receivable, TransactionType.Receivable, p.Applied,
                        $"Venda #{sale.Number} — {DescribeMethod(p.Method)} a prazo", p.DueDate!.Value,
                        SaleReference, sale.Id, ct);
                    await _financial.AddTransactionAsync(transaction, ct);
                }
            }

            // 4. Transition state machine
            sale.Confirm();
            if (!hasCreditPayment)
                sale.MarkPaid();  // nothing a prazo → immediately Paid

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
    /// - Confirmed cancellation: reverses stock, takes the physical cash back out of the drawer,
    ///   cancels pending receivables and posts a counter-entry for revenue already settled
    ///   (Pix/card) — history is never deleted.
    /// </summary>
    public async Task CancelAsync(Guid saleId, string? reason = null, CancellationToken ct = default)
    {
        await using var tx = await _uow.BeginTransactionAsync(ct);
        try
        {
            await _sales.LockAsync(saleId, ct);
            var sale = await _sales.GetByIdWithItemsAsync(saleId, ct)
                ?? throw new NotFoundException("Sale", saleId);

            // Domain enforces: a paid sale is undone by a devolução, not a cancellation
            sale.Cancel(_currentUser.UserId, reason);

            // Reverse side-effects only if stock was already deducted (sale was Confirmed)
            if (sale.WasConfirmed)
            {
                // 1. Reverse stock — exactly what this sale took out (recipe items of the restaurant
                //    left through their ingredients and are not re-entered as finished products)
                var outputs = (await _stock.GetMovementsByReferenceAsync("Sale", sale.Id, ct))
                    .Where(m => m.MovementType == StockMovementType.SaleOutput);
                foreach (var output in outputs)
                    await ReEnterStockAsync(output.ProductId, output.Quantity, SaleCancellationReference, sale.Id, ct);

                // 2. Drawer: reverse exactly what this sale put in it (sales confirmed before
                //    PR 43 also recorded Pix/card there). The money leaves an OPEN drawer — the
                //    original one if still open, else the current one; a closed day stays closed.
                var receipts = (await _cash.GetMovementsByReferenceAsync("Sale", sale.Id, ct))
                    .Where(m => m.MovementType == CashMovementType.SaleReceipt)
                    .ToList();
                if (receipts.Count > 0)
                {
                    var drawerId = await ResolveRefundCashSessionAsync(sale, receipts.Sum(r => r.Amount), ct);
                    foreach (var receipt in receipts)
                    {
                        await _cash.AddMovementAsync(CashMovement.Create(
                            _currentTenant.Id,
                            drawerId,
                            CashMovementType.SaleRefund,
                            receipt.Amount,
                            $"Estorno Venda #{sale.Number}",
                            _currentUser.UserId,
                            referenceType: SaleCancellationReference,
                            referenceId: sale.Id), ct);
                    }
                }

                // 3. Financeiro: cancel what is still pending, counter-entry for what was settled
                var transactions = await _financial.GetTransactionsBySaleAsync(sale.Id, ct);
                foreach (var finTx in transactions)
                {
                    if (finTx.Status is TransactionStatus.Pending or TransactionStatus.Overdue)
                    {
                        finTx.Cancel();
                    }
                    else if (finTx.Status == TransactionStatus.Paid
                             && finTx.TransactionType == TransactionType.Receivable)
                    {
                        var counter = await NewSaleTransactionAsync(
                            FinancialAccountType.Payable, TransactionType.Payable, finTx.Amount,
                            $"Estorno — {finTx.Description}", DateTime.UtcNow,
                            SaleCancellationReference, sale.Id, ct);
                        counter.MarkPaid(DateTime.UtcNow);
                        await _financial.AddTransactionAsync(counter, ct);
                    }
                }
            }

            _audit.Stage(AuditActions.SaleCancelled, AuditSeverity.Warning, "Sale", sale.Id.ToString(),
                $"Venda #{sale.Number} cancelada" + (sale.CancellationReason is null ? "" : $": {sale.CancellationReason}"),
                tenantId: _currentTenant.Id, actorId: _currentUser.UserId, actorName: _currentUser.Name,
                metadata: new { sale.Total, WasConfirmed = sale.WasConfirmed });

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

    public const string SaleReturnReference = "SaleReturn";

    /// <summary>
    /// Devolução (total or partial) of a paid sale — the reversal path for sales that already took
    /// the money. Atomically, under the sale lock:
    ///   1. replay: the same idempotency key returns the devolução already recorded;
    ///   2. each line ≤ what is still unreturned; the refund is the lines' share of the sale net of
    ///      its discount, and the devolução that empties the sale refunds exactly what is left;
    ///   3. stock re-enters only what the sale actually took out (recipe items — food served — don't);
    ///   4. money goes back by the chosen method: physical cash leaves an open drawer that holds it
    ///      (SaleRefund), Pix/card/transfer is a settled counter-entry in the financeiro;
    ///   5. the sale becomes PartiallyReturned / Returned and the trail is audited.
    /// </summary>
    public async Task<SaleReturnDto> ReturnAsync(Guid saleId, CreateSaleReturnRequest request, CancellationToken ct = default)
    {
        if (request.Items is null || request.Items.Count == 0)
            throw new DomainException("Selecione ao menos um item para devolver.");
        if (!Enum.TryParse<PaymentMethod>(request.RefundMethod, ignoreCase: true, out var refundMethod)
            || !SaleReturn.AllowedRefundMethods.Contains(refundMethod))
            throw new DomainException("Forma de reembolso inválida. Use Dinheiro, Pix, cartão ou transferência.");

        await using var tx = await _uow.BeginTransactionAsync(ct);
        try
        {
            await _sales.LockAsync(saleId, ct);

            if (request.IdempotencyKey is { } key && await _sales.GetReturnByIdempotencyKeyAsync(key, ct) is { } replay)
            {
                if (replay.SaleId != saleId)
                    throw new ConflictException("Esta chave de devolução pertence a outra venda.");
                await tx.CommitAsync(ct);
                return MapReturn(replay);
            }

            var sale = await _sales.GetByIdWithItemsAsync(saleId, ct)
                ?? throw new NotFoundException("Sale", saleId);
            if (!sale.CanBeReturned)
                throw sale.Status == SaleStatus.Confirmed
                    ? new DomainException("Esta venda ainda tem valores a prazo em aberto: cancele a venda em vez de devolver.")
                    : new ConflictException($"A venda #{sale.Number} não aceita devolução ({DescribeStatus(sale.Status)}).");

            // What is still returnable per line
            var previous = await _sales.GetReturnsBySaleAsync(sale.Id, ct);
            var returned = previous.SelectMany(r => r.Items)
                .GroupBy(i => i.SaleItemId)
                .ToDictionary(g => g.Key, g => g.Sum(i => i.Quantity));
            decimal Remaining(SaleItem i) => i.Quantity - returned.GetValueOrDefault(i.Id);

            var requested = request.Items
                .GroupBy(i => i.SaleItemId)
                .Select(g => (SaleItemId: g.Key, Quantity: g.Sum(i => i.Quantity)))
                .ToList();
            var lines = new List<(SaleItem Item, decimal Quantity)>();
            foreach (var (saleItemId, quantity) in requested)
            {
                var item = sale.Items.FirstOrDefault(i => i.Id == saleItemId)
                    ?? throw new DomainException("Item não pertence a esta venda.");
                if (quantity <= 0)
                    throw new DomainException("A quantidade devolvida deve ser positiva.");
                if (quantity > Remaining(item))
                    throw new DomainException(
                        $"Quantidade acima do disponível para devolução: restam {Remaining(item):0.####}.");
                lines.Add((item, quantity));
            }

            // Refund: the lines' share of the sale net of its discount; the devolução that empties
            // the sale refunds exactly what is left (absorbs rounding, couvert, service fee).
            var factor = sale.Subtotal > 0 ? (sale.Subtotal - sale.DiscountAmount) / sale.Subtotal : 0m;
            var amounts = lines
                .Select(l => Math.Round(l.Item.Total * factor * l.Quantity / l.Item.Quantity, 2, MidpointRounding.AwayFromZero))
                .ToList();
            var fullyReturned = sale.Items.All(i =>
                Remaining(i) - lines.Where(l => l.Item.Id == i.Id).Sum(l => l.Quantity) <= 0);
            var refund = fullyReturned ? sale.Total - sale.ReturnedAmount : amounts.Sum();
            if (fullyReturned) amounts[^1] += refund - amounts.Sum();
            if (refund <= 0)
                throw new DomainException("Não há valor a devolver para estes itens.");

            // Money back
            Guid? drawerId = refundMethod == PaymentMethod.Cash
                ? await ResolveRefundCashSessionAsync(sale, refund, ct)
                : null;

            var saleReturn = SaleReturn.Create(
                _currentTenant.Id, sale.Id, refundMethod, refund, request.Reason ?? string.Empty,
                _currentUser.UserId, request.IdempotencyKey, drawerId);
            for (var i = 0; i < lines.Count; i++)
                saleReturn.AddItem(lines[i].Item.Id, lines[i].Item.ProductId, lines[i].Quantity, amounts[i]);
            await _sales.AddReturnAsync(saleReturn, ct);

            if (drawerId is { } drawer)
            {
                await _cash.AddMovementAsync(CashMovement.Create(
                    _currentTenant.Id, drawer, CashMovementType.SaleRefund, refund,
                    $"Devolução Venda #{sale.Number}", _currentUser.UserId,
                    referenceType: SaleReturnReference, referenceId: saleReturn.Id), ct);
            }
            else
            {
                var counter = await NewSaleTransactionAsync(
                    FinancialAccountType.Payable, TransactionType.Payable, refund,
                    $"Devolução Venda #{sale.Number} — {DescribeMethod(refundMethod)}", DateTime.UtcNow,
                    SaleReturnReference, saleReturn.Id, ct);
                counter.MarkPaid(DateTime.UtcNow);
                await _financial.AddTransactionAsync(counter, ct);
            }

            // Stock: only what this sale took out comes back
            var deducted = (await _stock.GetMovementsByReferenceAsync("Sale", sale.Id, ct))
                .Where(m => m.MovementType == StockMovementType.SaleOutput)
                .Select(m => m.ProductId)
                .ToHashSet();
            foreach (var (item, quantity) in lines.Where(l => deducted.Contains(l.Item.ProductId)))
                await ReEnterStockAsync(item.ProductId, quantity, SaleReturnReference, saleReturn.Id, ct);

            sale.RegisterReturn(refund, fullyReturned);
            _audit.Stage(AuditActions.SaleReturned, AuditSeverity.Warning, "Sale", sale.Id.ToString(),
                $"Devolução de {refund:F2} na venda #{sale.Number} ({DescribeMethod(refundMethod)}): {saleReturn.Reason}",
                tenantId: _currentTenant.Id, actorId: _currentUser.UserId, actorName: _currentUser.Name,
                metadata: new { SaleReturnId = saleReturn.Id, refund, RefundMethod = refundMethod.ToString(), fullyReturned });

            await _sales.SaveChangesAsync(ct);
            await _stock.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return MapReturn(saleReturn);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new DomainException("O estoque mudou durante a devolução. Tente novamente.");
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    public async Task<IReadOnlyList<SaleReturnDto>> GetReturnsAsync(Guid saleId, CancellationToken ct = default)
    {
        _ = await _sales.GetByIdAsync(saleId, ct) ?? throw new NotFoundException("Sale", saleId);
        return (await _sales.GetReturnsBySaleAsync(saleId, ct)).Select(MapReturn).ToList();
    }

    private async Task ReEnterStockAsync(Guid productId, decimal quantity, string referenceType, Guid referenceId, CancellationToken ct)
    {
        var stockItem = await _stock.GetByProductIdAsync(productId, ct);
        if (stockItem is null) return;

        var before = stockItem.CurrentQuantity;
        stockItem.ApplyMovement(quantity);
        await _stock.AddMovementAsync(StockMovement.Create(
            _currentTenant.Id, productId, StockMovementType.ReturnEntry, quantity,
            before, stockItem.CurrentQuantity, _currentUser.UserId,
            referenceType: referenceType, referenceId: referenceId), ct);
    }

    private static SaleReturnDto MapReturn(SaleReturn r) => new(
        r.Id, r.SaleId, r.RefundMethod.ToString(), r.RefundAmount, r.Reason, r.CreatedByUserId, r.CreatedAt,
        r.Items.Select(i => new SaleReturnItemDto(i.SaleItemId, i.ProductId, i.Quantity, i.Amount)).ToList());

    // ── Payment helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// The open drawer the cash of this sale lands in: the session the sale was opened against (it
    /// must still be open), else the seller's own open session, else the store's open session —
    /// the PDV and the salão sell on the drawer the management opened. The chosen session is
    /// row-locked and re-checked, so cash never lands in a drawer that is closing concurrently.
    /// </summary>
    private async Task<Guid> ResolveOpenCashSessionAsync(Sale sale, CancellationToken ct)
    {
        if (sale.CashSessionId.HasValue)
        {
            var linked = await LockIfOpenAsync(sale.CashSessionId.Value, ct);
            return linked?.Id ?? throw new DomainException(
                "O caixa desta venda foi fechado. Abra um caixa e refaça a venda para receber em dinheiro.");
        }

        foreach (var candidate in new[]
                 {
                     await _cash.GetOpenSessionByUserAsync(_currentUser.UserId, ct),
                     await _cash.GetOpenSessionAsync(ct),
                 })
        {
            if (candidate is null) continue;
            var locked = await LockIfOpenAsync(candidate.Id, ct);
            if (locked is not null) return locked.Id;
        }

        throw new DomainException("Nenhum caixa aberto. Abra o caixa para receber pagamentos em dinheiro.");
    }

    /// <summary>
    /// The open drawer the refund of a cancelled sale leaves from — the original if still open,
    /// else the current one (a closed day stays closed) — locked, and holding enough money.
    /// </summary>
    private async Task<Guid> ResolveRefundCashSessionAsync(Sale sale, decimal refund, CancellationToken ct)
    {
        var candidates = new List<Guid>();
        if (sale.CashSessionId.HasValue) candidates.Add(sale.CashSessionId.Value);
        if (await _cash.GetOpenSessionByUserAsync(_currentUser.UserId, ct) is { } mine) candidates.Add(mine.Id);
        if (await _cash.GetOpenSessionAsync(ct) is { } store) candidates.Add(store.Id);

        foreach (var id in candidates.Distinct())
        {
            var session = await LockIfOpenAsync(id, ct);
            if (session is null) continue;

            var available = CashSession.ExpectedBalanceFrom(
                session.OpeningBalance, await _cash.GetMovementsBySessionAsync(session.Id, ct));
            if (refund > available)
                throw new DomainException(
                    $"O caixa não tem dinheiro suficiente para devolver {refund:F2} (disponível {available:F2}).");
            return session.Id;
        }

        throw new DomainException(
            "Nenhum caixa aberto para devolver o dinheiro desta venda. Abra o caixa e tente de novo.");
    }

    private async Task<CashSession?> LockIfOpenAsync(Guid sessionId, CancellationToken ct)
    {
        await _cash.LockSessionAsync(sessionId, _currentTenant.Id, ct);
        var session = await _cash.GetByIdAsync(sessionId, ct);
        return session is { IsOpen: true } ? session : null;
    }

    private async Task<FinancialTransaction> NewSaleTransactionAsync(
        FinancialAccountType accountType, TransactionType type, decimal amount, string description,
        DateTime dueDate, string referenceType, Guid saleId, CancellationToken ct)
    {
        var account = await _financial.GetDefaultByTypeAsync(accountType, ct)
            ?? throw new DomainException(accountType == FinancialAccountType.Receivable
                ? "No active 'Contas a Receber' account found for this tenant. Please set up a default Receivable account in financial settings."
                : "No active 'Contas a Pagar' account found for this tenant. Please set up a default Payable account in financial settings.");

        return FinancialTransaction.Create(
            _currentTenant.Id, account.Id, type, amount, description, dueDate, _currentUser.UserId,
            referenceType: referenceType, referenceId: saleId);
    }

    /// <summary>
    /// A retry of a confirmation that already went through (network timeout, double click): same
    /// number of lines and the same amounts handed over. Anything else is a real conflict.
    /// </summary>
    private static bool IsReplayOf(Sale sale, ConfirmSaleRequest request)
    {
        if (sale.Status is not (SaleStatus.Confirmed or SaleStatus.Paid)) return false;
        var recorded = sale.Payments
            .Select(p => $"{p.Method}|{p.Type}|{p.Amount + p.ChangeAmount:F2}")
            .OrderBy(x => x, StringComparer.Ordinal).ToList();
        var requested = request.Payments
            .Select(p => Enum.TryParse<PaymentMethod>(p.Method, true, out var m)
                         && Enum.TryParse<PaymentType>(p.Type, true, out var t)
                ? $"{m}|{t}|{p.Amount:F2}" : "?")
            .OrderBy(x => x, StringComparer.Ordinal).ToList();
        return recorded.Count > 0 && recorded.SequenceEqual(requested);
    }

    private static string DescribeStatus(SaleStatus status) => status switch
    {
        SaleStatus.Cancelled => "cancelada",
        SaleStatus.Paid      => "paga",
        _                    => "confirmada",
    };

    private static string DescribeMethod(PaymentMethod method) => method switch
    {
        PaymentMethod.Cash     => "Dinheiro",
        PaymentMethod.Pix      => "Pix",
        PaymentMethod.Debit    => "Cartão de débito",
        PaymentMethod.Credit   => "Cartão de crédito",
        PaymentMethod.Transfer => "Transferência",
        PaymentMethod.Check    => "Cheque",
        _                      => "Outro",
    };

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
            p.DueDate,
            p.TenderedAmount,
            p.ChangeAmount)).ToList(),
        s.CreatedAt,
        s.UpdatedAt,
        s.ReturnedAmount,
        s.CancellationReason,
        s.SurchargesAmount);
}
