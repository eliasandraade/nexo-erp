using Microsoft.EntityFrameworkCore;
using Nexo.Application.Common.Interfaces;
using Nexo.Domain.Entities;
using Nexo.Infrastructure.Persistence;

namespace Nexo.Infrastructure.Repositories;

/// <summary>
/// User repository.
/// Most queries are automatically scoped to the current tenant via EF Core Global Query Filters.
/// Do NOT add tenant_id parameters to scoped queries — the filter handles isolation.
///
/// Exception: GetByLoginAsync and GetByEmailAsync bypass the filter because login is a
/// cross-tenant operation — the caller provides credentials, not a tenant identity, and
/// the request arrives before any tenant context is established.
/// </summary>
public class UserRepository : IUserRepository
{
    private readonly NexoDbContext _context;

    public UserRepository(NexoDbContext context) => _context = context;

    public async Task<User?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await _context.Users.FirstOrDefaultAsync(u => u.Id == id, ct);

    // IgnoreQueryFilters: login is a cross-tenant operation. The caller arrives without a
    // tenant context; the filter would otherwise produce WHERE tenant_id = Guid.Empty → no rows.
    public async Task<User?> GetByLoginAsync(string login, CancellationToken ct = default)
        => await _context.Users.IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Login == login, ct);

    // Scoped to an explicit tenant. The global tenant query filter remains ACTIVE
    // (no IgnoreQueryFilters), and the explicit TenantId predicate is
    // defense-in-depth: a user from a different tenant can never be returned, even
    // if the ambient filter were ever bypassed. Used by manager verification to
    // prevent cross-tenant authorization.
    public async Task<User?> GetByLoginInTenantAsync(string login, Guid tenantId, CancellationToken ct = default)
        => await _context.Users
            .Where(u => u.Login == login && u.TenantId == tenantId)
            .FirstOrDefaultAsync(ct);

    public async Task<User?> GetByEmailAsync(string email, CancellationToken ct = default)
        => await _context.Users.IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Email == email, ct);

    public async Task<bool> LoginExistsAsync(string login, CancellationToken ct = default)
        => await _context.Users.AnyAsync(u => u.Login == login, ct);

    public async Task<bool> EmailExistsAsync(string email, CancellationToken ct = default)
        => await _context.Users.AnyAsync(u => u.Email == email, ct);

    public async Task<IReadOnlyList<User>> GetAllAsync(CancellationToken ct = default)
        => await _context.Users
            .OrderBy(u => u.FullName)
            .ToListAsync(ct);

    public async Task AddAsync(User user, CancellationToken ct = default)
        => await _context.Users.AddAsync(user, ct);

    public async Task SaveChangesAsync(CancellationToken ct = default)
        => await _context.SaveChangesAsync(ct);
}
