using Nexo.Domain.Entities;

namespace Nexo.Application.Common.Interfaces;

/// <summary>
/// User repository — all queries are automatically scoped to the current tenant
/// via EF Core Global Query Filters. No tenant_id parameter required.
/// </summary>
public interface IUserRepository
{
    Task<User?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Looks up a user by id, bypassing the tenant query filter. Use ONLY for the
    /// tenant-less refresh flow: /api/auth/refresh is [AllowAnonymous], so no tenant
    /// context is resolved and the normal <see cref="GetByIdAsync"/> would filter on
    /// CurrentTenantIdForFilter == Guid.Empty and return null. The caller MUST have
    /// already validated the refresh token's signature and confirmed it is present in
    /// the refresh-token store, and SHOULD assert the loaded user's TenantId matches
    /// the token's tenantId claim (defence in depth).
    /// </summary>
    Task<User?> GetByIdAcrossTenantsAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Looks up a user by login, bypassing the tenant query filter. Use ONLY for
    /// the tenant-less login flow, where no tenant context exists yet. For any
    /// authenticated, tenant-bound credential check use
    /// <see cref="GetByLoginInTenantAsync"/> instead.
    /// </summary>
    Task<User?> GetByLoginAsync(string login, CancellationToken ct = default);

    /// <summary>
    /// Looks up a user by login WITHIN a specific tenant. Explicitly scoped so a
    /// user from another tenant can never be returned — use this whenever a tenant
    /// context is already established (e.g. manager verification).
    /// </summary>
    Task<User?> GetByLoginInTenantAsync(string login, Guid tenantId, CancellationToken ct = default);

    Task<User?> GetByEmailAsync(string email, CancellationToken ct = default);
    Task<bool> LoginExistsAsync(string login, CancellationToken ct = default);
    Task<bool> EmailExistsAsync(string email, CancellationToken ct = default);
    Task<IReadOnlyList<User>> GetAllAsync(CancellationToken ct = default);
    Task AddAsync(User user, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
