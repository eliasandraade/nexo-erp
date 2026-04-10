using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Nexo.Application.Common.Interfaces;
using Nexo.Domain.Enums;
using Nexo.Infrastructure.Persistence;

namespace Nexo.Infrastructure.Modules;

/// <summary>
/// Verifica acesso a módulos com cache em memória (TTL 5 min).
/// Fonte de verdade: module_subscriptions no banco.
/// </summary>
public class ModuleAccessService : IModuleAccessService
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    private readonly NexoDbContext _context;
    private readonly IMemoryCache  _cache;

    public ModuleAccessService(NexoDbContext context, IMemoryCache cache)
    {
        _context = context;
        _cache   = cache;
    }

    public async Task<bool> HasActiveModuleAsync(Guid tenantId, string moduleKey, CancellationToken ct = default)
    {
        var cacheKey = CacheKey(tenantId, moduleKey);

        if (_cache.TryGetValue<bool>(cacheKey, out var cached))
            return cached;

        // Fonte de verdade: banco (ModuleSubscription não é TenantEntity — sem GQF)
        var isActive = await _context.ModuleSubscriptions
            .AnyAsync(s =>
                s.TenantId  == tenantId &&
                s.ModuleKey == moduleKey.ToLowerInvariant() &&
                (s.Status == SubscriptionStatus.Active || s.Status == SubscriptionStatus.Trialing) &&
                (s.CurrentPeriodEnd == null || s.CurrentPeriodEnd > DateTime.UtcNow),
                ct);

        _cache.Set(cacheKey, isActive, CacheTtl);
        return isActive;
    }

    public void InvalidateCache(Guid tenantId, string moduleKey)
        => _cache.Remove(CacheKey(tenantId, moduleKey));

    private static string CacheKey(Guid tenantId, string moduleKey)
        => $"mod:{tenantId}:{moduleKey.ToLowerInvariant()}";
}
