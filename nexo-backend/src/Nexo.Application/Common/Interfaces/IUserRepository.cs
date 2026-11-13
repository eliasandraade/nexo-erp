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

    /// <summary>
    /// Whether a login is already taken ANYWHERE: by a user of any company, or by a platform operator
    /// (whose e-mail is their login on the shared sign-in form). Bypasses the tenant filter on purpose,
    /// because sign-in looks a login up without a company. The login is normalized here.
    /// </summary>
    Task<bool> LoginExistsAsync(string login, CancellationToken ct = default);
    Task<bool> EmailExistsAsync(string email, CancellationToken ct = default);
    Task<IReadOnlyList<User>> GetAllAsync(CancellationToken ct = default);
    Task AddAsync(User user, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);

    /// <summary>
    /// Serialises, in the database, every change to who is an active Diretoria of this company until the current
    /// transaction ends. Call it before reading what the change depends on. Requires an open transaction; a wait that
    /// runs out is a <see cref="Nexo.Domain.Exceptions.ConflictException"/>.
    /// </summary>
    Task LockDirectorsAsync(Guid tenantId, CancellationToken ct = default);

    /// <summary>Whether the company has an active Diretoria other than this user. Only conclusive under <see cref="LockDirectorsAsync"/>.</summary>
    Task<bool> HasAnotherActiveDirectorAsync(Guid tenantId, Guid userId, CancellationToken ct = default);

    /// <summary>Claims a TOTP step for the user (only if newer than the last one) — atomic; false = replay.</summary>
    Task<bool> TryClaimMfaStepAsync(Guid userId, long step, CancellationToken ct = default);

    /// <summary>Swaps the recovery-code list only if unchanged since read — atomic; false = used concurrently.</summary>
    Task<bool> TryReplaceRecoveryCodesAsync(Guid userId, string? expected, string replacement, CancellationToken ct = default);
}
