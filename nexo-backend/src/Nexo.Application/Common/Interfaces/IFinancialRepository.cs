using Nexo.Domain.Enums;
using Nexo.Domain.Entities;

namespace Nexo.Application.Common.Interfaces;

public interface IFinancialRepository
{
    // Accounts
    Task<FinancialAccount?> GetAccountByIdAsync(Guid id, CancellationToken ct = default);
    /// <summary>Returns the tenant's default account for the given type (e.g. Receivable for credit sales).</summary>
    Task<FinancialAccount?> GetDefaultByTypeAsync(Nexo.Domain.Enums.FinancialAccountType type, CancellationToken ct = default);
    Task<IReadOnlyList<FinancialAccount>> GetAllAccountsAsync(bool includeInactive = false, CancellationToken ct = default);
    Task<bool> AccountCodeExistsAsync(string code, Guid? excludeId = null, CancellationToken ct = default);
    Task AddAccountAsync(FinancialAccount account, CancellationToken ct = default);

    // Transactions
    Task<FinancialTransaction?> GetTransactionByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<FinancialTransaction>> GetTransactionsByAccountAsync(Guid accountId, CancellationToken ct = default);
    Task<IReadOnlyList<FinancialTransaction>> GetPendingTransactionsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<FinancialTransaction>> GetTransactionsBySaleAsync(Guid saleId, CancellationToken ct = default);
    /// <summary>Lançamentos created for a document (e.g. ReferenceType "Purchase" + the purchase id).</summary>
    Task<IReadOnlyList<FinancialTransaction>> GetTransactionsByReferenceAsync(string referenceType, Guid referenceId, CancellationToken ct = default);
    Task AddTransactionAsync(FinancialTransaction transaction, CancellationToken ct = default);

    /// <summary>Títulos of one direction by status/due date/customer, soonest due first, at most <paramref name="take"/>.</summary>
    Task<IReadOnlyList<FinancialTransaction>> GetTitlesAsync(
        Nexo.Application.Features.Financial.TitlesFilter filter, DateTime todayStartUtc, int take, CancellationToken ct = default);
    /// <summary>Due date and amount of every open título of one direction (for the aging).</summary>
    Task<IReadOnlyList<Nexo.Application.Features.Financial.OpenTitleRow>> GetOpenTitleRowsAsync(
        TransactionType type, Guid? customerId, Guid? supplierId, CancellationToken ct = default);
    Task<IReadOnlyDictionary<Guid, string>> GetCustomerNamesAsync(IEnumerable<Guid> ids, CancellationToken ct = default);
    Task<IReadOnlyDictionary<Guid, string>> GetSupplierNamesAsync(IEnumerable<Guid> ids, CancellationToken ct = default);

    // Fluxo de caixa
    /// <summary>Money in − money out of settled lançamentos paid in [from, to) (from null = since the start).</summary>
    Task<decimal> GetRealizedNetAsync(DateTime? fromUtc, DateTime toUtc, CancellationToken ct = default);
    /// <summary>
    /// Per Brazil day and direction: realized (paid in [from, to)) and projected (open, due in
    /// [max(from, today), to)).
    /// </summary>
    Task<IReadOnlyList<Nexo.Application.Features.Financial.CashFlowDayRow>> GetCashFlowDaysAsync(
        DateTime fromUtc, DateTime toUtc, DateTime todayStartUtc, CancellationToken ct = default);
    /// <summary>Money in − money out of open títulos due in [from, to).</summary>
    Task<decimal> GetProjectedNetAsync(DateTime fromUtc, DateTime toUtc, CancellationToken ct = default);
    /// <summary>Open títulos already past due: (to receive, to pay).</summary>
    Task<(decimal In, decimal Out)> GetOverdueTotalsAsync(DateTime todayStartUtc, CancellationToken ct = default);

    // Recorrências
    Task<IReadOnlyList<FinancialRecurrence>> GetRecurrencesAsync(bool includeInactive, CancellationToken ct = default);
    Task<FinancialRecurrence?> GetRecurrenceByIdAsync(Guid id, CancellationToken ct = default);
    Task AddRecurrenceAsync(FinancialRecurrence recurrence, CancellationToken ct = default);
    /// <summary>
    /// Active recurrences with months still to generate, row-locked (FOR UPDATE) and reloaded: two
    /// requests generating at once serialise, and the second finds nothing left to do.
    /// </summary>
    Task<IReadOnlyList<FinancialRecurrence>> LockRecurrencesDueAsync(DateOnly horizonMonth, Guid tenantId, CancellationToken ct = default);

    /// <summary>Transaction-scoped lock on a record of origin (pg_advisory_xact_lock): one posting at a time.</summary>
    Task LockReferenceAsync(Guid referenceId, CancellationToken ct = default);

    /// <summary>Row lock (FOR UPDATE) on a lançamento, reloading it if tracked — serialises pay × cancel.</summary>
    Task LockTransactionAsync(Guid id, Guid tenantId, CancellationToken ct = default);
    /// <summary>Row locks on every lançamento of a record of origin (e.g. a sale being cancelled).</summary>
    Task LockTransactionsByReferenceAsync(string referenceType, Guid referenceId, Guid tenantId, CancellationToken ct = default);

    /// <summary>Entries of the period in the regime's order (newest first), at most <paramref name="take"/>.</summary>
    Task<IReadOnlyList<FinancialTransaction>> GetLedgerEntriesAsync(
        Nexo.Application.Features.Financial.LedgerFilter filter, int take, CancellationToken ct = default);
    /// <summary>Sums of the whole period by category and direction.</summary>
    Task<IReadOnlyList<Nexo.Application.Features.Financial.LedgerTotalRow>> GetLedgerTotalsAsync(
        Nexo.Application.Features.Financial.LedgerFilter filter, CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}
