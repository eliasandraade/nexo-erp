using Microsoft.EntityFrameworkCore;
using Nexo.Application.Modules.Build.Interfaces;
using Nexo.Domain.Modules.Build;
using Nexo.Infrastructure.Persistence;

namespace Nexo.Infrastructure.Repositories.Modules.Build;

public class BuildDailyLogRepository : IBuildDailyLogRepository
{
    private readonly NexoDbContext _context;

    public BuildDailyLogRepository(NexoDbContext context) => _context = context;

    public async Task<BuildDailyLog?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await _context.BldDailyLogs
            .FirstOrDefaultAsync(x => x.Id == id, ct);

    /// <summary>Loads daily log with its Photos collection.</summary>
    public async Task<BuildDailyLog?> GetByIdWithPhotosAsync(Guid id, CancellationToken ct = default)
        => await _context.BldDailyLogs
            .Include(x => x.Photos.OrderBy(p => p.CreatedAt))
            .FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<IReadOnlyList<BuildDailyLog>> GetByProjectAsync(
        Guid              projectId,
        DateOnly?         from     = null,
        DateOnly?         to       = null,
        int               page     = 1,
        int               pageSize = 20,
        CancellationToken ct       = default)
    {
        var query = _context.BldDailyLogs
            .Where(x => x.ProjectId == projectId);

        if (from.HasValue) query = query.Where(x => x.Date >= from.Value);
        if (to.HasValue)   query = query.Where(x => x.Date <= to.Value);

        return await query
            .OrderByDescending(x => x.Date)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);
    }

    public async Task<bool> ExistsForDateAsync(Guid projectId, DateOnly date, CancellationToken ct = default)
        => await _context.BldDailyLogs
            .AnyAsync(x => x.ProjectId == projectId && x.Date == date, ct);

    public async Task AddAsync(BuildDailyLog log, CancellationToken ct = default)
        => await _context.BldDailyLogs.AddAsync(log, ct);

    public void Update(BuildDailyLog log)
        => _context.BldDailyLogs.Update(log);

    public void Remove(BuildDailyLog log)
        => _context.BldDailyLogs.Remove(log);

    public async Task SaveChangesAsync(CancellationToken ct = default)
        => await _context.SaveChangesAsync(ct);
}
