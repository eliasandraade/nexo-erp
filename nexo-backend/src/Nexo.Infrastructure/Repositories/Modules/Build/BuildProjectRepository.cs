using Microsoft.EntityFrameworkCore;
using Nexo.Application.Modules.Build.Interfaces;
using Nexo.Domain.Modules.Build;
using Nexo.Infrastructure.Persistence;

namespace Nexo.Infrastructure.Repositories.Modules.Build;

public class BuildProjectRepository : IBuildProjectRepository
{
    private readonly NexoDbContext _context;

    public BuildProjectRepository(NexoDbContext context) => _context = context;

    public async Task<BuildProject?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await _context.BldProjects
            .FirstOrDefaultAsync(x => x.Id == id, ct);

    /// <summary>
    /// Loads project with its Stages and DailyLogs (without stage photos to keep it light).
    /// Used by project detail view and BuildProjectService.GetDetailsAsync.
    /// </summary>
    public async Task<BuildProject?> GetByIdWithDetailsAsync(Guid id, CancellationToken ct = default)
        => await _context.BldProjects
            .Include(x => x.Stages.OrderBy(s => s.Order))
            .Include(x => x.DailyLogs.OrderByDescending(l => l.Date))
            .FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<IReadOnlyList<BuildProject>> GetAllAsync(
        BuildProjectStatus? status   = null,
        int                 page     = 1,
        int                 pageSize = 20,
        CancellationToken   ct       = default)
    {
        var query = _context.BldProjects.AsQueryable();

        if (status.HasValue)
            query = query.Where(x => x.Status == status.Value);

        return await query
            .OrderByDescending(x => x.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);
    }

    public async Task<int> CountAsync(BuildProjectStatus? status = null, CancellationToken ct = default)
    {
        var query = _context.BldProjects.AsQueryable();
        if (status.HasValue)
            query = query.Where(x => x.Status == status.Value);
        return await query.CountAsync(ct);
    }

    public async Task AddAsync(BuildProject project, CancellationToken ct = default)
        => await _context.BldProjects.AddAsync(project, ct);

    public void Update(BuildProject project)
        => _context.BldProjects.Update(project);

    public async Task SaveChangesAsync(CancellationToken ct = default)
        => await _context.SaveChangesAsync(ct);
}
