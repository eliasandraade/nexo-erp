using Nexo.Application.Common.Interfaces;
using Nexo.Application.Modules.Service;
using Nexo.Domain.Entities;
using Nexo.Domain.Enums;
using Nexo.Domain.Exceptions;

namespace Nexo.Application.Features.Financial;

public class FinancialService
{
    private readonly IFinancialRepository _financial;
    private readonly IStoreRepository _stores;
    private readonly ICurrentTenant _currentTenant;
    private readonly ICurrentUser _currentUser;

    /// <summary>Most entries a ledger response carries; totals always cover the whole period.</summary>
    public const int LedgerEntryLimit = 1000;

    public FinancialService(
        IFinancialRepository financial, IStoreRepository stores, ICurrentTenant currentTenant, ICurrentUser currentUser)
    {
        _financial     = financial;
        _stores        = stores;
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

        if (IsSystemOwned(request.ReferenceType))
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

        var tx = FinancialTransaction.Create(
            _currentTenant.Id,
            request.FinancialAccountId,
            transactionType,
            category,
            request.Amount,
            request.Description,
            request.DueDate,
            _currentUser.UserId,
            request.ReferenceType,
            request.ReferenceId,
            request.StoreId,
            request.CompetenceDate ?? request.DueDate);

        await _financial.AddTransactionAsync(tx, ct);
        await _financial.SaveChangesAsync(ct);
        return MapTransactionToDto(tx);
    }

    public async Task<FinancialTransactionDto> UpdateTransactionAsync(Guid id, UpdateTransactionRequest request, CancellationToken ct = default)
    {
        var tx = await _financial.GetTransactionByIdAsync(id, ct)
            ?? throw new NotFoundException("FinancialTransaction", id);

        EnsureNotSystemOwned(tx);

        tx.Update(request.Amount, request.Description, request.DueDate);   // open entries only (domain)
        await _financial.SaveChangesAsync(ct);
        return MapTransactionToDto(tx);
    }

    public async Task<FinancialTransactionDto> MarkPaidAsync(Guid id, MarkTransactionPaidRequest request, CancellationToken ct = default)
    {
        var tx = await _financial.GetTransactionByIdAsync(id, ct)
            ?? throw new NotFoundException("FinancialTransaction", id);
        EnsureNotServiceManaged(tx);

        tx.MarkPaid(request.PaidAt);
        await _financial.SaveChangesAsync(ct);
        return MapTransactionToDto(tx);
    }

    public async Task<FinancialTransactionDto> CancelTransactionAsync(Guid id, CancellationToken ct = default)
    {
        var tx = await _financial.GetTransactionByIdAsync(id, ct)
            ?? throw new NotFoundException("FinancialTransaction", id);

        EnsureNotSystemOwned(tx);

        tx.Cancel();   // open entries only — a settled one is reversed, not cancelled (domain)
        await _financial.SaveChangesAsync(ct);
        return MapTransactionToDto(tx);
    }

    // ── Ledger ───────────────────────────────────────────────────────────────

    public async Task<LedgerDto> GetLedgerAsync(LedgerFilter filter, CancellationToken ct = default)
    {
        if (filter.To <= filter.From)
            throw new DomainException("O fim do período precisa ser depois do início.");

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
        return new LedgerDto(
            filter.Regime.ToString(), filter.From, filter.To, totalIn, totalOut, totalIn - totalOut, byCategory,
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
        if (ServiceFinancialPostingService.IsServiceManaged(tx.ReferenceType))
            throw new DomainException(
                "This entry was generated by Orken Service and can only be changed there (e.g. void the payment).");
    }

    private static FinancialAccountDto MapAccountToDto(FinancialAccount a) => new(
        a.Id, a.Code, a.Name, a.AccountType.ToString(), a.ParentAccountId, a.IsActive, a.CreatedAt, a.UpdatedAt);

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
        t.ReversesTransactionId);
}
