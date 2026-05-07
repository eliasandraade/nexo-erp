using Nexo.Domain.Modules.Build;

namespace Nexo.Application.Modules.Build.Interfaces;

public interface IBuildDailyLogRepository
{
    Task<BuildDailyLog?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>Loads daily log with its Photos collection.</summary>
    Task<BuildDailyLog?> GetByIdWithPhotosAsync(Guid id, CancellationToken ct = default);

    Task<IReadOnlyList<BuildDailyLog>> GetByProjectAsync(
        Guid              projectId,
        DateOnly?         from     = null,
        DateOnly?         to       = null,
        int               page     = 1,
        int               pageSize = 20,
        CancellationToken ct       = default);

    /// <summary>Returns true if a log already exists for the project on that date.</summary>
    Task<bool> ExistsForDateAsync(Guid projectId, DateOnly date, CancellationToken ct = default);

    Task AddAsync(BuildDailyLog log, CancellationToken ct = default);
    void Update(BuildDailyLog log);
    void Remove(BuildDailyLog log);
    Task SaveChangesAsync(CancellationToken ct = default);
}
