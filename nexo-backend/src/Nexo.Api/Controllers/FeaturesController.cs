using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Nexo.Application.Common.Interfaces;
using Nexo.Infrastructure.Persistence;

namespace Nexo.Api.Controllers;

/// <summary>
/// Tenant-side endpoint for resolving feature flags.
/// Returns the effective flag values for the authenticated tenant.
///
/// Resolution: TenantFeatureOverride.IsEnabled ?? FeatureFlag.DefaultEnabled
///
/// Cached in Redis as "features:{tenantId}" with 2-minute TTL.
/// Cache is invalidated immediately when a platform admin changes a tenant override,
/// and on TTL for global default changes.
/// </summary>
[ApiController]
[Route("api/features")]
[Authorize]
public class FeaturesController : ControllerBase
{
    private readonly NexoDbContext _db;
    private readonly ICurrentTenant _currentTenant;
    private readonly ICacheService _cache;

    public FeaturesController(NexoDbContext db, ICurrentTenant currentTenant, ICacheService cache)
    {
        _db             = db;
        _currentTenant  = currentTenant;
        _cache          = cache;
    }

    /// <summary>
    /// Returns all resolved feature flags for the authenticated tenant.
    /// Response: { "pdv-desconto-gerente": true, "restaurante-taxa-servico": false, ... }
    ///
    /// Cached for 2 minutes in Redis. Safe to call on every app load.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetFeatures(CancellationToken ct)
    {
        if (!_currentTenant.IsResolved)
            return Unauthorized();

        var tenantId = _currentTenant.Id;
        var cacheKey = $"features:{tenantId}";

        // Try Redis cache first
        var cached = await _cache.GetAsync<Dictionary<string, bool>>(cacheKey, ct);
        if (cached is not null)
            return Ok(cached);

        // Resolve from DB: fetch all flags + this tenant's overrides
        var flags = await _db.FeatureFlags
            .ToListAsync(ct);  // no tenant filter on FeatureFlags (global table)

        var overrides = await _db.TenantFeatureOverrides
            .IgnoreQueryFilters()
            .Where(o => o.TenantId == tenantId)
            .ToDictionaryAsync(o => o.FlagKey, o => o.IsEnabled, ct);

        var resolved = flags.ToDictionary(
            f => f.Key,
            f => overrides.TryGetValue(f.Key, out var ov) ? ov : f.DefaultEnabled
        );

        // Cache for 2 minutes
        await _cache.SetAsync(cacheKey, resolved, TimeSpan.FromMinutes(2), ct);

        return Ok(resolved);
    }
}
