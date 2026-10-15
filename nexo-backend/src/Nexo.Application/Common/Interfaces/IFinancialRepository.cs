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
