using Nexo.Domain.Entities;

namespace Nexo.Application.Common.Interfaces;

public interface ICategoryRepository
{
    Task<Category?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<Category>> GetAllAsync(bool includeInactive = false, CancellationToken ct = default);
    Task<bool> HasChildrenAsync(Guid id, CancellationToken ct = default);
    Task AddAsync(Category category, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
