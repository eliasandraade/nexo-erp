using Microsoft.Extensions.Logging;
using Nexo.Application.Common;
using Nexo.Application.Common.Interfaces;
using Nexo.Application.Modules.Service;
using Nexo.Application.Modules.Varejo;
using Nexo.Domain.Entities;
using Nexo.Domain.Enums;
using Nexo.Domain.Exceptions;

namespace Nexo.Application.Features.Financial;

public class FinancialService
{
    private readonly IFinancialRepository _financial;
    private readonly IStoreRepository _stores;
    private readonly ICustomerRepository _customers;
    private readonly ISupplierRepository _suppliers;
    private readonly FinancialRecurrenceService _recurrences;
    private readonly IUnitOfWork _uow;
    private readonly ICurrentTenant _currentTenant;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<FinancialService> _logger;

    /// <summary>Most entries a ledger response carries; totals always cover the whole period.</summary>
    public const int LedgerEntryLimit = 1000;

    public FinancialService(
        IFinancialRepository financial, IStoreRepository stores, ICustomerRepository customers,
        ISupplierRepository suppliers, FinancialRecurrenceService recurrences, IUnitOfWork uow,
        ICurrentTenant currentTenant, ICurrentUser currentUser, ILogger<FinancialService> logger)
    {
        _logger        = logger;
        _financial     = financial;
        _stores        = stores;
        _customers     = customers;
        _suppliers     = suppliers;
        _recurrences   = recurrences;
        _uow           = uow;
        _currentTenant = currentTenant;
        _currentUser   = currentUser;
    }

    /// <summary>
    /// ReferenceTypes written by the modules themselves (sales, purchases, Service). Their lançamentos
    /// mirror a record of origin and are corrected there — cancel the sale, cancel the purchase, void
    /// the payment — never edited or cancelled here, or the two sides silently disagree.
    /// </summary>
    public static bool IsSystemOwned(string? referenceType)
        => referenceType is not null
           && (ServiceFinancialPostingService.IsServiceManaged(referenceType)
               || referenceType == FinancialTransaction.SettlementReference
               || referenceType.StartsWith(VerticalLedgerPostingService.RestExpenseReference, StringComparison.Ordinal)
               || referenceType.StartsWith(VerticalLedgerPostingService.MovementReference, StringComparison.Ordinal)
               || referenceType.StartsWith("Sale", StringComparison.OrdinalIgnoreCase)
               || referenceType.StartsWith("Purchase", StringComparison.OrdinalIgnoreCase));

    // ── Accounts ─────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<FinancialAccountDto>> GetAllAccountsAsync(bool includeInactive = false, CancellationToken ct = default)
    {
        var list = await _financial.GetAllAccountsAsync(includeInactive, ct);
        return list.Select(MapAccountToDto).ToList();
    }

    public async Task<FinancialAccountDto> GetAccountByIdAsync(Guid id, CancellationToken ct = default)
    {
        var account = await _financial.GetAccountByIdAsync(id, ct)
            ?? throw new NotFoundException("FinancialAccount", id);
        return MapAccountToDto(account);
    }

    public async Task<FinancialAccountDto> CreateAccountAsync(CreateFinancialAccountRequest request, CancellationToken ct = default)
    {
        if (await _financial.AccountCodeExistsAsync(request.Code, ct: ct))
            throw new ConflictException($"Account code '{request.Code}' is already in use.");

        var accountType = Enum.Parse<FinancialAccountType>(request.AccountType, ignoreCase: true);

        var account = FinancialAccount.Create(
            _currentTenant.Id,
            request.Code,
            request.Name,
            accountType,
            request.ParentAccountId);

        await _financial.AddAccountAsync(account, ct);
        await _financial.SaveChangesAsync(ct);
        return MapAccountToDto(account);
    }

    public async Task<FinancialAccountDto> UpdateAccountAsync(Guid id, UpdateFinancialAccountRequest request, CancellationToken ct = default)
    {
        var account = await _financial.GetAccountByIdAsync(id, ct)
            ?? throw new NotFoundException("FinancialAccount", id);

        if (await _financial.AccountCodeExistsAsync(request.Code, excludeId: id, ct: ct))
            throw new ConflictException($"Account code '{request.Code}' is already in use.");

        account.Update(request.Code, request.Name, request.ParentAccountId);
        await _financial.SaveChangesAsync(ct);
        return MapAccountToDto(account);
    }

    public async Task DeactivateAccountAsync(Guid id, CancellationToken ct = default)
    {
        var account = await _financial.GetAccountByIdAsync(id, ct)
            ?? throw new NotFoundException("FinancialAccount", id);

        // Sales and purchases post to the active Contas a Receber / a Pagar: never leave a tenant without one.
        if (account.IsActive && account.AccountType is FinancialAccountType.Receivable or FinancialAccountType.Payable
            && (await _financial.GetAllAccountsAsync(includeInactive: false, ct))
                .Count(a => a.AccountType == account.AccountType) <= 1)
            throw new DomainException("Esta é a única conta ativa do tipo: vendas e compras dependem dela. Ative outra antes.");

        account.Deactivate();
        await _financial.SaveChangesAsync(ct);
    }

    public async Task ActivateAccountAsync(Guid id, CancellationToken ct = default)
    {
        var account = await _financial.GetAccountByIdAsync(id, ct)
            ?? throw new NotFoundException("FinancialAccount", id);
        account.Activate();
        await _financial.SaveChangesAsync(ct);
    }

    // ── Transactions ─────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<FinancialTransactionDto>> GetTransactionsByAccountAsync(Guid accountId, CancellationToken ct = default)
    {
        _ = await _financial.GetAccountByIdAsync(accountId, ct)
            ?? throw new NotFoundException("FinancialAccount", accountId);

        var list = await _financial.GetTransactionsByAccountAsync(accountId, ct);
        return list.Select(MapTransactionToDto).ToList();
    }

    public async Task<IReadOnlyList<FinancialTransactionDto>> GetPendingTransactionsAsync(CancellationToken ct = default)
    {
        var list = await _financial.GetPendingTransactionsAsync(ct);
        return list.Select(MapTransactionToDto).ToList();
    }

    public async Task<FinancialTransactionDto> GetTransactionByIdAsync(Guid id, CancellationToken ct = default)
    {
        var tx = await _financial.GetTransactionByIdAsync(id, ct)
            ?? throw new NotFoundException("FinancialTransaction", id);
        return MapTransactionToDto(tx);
    }

    public async Task<FinancialTransactionDto> CreateTransactionAsync(CreateTransactionRequest request, CancellationToken ct = default)
    {
        _ = await _financial.GetAccountByIdAsync(request.FinancialAccountId, ct)
            ?? throw new NotFoundException("FinancialAccount", request.FinancialAccountId);

        if (IsSystemOwned(request.ReferenceType) || request.ReferenceType == FinancialRecurrenceService.RecurrenceReference)
            throw new DomainException("Esta referência é reservada aos lançamentos gerados pelos módulos (vendas, compras, Service).");

        if (!Enum.TryParse<TransactionType>(request.TransactionType, ignoreCase: true, out var transactionType)
            || !Enum.IsDefined(transactionType))
            throw new DomainException("Tipo de lançamento inválido: use Receivable ou Payable.");

        var category = FinancialCategoryRules.DefaultFor(transactionType);
        if (request.Category is not null
            && (!Enum.TryParse(request.Category, ignoreCase: true, out category) || !Enum.IsDefined(category)))
            throw new DomainException("Categoria inválida.");
        if (!category.IsAllowedForManualEntry(transactionType))
            throw new DomainException("Esta categoria não combina com o tipo do lançamento.");

        if (request.StoreId is { } storeId)
            _ = await _stores.GetByIdAsync(storeId, ct) ?? throw new NotFoundException("Store", storeId);
        if (request.CustomerId is { } customerId)
            _ = await _customers.GetByIdAsync(customerId, ct) ?? throw new NotFoundException("Customer", customerId);
        if (request.SupplierId is { } supplierId)
            _ = await _suppliers.GetByIdAsync(supplierId, ct) ?? throw new NotFoundException("Supplier", supplierId);

        var tx = FinancialTransaction.Create(
            _currentTenant.Id,
            request.FinancialAccountId,
            transactionType,
            category,
            request.Amount,
            request.Description,
            Utc(request.DueDate),
            _currentUser.UserId,
            request.ReferenceType,
            request.ReferenceId,
            request.StoreId,
            Utc(request.CompetenceDate ?? request.DueDate),
            request.CustomerId,
            request.SupplierId);

        await _financial.AddTransactionAsync(tx, ct);
        await _financial.SaveChangesAsync(ct);
        return MapTransactionToDto(tx);
    }

    public Task<FinancialTransactionDto> UpdateTransactionAsync(Guid id, UpdateTransactionRequest request, CancellationToken ct = default)
        => WithLockedTransactionAsync(id, tx =>
        {
            // A supplier's open bill can be renegotiated; other module entries are fixed at the origin.
            if (tx.ReferenceType != PurchaseService.PurchaseReference) EnsureNotSystemOwned(tx);
            tx.Update(request.Amount, request.Description, Utc(request.DueDate), BusinessClock.TodayStartUtc());   // open only (domain)
        }, ct);

    public Task<FinancialTransactionDto> MarkPaidAsync(Guid id, MarkTransactionPaidRequest request, CancellationToken ct = default)
        => WithLockedTransactionAsync(id, tx =>
        {
            EnsureNotServiceManaged(tx);
            tx.MarkPaid(request.PaidAt is { } paidAt ? Utc(paidAt) : null);
        }, ct);

    public Task<FinancialTransactionDto> CancelTransactionAsync(Guid id, CancellationToken ct = default)
        => WithLockedTransactionAsync(id, tx =>
        {
            EnsureNotSystemOwned(tx);
            tx.Cancel();   // open entries only — a settled one is reversed, not cancelled (domain)
        }, ct);

    /// <summary>
    /// Pay, cancel and edit run under a row lock, as does cancelling the sale or purchase behind the
    /// entry: a concurrent pay × cancel can never leave money received on a cancelled título.
    /// </summary>
    private async Task<FinancialTransactionDto> WithLockedTransactionAsync(
        Guid id, Action<FinancialTransaction> change, CancellationToken ct)
    {
        await using var scope = await _uow.BeginTransactionAsync(ct);
        await _financial.LockTransactionAsync(id, _currentTenant.Id, ct);
        var tx = await _financial.GetTransactionByIdAsync(id, ct)
            ?? throw new NotFoundException("FinancialTransaction", id);
        change(tx);
        await _financial.SaveChangesAsync(ct);
        await scope.CommitAsync(ct);
        return MapTransactionToDto(tx);
    }

    /// <summary>
    /// timestamptz takes UTC. A date without time ("2026-10-20") is a business day: noon in Brazil, so it
    /// never lands on the previous day; any other zone-less value is read as UTC.
    /// </summary>
    private static DateTime Utc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc   => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ when value.TimeOfDay == TimeSpan.Zero => BusinessClock.NoonUtc(DateOnly.FromDateTime(value)),
        _                  => DateTime.SpecifyKind(value, DateTimeKind.Utc),
    };

    // ── Baixa ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Baixa total ou parcial, under the título's row lock. Money received/paid = Amount; juros + multa
    /// become a financial income (receivable) / expense (payable) lançamento; a desconto settles part of
    /// the título without money and is the opposite financial lançamento. Service títulos are settled
    /// at the origin.
    /// </summary>
    public async Task<SettlementResultDto> SettleAsync(Guid id, SettleTransactionRequest request, CancellationToken ct = default)
    {
        if (request.Amount < 0 || request.Interest < 0 || request.Fine < 0 || request.Discount < 0)
            throw new DomainException("Valores da baixa não podem ser negativos.");
        foreach (var v in new[] { request.Amount, request.Interest, request.Fine, request.Discount })
            if (decimal.Round(v, 2) != v)
                throw new DomainException("Valores da baixa vão até centavos.");
        var paidAt = request.PaidAt is { } p ? Utc(p) : DateTime.UtcNow;
        if (paidAt > DateTime.UtcNow.AddDays(1))
            throw new DomainException("A data da baixa não pode estar no futuro.");

        await using var scope = await _uow.BeginTransactionAsync(ct);
        await _financial.LockTransactionAsync(id, _currentTenant.Id, ct);
        var tx = await _financial.GetTransactionByIdAsync(id, ct)
            ?? throw new NotFoundException("FinancialTransaction", id);
        EnsureNotServiceManaged(tx);

        var (remainder, abated) = tx.Settle(request.Amount, request.Discount, paidAt);
        if (remainder is not null) await _financial.AddTransactionAsync(remainder, ct);
        if (abated is not null) await _financial.AddTransactionAsync(abated, ct);

        var isReceivable = tx.TransactionType == TransactionType.Receivable;
        var adjustments = new List<FinancialTransaction>();
        var charges = request.Interest + request.Fine;
        if (charges > 0)
            adjustments.Add(tx.CreateSettlementAdjustment(
                await DefaultAccountIdAsync(tx.TransactionType, ct), tx.TransactionType, charges,
                $"Juros/multa — {tx.Description}", paidAt, _currentUser.UserId, moneyMoved: true));
        if (request.Discount > 0)
        {
            var opposite = isReceivable ? TransactionType.Payable : TransactionType.Receivable;
            adjustments.Add(tx.CreateSettlementAdjustment(
                await DefaultAccountIdAsync(opposite, ct), opposite, request.Discount,
                $"Desconto {(isReceivable ? "concedido" : "obtido")} — {tx.Description}", paidAt, _currentUser.UserId,
                moneyMoved: false));
        }
        foreach (var a in adjustments) await _financial.AddTransactionAsync(a, ct);

        await _financial.SaveChangesAsync(ct);
        await scope.CommitAsync(ct);
        return new SettlementResultDto(
            MapTransactionToDto(tx),
            remainder is null ? null : MapTransactionToDto(remainder),
            adjustments.Select(MapTransactionToDto).ToList());
    }

    // ── Títulos ──────────────────────────────────────────────────────────────

    /// <summary>Most títulos a listing carries; the aging always covers every open título.</summary>
    public const int TitleListLimit = 500;

    public async Task<TitlesDto> GetTitlesAsync(TitlesFilter filter, CancellationToken ct = default)
    {
        await EnsureRecurrencesAsync(ct);   // recurring títulos of this and next month exist
        var today = BusinessClock.TodayStartUtc();
        var titles = await _financial.GetTitlesAsync(filter, today, TitleListLimit + 1, ct);
        var page = titles.Take(TitleListLimit).ToList();
        var names = await _financial.GetCustomerNamesAsync(
            page.Where(t => t.CustomerId.HasValue).Select(t => t.CustomerId!.Value), ct);
        var supplierNames = await _financial.GetSupplierNamesAsync(
            page.Where(t => t.SupplierId.HasValue).Select(t => t.SupplierId!.Value), ct);

        var open = await _financial.GetOpenTitleRowsAsync(filter.Type, filter.CustomerId, filter.SupplierId, ct);
        decimal Bucket(int min, int max) => open
            .Where(r => r.DueDate < today && BusinessClock.DaysOverdue(r.DueDate) is var d && d >= min && d <= max)
            .Sum(r => r.Amount);
        var aging = new AgingDto(
            NotDue:        open.Where(r => r.DueDate >= today).Sum(r => r.Amount),
            Overdue1To30:  Bucket(1, 30),
            Overdue31To60: Bucket(31, 60),
            OverdueOver60: Bucket(61, int.MaxValue),
            TotalOpen:     open.Sum(r => r.Amount),
            OpenCount:     open.Count,
            OverdueCount:  open.Count(r => r.DueDate < today));

        return new TitlesDto(page.Select(t => MapTitle(t, names, supplierNames, today)).ToList(), aging, titles.Count > TitleListLimit);
    }

    private static TitleDto MapTitle(
        FinancialTransaction t, IReadOnlyDictionary<Guid, string> names, IReadOnlyDictionary<Guid, string> supplierNames, DateTime today)
    {
        var late = t.IsOpen && t.DueDate < today;
        return new TitleDto(
            t.Id, t.Description, t.CustomerId,
            t.CustomerId is { } c && names.TryGetValue(c, out var n) ? n : null,
            t.SupplierId,
            t.SupplierId is { } s && supplierNames.TryGetValue(s, out var sn) ? sn : null,
            t.Amount, t.DueDate, t.CompetenceDate, t.PaidAt,
            late ? TransactionStatus.Overdue.ToString() : t.Status.ToString(),
            late ? BusinessClock.DaysOverdue(t.DueDate) : 0,
            t.Category.ToString(), t.StoreId, t.ReferenceType, t.ReferenceId, t.ParentTransactionId);
    }

    /// <summary>Generation never blocks a read: a failure is logged and the read shows what exists.</summary>
    private async Task EnsureRecurrencesAsync(CancellationToken ct)
    {
        try { await _recurrences.EnsureGeneratedAsync(ct); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Recurring títulos could not be generated for tenant {TenantId}", _currentTenant.Id);
        }
    }

    // ── Parcelamento ─────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a manual título split in monthly installments: the cents are divided evenly and the last
    /// installment takes the rest, so the parcelas always add up to the total.
    /// </summary>
    public async Task<IReadOnlyList<FinancialTransactionDto>> CreateInstallmentsAsync(
        CreateInstallmentsRequest request, CancellationToken ct = default)
    {
        if (!Enum.TryParse<TransactionType>(request.TransactionType, ignoreCase: true, out var type) || !Enum.IsDefined(type))
            throw new DomainException("Tipo de lançamento inválido: use Receivable ou Payable.");
        if (request.Installments is < 1 or > 60)
            throw new DomainException("Parcele em 1 a 60 vezes.");
        if (decimal.Round(request.TotalAmount, 2) != request.TotalAmount || request.TotalAmount < 0.01m * request.Installments)
            throw new DomainException("O total precisa estar em centavos e cobrir ao menos R$ 0,01 por parcela.");
        if (string.IsNullOrWhiteSpace(request.Description))
            throw new DomainException("O lançamento precisa de uma descrição.");

        var category = FinancialCategoryRules.DefaultFor(type);
        if (request.Category is not null
            && (!Enum.TryParse(request.Category, ignoreCase: true, out category) || !Enum.IsDefined(category)))
            throw new DomainException("Categoria inválida.");
        if (!category.IsAllowedForManualEntry(type))
            throw new DomainException("Esta categoria não combina com o tipo do lançamento.");
        if (request.CustomerId is { } customerId)
            _ = await _customers.GetByIdAsync(customerId, ct) ?? throw new NotFoundException("Customer", customerId);
        if (request.SupplierId is { } supplierId)
            _ = await _suppliers.GetByIdAsync(supplierId, ct) ?? throw new NotFoundException("Supplier", supplierId);
        if (request.StoreId is { } storeId)
            _ = await _stores.GetByIdAsync(storeId, ct) ?? throw new NotFoundException("Store", storeId);

        Guid accountId;
        if (request.FinancialAccountId is { } given)
        {
            var account = await _financial.GetAccountByIdAsync(given, ct) ?? throw new NotFoundException("FinancialAccount", given);
            var expected = type == TransactionType.Receivable ? FinancialAccountType.Receivable : FinancialAccountType.Payable;
            if (!account.IsActive || account.AccountType != expected)
                throw new DomainException("A conta precisa estar ativa e ser do mesmo tipo do lançamento.");
            accountId = account.Id;
        }
        else
            accountId = await DefaultAccountIdAsync(type, ct);

        var cents = (long)(request.TotalAmount * 100);
        var each = cents / request.Installments;
        var first = Utc(request.FirstDueDate);
        var created = new List<FinancialTransaction>();
        for (var i = 0; i < request.Installments; i++)
        {
            var amount = (i == request.Installments - 1 ? cents - each * (request.Installments - 1) : each) / 100m;
            var due = first.AddMonths(i);
            var description = request.Installments == 1
                ? request.Description.Trim()
                : $"{request.Description.Trim()} ({i + 1}/{request.Installments})";
            var tx = FinancialTransaction.Create(
                _currentTenant.Id, accountId, type, category, amount, description, due, _currentUser.UserId,
                storeId: request.StoreId, competenceDate: request.CompetenceDate is { } c ? Utc(c) : due,
                customerId: request.CustomerId, supplierId: request.SupplierId);
            await _financial.AddTransactionAsync(tx, ct);
            created.Add(tx);
        }
        await _financial.SaveChangesAsync(ct);
        return created.Select(MapTransactionToDto).ToList();
    }

    private async Task<Guid> DefaultAccountIdAsync(TransactionType type, CancellationToken ct)
        => (await _financial.GetDefaultByTypeAsync(
                type == TransactionType.Receivable ? FinancialAccountType.Receivable : FinancialAccountType.Payable, ct)
            ?? throw new DomainException(type == TransactionType.Receivable
                ? "Nenhuma conta 'Contas a Receber' ativa."
                : "Nenhuma conta 'Contas a Pagar' ativa.")).Id;

    // ── Ledger ───────────────────────────────────────────────────────────────

    public async Task<LedgerDto> GetLedgerAsync(LedgerFilter filter, CancellationToken ct = default)
    {
        if (filter.To <= filter.From)
            throw new DomainException("O fim do período precisa ser depois do início.");

        await EnsureRecurrencesAsync(ct);
        var totals  = await _financial.GetLedgerTotalsAsync(filter, ct);
        var entries = await _financial.GetLedgerEntriesAsync(filter, LedgerEntryLimit + 1, ct);

        var byCategory = totals
            .GroupBy(t => t.Category)
            .Select(g =>
            {
                var inflow  = g.Where(t => t.Type == TransactionType.Receivable).Sum(t => t.Amount);
                var outflow = g.Where(t => t.Type == TransactionType.Payable).Sum(t => t.Amount);
                return new LedgerCategoryTotalDto(g.Key.ToString(), inflow, outflow, inflow - outflow, g.Sum(t => t.Count));
            })
            .OrderBy(c => Enum.Parse<FinancialCategory>(c.Category))
            .ToList();

        var totalIn  = byCategory.Sum(c => c.In);
        var totalOut = byCategory.Sum(c => c.Out);
        // Competência: buying stock is not an expense (its cost reaches the result through the CMV
        // of what is sold), so the result leaves compras out. Caixa: every real outflow counts.
        var result = filter.Regime == LedgerRegime.Accrual
            ? byCategory.Where(c => c.Category != nameof(FinancialCategory.Purchase)).Sum(c => c.Net)
            : totalIn - totalOut;
        return new LedgerDto(
            filter.Regime.ToString(), filter.From, filter.To, totalIn, totalOut, totalIn - totalOut, result, byCategory,
            entries.Take(LedgerEntryLimit).Select(MapLedgerEntry).ToList(),
            Truncated: entries.Count > LedgerEntryLimit);
    }

    private static LedgerEntryDto MapLedgerEntry(FinancialTransaction t) => new(
        t.Id,
        t.TransactionType == TransactionType.Receivable ? "In" : "Out",
        t.Category.ToString(),
        t.Amount,
        t.Description,
        t.CompetenceDate,
        t.DueDate,
        t.PaidAt,
        t.Status.ToString(),
        t.StoreId,
        t.ReferenceType,
        t.ReferenceId,
        t.ReversesTransactionId);

    /// <summary>Module lançamentos are corrected at the origin, never edited or cancelled here.</summary>
    private static void EnsureNotSystemOwned(FinancialTransaction tx)
    {
        if (IsSystemOwned(tx.ReferenceType))
            throw new DomainException(
                "Este lançamento foi gerado por um módulo e só pode ser corrigido na origem (cancele a venda, a compra ou o pagamento).");
    }

    /// <summary>Service lançamentos are corrected at the origin (void the payment), never here.</summary>
    private static void EnsureNotServiceManaged(FinancialTransaction tx)
    {
        if (VerticalLedgerPostingService.IsSettledAtOrigin(tx.ReferenceType))
            throw new DomainException(tx.ReferenceType == VerticalLedgerPostingService.RestExpenseReference
                ? "Esta despesa do restaurante é paga na própria despesa (informe a data de pagamento lá)."
                : "Esta movimentação já está quitada na origem (Build).");
        if (ServiceFinancialPostingService.IsServiceManaged(tx.ReferenceType))
            throw new DomainException(
                "This entry was generated by Orken Service and can only be changed there (e.g. void the payment).");
    }

    private static FinancialAccountDto MapAccountToDto(FinancialAccount a) => new(
        a.Id, a.Code, a.Name, a.AccountType.ToString(), a.ParentAccountId, a.IsActive, a.CreatedAt, a.UpdatedAt,
        a.OpeningBalance, a.OpeningBalanceDate);

    public async Task<FinancialAccountDto> SetOpeningBalanceAsync(Guid id, SetOpeningBalanceRequest request, CancellationToken ct = default)
    {
        var account = await _financial.GetAccountByIdAsync(id, ct) ?? throw new NotFoundException("FinancialAccount", id);
        if (!account.IsActive)
            throw new DomainException("Ative a conta antes de informar o saldo inicial.");
        if (request.Date > DateOnly.FromDateTime(BusinessClock.ToBrazil(DateTime.UtcNow)))
            throw new DomainException("O saldo inicial é de um dia que já passou ou de hoje.");

        // One cut-off date for the company: payments aren't booked to Caixa or Banco, so balances taken
        // on different days would count the money in between twice.
        var other = (await _financial.GetAllAccountsAsync(includeInactive: true, ct))
            .FirstOrDefault(a => a.Id != id && a.OpeningBalanceDate is { } d && d != request.Date);
        if (other is not null)
            throw new DomainException(
                $"Use a mesma data de corte das outras contas ({other.OpeningBalanceDate:dd/MM/yyyy}, em {other.Name}).");

        account.SetOpeningBalance(request.Amount, request.Date);
        await _financial.SaveChangesAsync(ct);
        return MapAccountToDto(account);
    }

    public async Task<FinancialAccountDto> ClearOpeningBalanceAsync(Guid id, CancellationToken ct = default)
    {
        var account = await _financial.GetAccountByIdAsync(id, ct) ?? throw new NotFoundException("FinancialAccount", id);
        account.ClearOpeningBalance();
        await _financial.SaveChangesAsync(ct);
        return MapAccountToDto(account);
    }

    private static FinancialTransactionDto MapTransactionToDto(FinancialTransaction t) => new(
        t.Id,
        t.FinancialAccountId,
        t.FinancialAccount?.Name ?? string.Empty,
        t.TransactionType.ToString(),
        t.Amount,
        t.Description,
        t.DueDate,
        t.PaidAt,
        t.Status.ToString(),
        t.ReferenceType,
        t.ReferenceId,
        t.CreatedByUserId,
        t.CreatedAt,
        t.UpdatedAt,
        t.Category.ToString(),
        t.CompetenceDate,
        t.StoreId,
        t.ReversesTransactionId,
        t.CustomerId,
        t.ParentTransactionId,
        t.SupplierId);
}
