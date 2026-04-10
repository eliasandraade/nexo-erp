using Microsoft.EntityFrameworkCore;
using Nexo.Application.Common.Interfaces;
using Nexo.Domain.Entities;
using Nexo.Domain.Enums;
using Nexo.Infrastructure.Persistence;

namespace Nexo.Infrastructure.Repositories;

public class FinancialRepository : IFinancialRepository
{
    private readonly NexoDbContext _context;

    public FinancialRepository(NexoDbContext context) => _context = context;

    public async Task<FinancialAccount?> GetAccountByIdAsync(Guid id, CancellationToken ct = default)
        => await _context.FinancialAccounts.FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<FinancialAccount?> GetDefaultByTypeAsync(Domain.Enums.FinancialAccountType type, CancellationToken ct = default)
        => await _context.FinancialAccounts
            .Where(x => x.AccountType == type && x.IsActive)
            .OrderBy(x => x.Code)
            .FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<FinancialAccount>> GetAllAccountsAsync(bool includeInactive = false, CancellationToken ct = default)
        => await _context.FinancialAccounts
            .Where(x => includeInactive || x.IsActive)
            .OrderBy(x => x.Code)
            .ToListAsync(ct);

    public async Task<bool> AccountCodeExistsAsync(string code, Guid? excludeId = null, CancellationToken ct = default)
        => await _context.FinancialAccounts.AnyAsync(
            x => x.Code == code && (excludeId == null || x.Id != excludeId), ct);

    public async Task AddAccountAsync(FinancialAccount account, CancellationToken ct = default)
        => await _context.FinancialAccounts.AddAsync(account, ct);

    public async Task<FinancialTransaction?> GetTransactionByIdAsync(Guid id, CancellationToken ct = default)
        => await _context.FinancialTransactions.FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<IReadOnlyList<FinancialTransaction>> GetTransactionsByAccountAsync(Guid accountId, CancellationToken ct = default)
        => await _context.FinancialTransactions
            .Where(x => x.FinancialAccountId == accountId)
            .OrderByDescending(x => x.DueDate)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<FinancialTransaction>> GetPendingTransactionsAsync(CancellationToken ct = default)
        => await _context.FinancialTransactions
            .Where(x => x.Status == TransactionStatus.Pending || x.Status == TransactionStatus.Overdue)
            .OrderBy(x => x.DueDate)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<FinancialTransaction>> GetTransactionsBySaleAsync(Guid saleId, CancellationToken ct = default)
        => await _context.FinancialTransactions
            .Where(x => x.ReferenceType == "Sale" && x.ReferenceId == saleId)
            .ToListAsync(ct);

    public async Task AddTransactionAsync(FinancialTransaction transaction, CancellationToken ct = default)
        => await _context.FinancialTransactions.AddAsync(transaction, ct);

    public async Task SaveChangesAsync(CancellationToken ct = default)
        => await _context.SaveChangesAsync(ct);
}
