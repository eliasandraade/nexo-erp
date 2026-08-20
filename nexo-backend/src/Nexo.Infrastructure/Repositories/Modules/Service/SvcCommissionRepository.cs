using Microsoft.EntityFrameworkCore;
using Nexo.Application.Modules.Service.Interfaces;
using Nexo.Domain.Modules.Service;
using Nexo.Infrastructure.Persistence;

namespace Nexo.Infrastructure.Repositories.Modules.Service;

public class SvcCommissionRepository : ISvcCommissionRepository
{
    private readonly NexoDbContext _context;
    public SvcCommissionRepository(NexoDbContext context) => _context = context;

    public async Task<IReadOnlyList<SvcCommissionEntry>> GetEntriesAsync(
        Guid? professionalId, DateTime? from, DateTime? to, bool? settled, CancellationToken ct = default)
    {
        var q = _context.SvcCommissionEntries.AsQueryable();
        if (professionalId is { } p) q = q.Where(x => x.ProfessionalId == p);
        if (from is { } f)           q = q.Where(x => x.RecognizedAt >= f);
        if (to is { } t)             q = q.Where(x => x.RecognizedAt <= t);
        // IsSettled is a computed C# property — EF cannot translate it, so filter on the column.
        if (settled is true)         q = q.Where(x => x.PayoutId != null);
        if (settled is false)        q = q.Where(x => x.PayoutId == null);
        return await q.OrderByDescending(x => x.RecognizedAt).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<SvcCommissionEntry>> GetOpenEntriesForPayoutAsync(
        Guid professionalId, DateTime periodStart, DateTime periodEnd, CancellationToken ct = default)
        => await _context.SvcCommissionEntries
            .Where(x => x.ProfessionalId == professionalId
                     && x.PayoutId == null
                     && x.RecognizedAt >= periodStart
                     && x.RecognizedAt <= periodEnd)
            .OrderBy(x => x.RecognizedAt)
            .ToListAsync(ct);

    public async Task<bool> EntryExistsForSourceAsync(
        SvcCommissionSource source, Guid sourceId, CancellationToken ct = default)
        => await _context.SvcCommissionEntries
            .AnyAsync(x => x.Source == source && x.SourceId == sourceId, ct);

    public async Task AddEntryAsync(SvcCommissionEntry entry, CancellationToken ct = default)
        => await _context.SvcCommissionEntries.AddAsync(entry, ct);

    public async Task<SvcCommissionPayout?> GetPayoutByIdAsync(Guid id, CancellationToken ct = default)
        => await _context.SvcCommissionPayouts.FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<IReadOnlyList<SvcCommissionPayout>> GetPayoutsAsync(
        Guid? professionalId, SvcCommissionPayoutStatus? status, CancellationToken ct = default)
    {
        var q = _context.SvcCommissionPayouts.AsQueryable();
        if (professionalId is { } p) q = q.Where(x => x.ProfessionalId == p);
        if (status is { } s)         q = q.Where(x => x.Status == s);
        return await q.OrderByDescending(x => x.PeriodEnd).ToListAsync(ct);
    }

    public async Task AddPayoutAsync(SvcCommissionPayout payout, CancellationToken ct = default)
        => await _context.SvcCommissionPayouts.AddAsync(payout, ct);

    public void UpdatePayout(SvcCommissionPayout payout) => _context.SvcCommissionPayouts.Update(payout);

    public async Task SaveChangesAsync(CancellationToken ct = default) => await _context.SaveChangesAsync(ct);
}
