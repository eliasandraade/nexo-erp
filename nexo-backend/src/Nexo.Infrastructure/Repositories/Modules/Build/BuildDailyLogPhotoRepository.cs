using Microsoft.EntityFrameworkCore;
using Nexo.Application.Modules.Build.Interfaces;
using Nexo.Domain.Modules.Build;
using Nexo.Infrastructure.Persistence;

namespace Nexo.Infrastructure.Repositories.Modules.Build;

public class BuildDailyLogPhotoRepository : IBuildDailyLogPhotoRepository
{
    private readonly NexoDbContext _context;

    public BuildDailyLogPhotoRepository(NexoDbContext context) => _context = context;

    public async Task<BuildDailyLogPhoto?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await _context.BldDailyLogPhotos
            .FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<IReadOnlyList<BuildDailyLogPhoto>> GetByLogAsync(Guid dailyLogId, CancellationToken ct = default)
        => await _context.BldDailyLogPhotos
            .Where(x => x.DailyLogId == dailyLogId)
            .OrderBy(x => x.CreatedAt)
            .ToListAsync(ct);

    public async Task AddAsync(BuildDailyLogPhoto photo, CancellationToken ct = default)
        => await _context.BldDailyLogPhotos.AddAsync(photo, ct);

    public void Remove(BuildDailyLogPhoto photo)
        => _context.BldDailyLogPhotos.Remove(photo);

    public async Task SaveChangesAsync(CancellationToken ct = default)
        => await _context.SaveChangesAsync(ct);
}
