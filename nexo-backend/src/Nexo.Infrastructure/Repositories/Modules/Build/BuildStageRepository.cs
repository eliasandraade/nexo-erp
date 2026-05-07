using Microsoft.EntityFrameworkCore;
using Nexo.Application.Modules.Build.Interfaces;
using Nexo.Domain.Modules.Build;
using Nexo.Infrastructure.Persistence;

namespace Nexo.Infrastructure.Repositories.Modules.Build;

public class BuildStageRepository : IBuildStageRepository
{
    private readonly NexoDbContext _context;

    public BuildStageRepository(NexoDbContext context) => _context = context;

    public async Task<BuildStage?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await _context.BldStages
            .FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<IReadOnlyList<BuildStage>> GetByProjectAsync(Guid projectId, CancellationToken ct = default)
        => await _context.BldStages
            .Where(x => x.ProjectId == projectId)
            .OrderBy(x => x.Order)
            .ToListAsync(ct);

    public async Task<int> GetMaxOrderAsync(Guid projectId, CancellationToken ct = default)
    {
        var max = await _context.BldStages
            .Where(x => x.ProjectId == projectId)
            .MaxAsync(x => (int?)x.Order, ct);
        return max ?? 0;
    }

    public async Task AddAsync(BuildStage stage, CancellationToken ct = default)
        => await _context.BldStages.AddAsync(stage, ct);

    public void Update(BuildStage stage)
        => _context.BldStages.Update(stage);

    public void Remove(BuildStage stage)
        => _context.BldStages.Remove(stage);

    public async Task SaveChangesAsync(CancellationToken ct = default)
        => await _context.SaveChangesAsync(ct);
}
