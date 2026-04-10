using Nexo.Domain.Modules.Restaurante;

namespace Nexo.Application.Modules.Restaurante.Interfaces;

public interface IAreaRepository
{
    Task<RestArea?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<RestArea>> GetAllAsync(bool includeInactive = false, CancellationToken ct = default);
    Task AddAsync(RestArea area, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
