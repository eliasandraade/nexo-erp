using Microsoft.EntityFrameworkCore;
using Nexo.Application.Modules.Service.Interfaces;
using Nexo.Domain.Modules.Service;
using Nexo.Infrastructure.Persistence;
using Npgsql;

namespace Nexo.Infrastructure.Repositories.Modules.Service;

public class SvcCommissionRepository : ISvcCommissionRepository
{
    private const string SourceUniqueIndex = "ux_svc_commission_entries_source";

    private readonly NexoDbContext _context;
    public SvcCommissionRepository(NexoDbContext context) => _context = context;

    // ── Entries ──────────────────────────────────────────────────────────────
    public async Task<IReadOnlyList<SvcCommissionEntry>> GetEntriesAsync(
        Guid? professionalId, DateTime? from, DateTime? to, bool? settled, Guid? payoutId, int limit,
        CancellationToken ct = default)
    {
        var q = _context.SvcCommissionEntries.AsNoTracking();
        if (professionalId is { } p) q = q.Where(x => x.ProfessionalId == p);
        if (from is { } f)           q = q.Where(x => x.RecognizedAt >= f);
        if (to is { } t)             q = q.Where(x => x.RecognizedAt <= t);
        if (payoutId is { } po)      q = q.Where(x => x.PayoutId == po);
        // IsSettled is a computed C# property — EF cannot translate it, so filter on the column.
        if (settled is true)         q = q.Where(x => x.PayoutId != null);
        if (settled is false)        q = q.Where(x => x.PayoutId == null);
        return await q.OrderByDescending(x => x.RecognizedAt).ThenBy(x => x.Id).Take(limit).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<SvcCommissionEntry>> GetOpenEntriesForPayoutAsync(
        Guid professionalId, DateTime periodStart, DateTime periodEnd, CancellationToken ct = default)
        => await _context.SvcCommissionEntries
            .AsNoTracking()
            .Where(x => x.ProfessionalId == professionalId
                     && x.PayoutId == null
                     && x.RecognizedAt >= periodStart
                     && x.RecognizedAt <= periodEnd)
            .OrderBy(x => x.RecognizedAt).ThenBy(x => x.Id)
            .ToListAsync(ct);

    public async Task<bool> EntryExistsForSourceAsync(
        SvcCommissionSource source, Guid sourceId, CancellationToken ct = default)
        => await _context.SvcCommissionEntries
            .AnyAsync(x => x.Source == source && x.SourceId == sourceId, ct);

    public async Task<IReadOnlySet<Guid>> GetRecognizedSourceIdsAsync(
        SvcCommissionSource source, IReadOnlyCollection<Guid> sourceIds, CancellationToken ct = default)
    {
        if (sourceIds.Count == 0) return new HashSet<Guid>();
        var ids = sourceIds.ToList();
        return (await _context.SvcCommissionEntries
                .Where(x => x.Source == source && ids.Contains(x.SourceId))
                .Select(x => x.SourceId)
                .ToListAsync(ct))
            .ToHashSet();
    }

    public async Task<bool> AppointmentCoveredByPackageAsync(Guid appointmentId, CancellationToken ct = default)
        => await _context.SvcPackageUsages.AnyAsync(u => u.AppointmentId == appointmentId, ct);

    public async Task<IReadOnlySet<Guid>> GetOrderItemIdsCoveredByPackageAsync(Guid orderId, CancellationToken ct = default)
        => (await _context.SvcPackageUsages
                .Where(u => u.OrderId == orderId && u.OrderItemId != null)
                .Select(u => u.OrderItemId!.Value)
                .ToListAsync(ct))
            .ToHashSet();

    public async Task<bool> TryAddEntryAsync(SvcCommissionEntry entry, CancellationToken ct = default)
    {
        await _context.SvcCommissionEntries.AddAsync(entry, ct);
        try
        {
            await _context.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException ex)
            when (ex.InnerException is PostgresException pg
                  && pg.SqlState == PostgresErrorCodes.UniqueViolation
                  && pg.ConstraintName == SourceUniqueIndex)
        {
            // Another request recognised this source first — that entry is the valid one.
            _context.Entry(entry).State = EntityState.Detached;
            return false;
        }
    }

    public async Task<IReadOnlyDictionary<Guid, (int Count, decimal Amount)>> GetOpenTotalsByProfessionalAsync(
        Guid? professionalId, CancellationToken ct = default)
    {
        var q = _context.SvcCommissionEntries.Where(x => x.PayoutId == null);
        if (professionalId is { } p) q = q.Where(x => x.ProfessionalId == p);
        var rows = await q
            .GroupBy(x => x.ProfessionalId)
            .Select(g => new { ProfessionalId = g.Key, Count = g.Count(), Amount = g.Sum(x => x.CommissionAmount) })
            .ToListAsync(ct);
        return rows.ToDictionary(r => r.ProfessionalId, r => (r.Count, r.Amount));
    }

    public async Task<int> AttachOpenEntriesToPayoutAsync(
        Guid payoutId, IReadOnlyCollection<Guid> entryIds, CancellationToken ct = default)
    {
        var ids = entryIds.ToList();
        var now = DateTime.UtcNow;
        return await _context.SvcCommissionEntries
            .Where(x => ids.Contains(x.Id) && x.PayoutId == null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.PayoutId, payoutId)
                .SetProperty(x => x.UpdatedAt, now), ct);
    }

    // ── Payouts ──────────────────────────────────────────────────────────────
    public async Task<SvcCommissionPayout?> GetPayoutByIdAsync(Guid id, CancellationToken ct = default)
        => await _context.SvcCommissionPayouts.FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<IReadOnlyList<SvcCommissionPayout>> GetPayoutsAsync(
        Guid? professionalId, SvcCommissionPayoutStatus? status, CancellationToken ct = default)
    {
        var q = _context.SvcCommissionPayouts.AsNoTracking();
        if (professionalId is { } p) q = q.Where(x => x.ProfessionalId == p);
        if (status is { } s)         q = q.Where(x => x.Status == s);
        return await q.OrderByDescending(x => x.CreatedAt).ToListAsync(ct);
    }

    public async Task<IReadOnlyDictionary<(Guid ProfessionalId, SvcCommissionPayoutStatus Status), (int Count, decimal Amount)>>
        GetPayoutTotalsAsync(Guid? professionalId, CancellationToken ct = default)
    {
        var q = _context.SvcCommissionPayouts.AsQueryable();
        if (professionalId is { } p) q = q.Where(x => x.ProfessionalId == p);
        var rows = await q
            .GroupBy(x => new { x.ProfessionalId, x.Status })
            .Select(g => new { g.Key.ProfessionalId, g.Key.Status, Count = g.Count(), Amount = g.Sum(x => x.TotalAmount) })
            .ToListAsync(ct);
        return rows.ToDictionary(r => (r.ProfessionalId, r.Status), r => (r.Count, r.Amount));
    }

    public async Task AddPayoutAsync(SvcCommissionPayout payout, CancellationToken ct = default)
        => await _context.SvcCommissionPayouts.AddAsync(payout, ct);

    public async Task<bool> TryMarkPayoutPaidAsync(Guid payoutId, DateTime paidAt, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var affected = await _context.SvcCommissionPayouts
            .Where(x => x.Id == payoutId && x.Status == SvcCommissionPayoutStatus.Pending)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.Status, SvcCommissionPayoutStatus.Paid)
                .SetProperty(x => x.PaidAt, paidAt)
                .SetProperty(x => x.UpdatedAt, now), ct);
        return affected == 1;
    }

    public async Task ReloadAsync(SvcCommissionPayout payout, CancellationToken ct = default)
        => await _context.Entry(payout).ReloadAsync(ct);

    public async Task SaveChangesAsync(CancellationToken ct = default) => await _context.SaveChangesAsync(ct);
}
