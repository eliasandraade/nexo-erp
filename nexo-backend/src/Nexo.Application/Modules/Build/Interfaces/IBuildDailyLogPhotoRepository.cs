using Nexo.Domain.Modules.Build;

namespace Nexo.Application.Modules.Build.Interfaces;

public interface IBuildDailyLogPhotoRepository
{
    Task<BuildDailyLogPhoto?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<BuildDailyLogPhoto>> GetByLogAsync(Guid dailyLogId, CancellationToken ct = default);

    Task AddAsync(BuildDailyLogPhoto photo, CancellationToken ct = default);
    void Remove(BuildDailyLogPhoto photo);
    Task SaveChangesAsync(CancellationToken ct = default);
}
