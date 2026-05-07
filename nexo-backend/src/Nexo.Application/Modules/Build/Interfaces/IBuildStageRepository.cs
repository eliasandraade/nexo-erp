using Nexo.Domain.Modules.Build;

namespace Nexo.Application.Modules.Build.Interfaces;

public interface IBuildStageRepository
{
    Task<BuildStage?> GetByIdAsync(Guid id, CancellationToken ct = default);

    Task<IReadOnlyList<BuildStage>> GetByProjectAsync(Guid projectId, CancellationToken ct = default);

    /// <summary>Returns the highest Order value for stages in the project (0 if none).</summary>
    Task<int> GetMaxOrderAsync(Guid projectId, CancellationToken ct = default);

    Task AddAsync(BuildStage stage, CancellationToken ct = default);
    void Update(BuildStage stage);
    void Remove(BuildStage stage);
    Task SaveChangesAsync(CancellationToken ct = default);
}
