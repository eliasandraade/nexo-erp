using Microsoft.EntityFrameworkCore;
using Nexo.Application.Common.Interfaces;
using Nexo.Application.Features.Financial;
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
            .Take(500)   // every à-vista sale lands on Contas a Receber now: the statement shows the latest
            .ToListAsync(ct);

    public async Task<IReadOnlyList<FinancialTransaction>> GetPendingTransactionsAsync(CancellationToken ct = default)
        => await _context.FinancialTransactions
            .Where(x => x.Status == TransactionStatus.Pending || x.Status == TransactionStatus.Overdue)
            .OrderBy(x => x.DueDate)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<FinancialTransaction>> GetTransactionsByReferenceAsync(
        string referenceType, Guid referenceId, CancellationToken ct = default)
        => await _context.FinancialTransactions
            .Where(x => x.ReferenceType == referenceType && x.ReferenceId == referenceId)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<FinancialTransaction>> GetTransactionsBySaleAsync(Guid saleId, CancellationToken ct = default)
        => await _context.FinancialTransactions
            .Where(x => x.ReferenceType == "Sale" && x.ReferenceId == saleId)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<FinancialTransaction>> GetLedgerEntriesAsync(
        LedgerFilter filter, int take, CancellationToken ct = default)
    {
        var query = Ledger(filter);
        query = filter.Regime == LedgerRegime.Cash
            ? query.OrderByDescending(x => x.PaidAt).ThenByDescending(x => x.CreatedAt)
            : query.OrderByDescending(x => x.CompetenceDate).ThenByDescending(x => x.CreatedAt);
        return await query.Take(take).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<LedgerTotalRow>> GetLedgerTotalsAsync(LedgerFilter filter, CancellationToken ct = default)
    {
        var rows = await Ledger(filter)
            .GroupBy(x => new { x.Category, x.TransactionType })
            .Select(g => new { g.Key.Category, g.Key.TransactionType, Amount = g.Sum(x => x.Amount), Count = g.Count() })
            .ToListAsync(ct);
        return rows.Select(r => new LedgerTotalRow(r.Category, r.TransactionType, r.Amount, r.Count)).ToList();
    }

    /// <summary>Competência: fact date, every entry not cancelled. Caixa: payment date, settled only.</summary>
    private IQueryable<FinancialTransaction> Ledger(LedgerFilter f)
    {
        var query = f.Regime == LedgerRegime.Cash
            ? _context.FinancialTransactions.Where(x =>
                x.Status == TransactionStatus.Paid && x.PaidAt >= f.From && x.PaidAt < f.To)
            : _context.FinancialTransactions.Where(x =>
                x.Status != TransactionStatus.Cancelled && x.CompetenceDate >= f.From && x.CompetenceDate < f.To);
        if (f.StoreId is { } storeId) query = query.Where(x => x.StoreId == storeId);
        if (f.Category is { } category) query = query.Where(x => x.Category == category);
        return query;
    }

    public async Task<IReadOnlyList<FinancialTransaction>> GetTitlesAsync(
        TitlesFilter f, DateTime todayStartUtc, int take, CancellationToken ct = default)
    {
        // Títulos only: not juros/desconto lines, not mirrors of money that never was a título
        // (Service, counter-entries, devoluções) and not à-vista sale payments (due the moment they
        // were created). A real título — a prazo, parcela, manual, compra — stays.
        var query = _context.FinancialTransactions.Where(x => x.TransactionType == f.Type
            && x.ReferenceType != FinancialTransaction.SettlementReference
            && (x.ReferenceType == null || !x.ReferenceType.StartsWith("Svc"))
            && x.ReferenceType != "SaleCancellation" && x.ReferenceType != "SaleReturn"
            && !(x.ReferenceType == "Sale" && x.DueDate <= x.CreatedAt.AddMinutes(1)));
        query = f.Status switch
        {
            TitleStatusFilter.Open    => query.Where(x => x.Status == TransactionStatus.Pending || x.Status == TransactionStatus.Overdue),
            // overdue = flagged by the job, or pending and already past due (the job runs hourly)
            TitleStatusFilter.Overdue => query.Where(x => x.Status == TransactionStatus.Overdue
                                                          || (x.Status == TransactionStatus.Pending && x.DueDate < todayStartUtc)),
            TitleStatusFilter.Settled => query.Where(x => x.Status == TransactionStatus.Paid || x.Status == TransactionStatus.Abated),
            _                         => query,
        };
        if (f.DueFrom is { } from) query = query.Where(x => x.DueDate >= from);
        if (f.DueTo is { } to) query = query.Where(x => x.DueDate < to);
        if (f.CustomerId is { } customerId) query = query.Where(x => x.CustomerId == customerId);
        if (f.SupplierId is { } supplierId) query = query.Where(x => x.SupplierId == supplierId);
        query = f.Status == TitleStatusFilter.Settled
            ? query.OrderByDescending(x => x.PaidAt).ThenByDescending(x => x.CreatedAt)
            : query.OrderBy(x => x.DueDate).ThenBy(x => x.CreatedAt);
        return await query.Take(take).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<OpenTitleRow>> GetOpenTitleRowsAsync(
        TransactionType type, Guid? customerId, CancellationToken ct = default)
    {
        var query = _context.FinancialTransactions.Where(x => x.TransactionType == type
            && (x.Status == TransactionStatus.Pending || x.Status == TransactionStatus.Overdue));
        if (customerId is { } id) query = query.Where(x => x.CustomerId == id);
        var rows = await query.Select(x => new { x.DueDate, x.Amount }).ToListAsync(ct);
        return rows.Select(r => new OpenTitleRow(r.DueDate, r.Amount)).ToList();
    }

    public async Task<IReadOnlyDictionary<Guid, string>> GetCustomerNamesAsync(IEnumerable<Guid> ids, CancellationToken ct = default)
    {
        var list = ids.Distinct().ToList();
        if (list.Count == 0) return new Dictionary<Guid, string>();
        return await _context.Customers.Where(c => list.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name, ct);
    }

    public async Task<IReadOnlyDictionary<Guid, string>> GetSupplierNamesAsync(IEnumerable<Guid> ids, CancellationToken ct = default)
    {
        var list = ids.Distinct().ToList();
        if (list.Count == 0) return new Dictionary<Guid, string>();
        var rows = await _context.Suppliers.Where(s => list.Contains(s.Id))
            .Select(s => new { s.Id, s.Name, s.TradeName }).ToListAsync(ct);
        return rows.ToDictionary(r => r.Id, r => r.TradeName ?? r.Name);
    }

    public async Task<IReadOnlyList<FinancialRecurrence>> GetRecurrencesAsync(bool includeInactive, CancellationToken ct = default)
    {
        var query = _context.FinancialRecurrences.AsQueryable();
        if (!includeInactive) query = query.Where(r => r.IsActive);
        return await query.OrderBy(r => r.DayOfMonth).ThenBy(r => r.Description).ToListAsync(ct);
    }

    public async Task<FinancialRecurrence?> GetRecurrenceByIdAsync(Guid id, CancellationToken ct = default)
        => await _context.FinancialRecurrences.FirstOrDefaultAsync(r => r.Id == id, ct);

    public async Task AddRecurrenceAsync(FinancialRecurrence recurrence, CancellationToken ct = default)
        => await _context.FinancialRecurrences.AddAsync(recurrence, ct);

    public async Task<IReadOnlyList<FinancialRecurrence>> LockRecurrencesDueAsync(
        DateOnly horizonMonth, Guid tenantId, CancellationToken ct = default)
    {
        await _context.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM nexo.financial_recurrences WHERE tenant_id = {tenantId} AND is_active ORDER BY id FOR UPDATE", ct);
        var due = await _context.FinancialRecurrences
            .Where(r => r.IsActive && (r.GeneratedThrough == null || r.GeneratedThrough < horizonMonth))
            .ToListAsync(ct);
        foreach (var r in due)
            await _context.Entry(r).ReloadAsync(ct);   // a request that generated while we waited moved GeneratedThrough
        return due;
    }

    public async Task LockTransactionAsync(Guid id, Guid tenantId, CancellationToken ct = default)
    {
        await _context.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM nexo.financial_transactions WHERE id = {id} AND tenant_id = {tenantId} FOR UPDATE", ct);
        var tracked = _context.ChangeTracker.Entries<FinancialTransaction>().FirstOrDefault(e => e.Entity.Id == id);
        if (tracked is not null) await tracked.ReloadAsync(ct);
    }

    public async Task LockTransactionsByReferenceAsync(
        string referenceType, Guid referenceId, Guid tenantId, CancellationToken ct = default)
    {
        // Twice: rows inserted by a baixa that committed while we waited (its remainder) are not in the
        // first statement's snapshot; the second one sees and locks them too.
        for (var pass = 0; pass < 2; pass++)
            await _context.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT 1 FROM nexo.financial_transactions WHERE reference_type = {referenceType} AND reference_id = {referenceId} AND tenant_id = {tenantId} ORDER BY id FOR UPDATE", ct);
        foreach (var tracked in _context.ChangeTracker.Entries<FinancialTransaction>()
                     .Where(e => e.Entity.ReferenceType == referenceType && e.Entity.ReferenceId == referenceId).ToList())
            await tracked.ReloadAsync(ct);
    }

    public async Task AddTransactionAsync(FinancialTransaction transaction, CancellationToken ct = default)
        => await _context.FinancialTransactions.AddAsync(transaction, ct);

    public async Task SaveChangesAsync(CancellationToken ct = default)
        => await _context.SaveChangesAsync(ct);
}
