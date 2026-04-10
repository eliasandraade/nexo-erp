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
    Task AddTransactionAsync(FinancialTransaction transaction, CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}
