using Nexo.Domain.Entities;

namespace Nexo.Application.Common.Interfaces;

/// <summary>
/// User repository — all queries are automatically scoped to the current tenant
/// via EF Core Global Query Filters. No tenant_id parameter required.
/// </summary>
public interface IUserRepository
{
    Task<User?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<User?> GetByLoginAsync(string login, CancellationToken ct = default);
    Task<User?> GetByEmailAsync(string email, CancellationToken ct = default);
    Task<bool> LoginExistsAsync(string login, CancellationToken ct = default);
    Task<bool> EmailExistsAsync(string email, CancellationToken ct = default);
    Task<IReadOnlyList<User>> GetAllAsync(CancellationToken ct = default);
    Task AddAsync(User user, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
