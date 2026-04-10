using System.Security.Claims;
using Nexo.Application.Common.Interfaces;

namespace Nexo.Api.Middleware;

/// <summary>
/// Resolves the tenant context for every authenticated HTTP request.
/// Must run AFTER UseAuthentication() and BEFORE UseAuthorization().
///
/// Flow:
///   1. Extract tenant_id from JWT claim.
///   2. Load tenant info from Redis cache (TTL 5min) or DB on cache miss.
///   3. Validate tenant status — returns 403 immediately for suspended tenants,
///      regardless of whether data came from cache or database.
///   4. Populate ICurrentTenant for the request scope.
///
/// On failure: 401 (invalid/missing tenantId claim) or 403 (suspended tenant).
///
/// Cache behaviour:
///   The TenantCacheEntry stores the tenant Status alongside Slug and ActiveModules.
///   Status is always checked after resolution — a suspension takes effect within
///   the cache TTL (≤5 min) without requiring a cache flush.
/// </summary>
public class TenantResolutionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<TenantResolutionMiddleware> _logger;

    public TenantResolutionMiddleware(
        RequestDelegate next,
        ILogger<TenantResolutionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(
        HttpContext context,
        ICurrentTenant currentTenant,
        ITenantRepository tenantRepository,
        ICacheService cache)
    {
        // Skip for anonymous endpoints (login, refresh, health, swagger)
        if (context.User.Identity?.IsAuthenticated != true)
        {
            await _next(context);
            return;
        }

        // 1. Extract tenant_id from JWT
        var tenantIdClaim = context.User.FindFirstValue("tenantId");
        if (!Guid.TryParse(tenantIdClaim, out var tenantId))
        {
            _logger.LogWarning("Request rejected: missing or invalid 'tenantId' claim.");
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = "Invalid tenant claim." });
            return;
        }

        // 2. Load tenant from Redis cache or DB
        var cacheKey  = $"tenant:{tenantId}:info";
        var tenantInfo = await cache.GetAsync<TenantCacheEntry>(cacheKey);

        if (tenantInfo is null)
        {
            var tenant = await tenantRepository.GetByIdAsync(tenantId);
            if (tenant is null)
            {
                _logger.LogWarning("Request rejected: tenant {TenantId} not found.", tenantId);
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsJsonAsync(new { error = "Tenant not found." });
                return;
            }

            var moduleKeys = await tenantRepository.GetActiveModuleKeysAsync(tenantId);
            tenantInfo = new TenantCacheEntry(
                tenant.Slug,
                tenant.Status.ToString(),
                moduleKeys.ToList());

            await cache.SetAsync(cacheKey, tenantInfo, TimeSpan.FromMinutes(5));
        }

        // 3. Validate status — enforced on EVERY request, whether from cache or DB.
        //    A suspension takes effect within the cache TTL without any manual flush.
        if (!string.Equals(tenantInfo.Status, "Active", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "Request rejected: tenant {TenantId} is {Status}.",
                tenantId, tenantInfo.Status);
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new { error = "Tenant account is suspended." });
            return;
        }

        // 4. Populate ICurrentTenant for this request scope
        currentTenant.Set(tenantId, tenantInfo.Slug, tenantInfo.ActiveModules);

        await _next(context);
    }
}

/// <summary>Serializable cache entry for tenant info.</summary>
internal record TenantCacheEntry(string Slug, string Status, List<string> ActiveModules);
