using Microsoft.EntityFrameworkCore;
using Nexo.Application.Common.Interfaces;
using Nexo.Domain.Entities;
using Nexo.Domain.Enums;
using Nexo.Infrastructure.Persistence;

namespace Nexo.Infrastructure.Repositories;

/// <summary>
/// Tenant repository — platform-level, bypasses Global Query Filters.
/// Used by: TenantResolutionMiddleware, AuthService, DataSeeder, Platform admin.
/// </summary>
public class TenantRepository : ITenantRepository
{
    private readonly NexoDbContext _context;

    public TenantRepository(NexoDbContext context) => _context = context;

    public async Task<Tenant?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await _context.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, ct);

    public async Task<Tenant?> GetBySlugAsync(string slug, CancellationToken ct = default)
        => await _context.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Slug == slug, ct);

    public async Task<IReadOnlyList<Tenant>> GetAllAsync(CancellationToken ct = default)
        => await _context.Tenants
            .AsNoTracking()
            .OrderBy(t => t.CompanyName)
            .ToListAsync(ct);

    /// <summary>
    /// Returns the list of module keys with an active subscription for the given tenant.
    /// Called by TenantResolutionMiddleware (with Redis cache in front).
    /// </summary>
    public async Task<IReadOnlyList<string>> GetActiveModuleKeysAsync(
        Guid tenantId,
        CancellationToken ct = default)
    {
        return await _context.ModuleSubscriptions
            .Where(s =>
                s.TenantId == tenantId &&
                (s.Status == SubscriptionStatus.Active || s.Status == SubscriptionStatus.Trialing) &&
                (s.CurrentPeriodEnd == null || s.CurrentPeriodEnd > DateTime.UtcNow))
            .Select(s => s.ModuleKey)
            .ToListAsync(ct);
    }

    public async Task<DateTime?> GetTrialEndsAtAsync(Guid tenantId, CancellationToken ct = default)
    {
        return await _context.ModuleSubscriptions
            .Where(s => s.TenantId == tenantId && s.PlanType == PlanType.Trial)
            .Select(s => s.CurrentPeriodEnd)
            .FirstOrDefaultAsync(ct);
    }

    public async Task AddAsync(Tenant tenant, CancellationToken ct = default)
        => await _context.Tenants.AddAsync(tenant, ct);

    public async Task SaveChangesAsync(CancellationToken ct = default)
        => await _context.SaveChangesAsync(ct);
}
