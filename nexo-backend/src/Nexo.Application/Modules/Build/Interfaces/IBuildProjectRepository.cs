using Nexo.Domain.Modules.Build;

namespace Nexo.Application.Modules.Build.Interfaces;

public interface IBuildProjectRepository
{
    Task<BuildProject?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>Loads project with Stages and DailyLogs collections.</summary>
    Task<BuildProject?> GetByIdWithDetailsAsync(Guid id, CancellationToken ct = default);

    Task<IReadOnlyList<BuildProject>> GetAllAsync(
        BuildProjectStatus? status   = null,
        int                 page     = 1,
        int                 pageSize = 20,
        CancellationToken   ct       = default);

    Task<int> CountAsync(BuildProjectStatus? status = null, CancellationToken ct = default);

    Task AddAsync(BuildProject project, CancellationToken ct = default);
    void Update(BuildProject project);
    Task SaveChangesAsync(CancellationToken ct = default);
}
