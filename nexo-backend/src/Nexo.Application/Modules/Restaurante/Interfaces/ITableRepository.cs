using Nexo.Domain.Modules.Restaurante;

namespace Nexo.Application.Modules.Restaurante.Interfaces;

public interface ITableRepository
{
    Task<RestTable?> GetByIdAsync(Guid id, CancellationToken ct = default);
    /// <summary>Loads table with pessimistic row-level lock (SELECT FOR UPDATE).</summary>
    Task<RestTable?> GetByIdForUpdateAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<RestTable>> GetAllAsync(bool includeInactive = false, CancellationToken ct = default);
    Task<IReadOnlyList<RestTable>> GetByAreaAsync(Guid areaId, CancellationToken ct = default);
    Task AddAsync(RestTable table, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
