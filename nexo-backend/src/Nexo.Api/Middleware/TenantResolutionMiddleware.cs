using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Nexo.Application.Common.Interfaces;
using Nexo.Infrastructure.Persistence;

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

        // Skip for platform tokens — they have no tenant context
        var tokenType = context.User.FindFirstValue("type");
        if (tokenType == "platform")
        {
            await _next(context);
            return;
        }

        // The caller left: ExceptionHandlingMiddleware treats the cancellation as "nothing failed, nobody to answer".
        var ct = context.RequestAborted;

        // 1. Extract tenant_id and user_id from JWT
        var tenantIdClaim = context.User.FindFirstValue("tenantId");
        var userIdClaim = context.User.FindFirstValue("userId");
        if (!Guid.TryParse(tenantIdClaim, out var tenantId) || !Guid.TryParse(userIdClaim, out var userId))
        {
            _logger.LogWarning("Request rejected: missing or invalid 'tenantId' or 'userId' claim.");
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = "Invalid tenant or user claim." }, ct);
            return;
        }

        // Verify user belongs to the tenant (with caching to avoid N+1).
        // MUST use IgnoreQueryFilters(): the global EF filter requires ICurrentTenant.IsResolved,
        // which is false at this point — we are the middleware that sets it.
        var userCacheKey = $"user:{userId}:info";
        var cachedUser = await cache.GetAsync<UserCacheEntry>(userCacheKey, ct);

        if (cachedUser == null)
        {
            var db = context.RequestServices.GetRequiredService<NexoDbContext>();
            var userInfo = await db.Users
                .IgnoreQueryFilters()
                .Where(u => u.Id == userId)
                .Select(u => new { u.TenantId, u.Status })
                .FirstOrDefaultAsync(ct);

            if (userInfo == null)
            {
                _logger.LogWarning("Request rejected: user {UserId} not found.", userId);
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsJsonAsync(new { error = "User not found." }, ct);
                return;
            }
            if (userInfo.TenantId != tenantId)
            {
                _logger.LogWarning("Request rejected: user {UserId} does not belong to tenant {TenantId}.", userId, tenantId);
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsJsonAsync(new { error = "Tenant mismatch." }, ct);
                return;
            }
            if (userInfo.Status != Domain.Enums.UserStatus.Active)
            {
                _logger.LogWarning("Request rejected: user {UserId} is {Status}.", userId, userInfo.Status);
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsJsonAsync(new { error = "User account is inactive." }, ct);
                return;
            }

            cachedUser = new UserCacheEntry(userInfo.TenantId, userInfo.Status.ToString());
            await cache.SetAsync(userCacheKey, cachedUser, TimeSpan.FromMinutes(5), ct);
        }
        else
        {
            if (cachedUser.TenantId != tenantId)
            {
                _logger.LogWarning("Request rejected: user {UserId} does not belong to tenant {TenantId}.", userId, tenantId);
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsJsonAsync(new { error = "Tenant mismatch." }, ct);
                return;
            }
            if (cachedUser.Status != "Active")
            {
                _logger.LogWarning("Request rejected: user {UserId} is {Status}.", userId, cachedUser.Status);
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsJsonAsync(new { error = "User account is inactive." }, ct);
                return;
            }
        }

        // 2. Load tenant from Redis cache or DB
        var cacheKey  = $"tenant:{tenantId}:info";
        var tenantInfo = await cache.GetAsync<TenantCacheEntry>(cacheKey, ct);

        // An entry written before ExpiredModules existed, still in Redis during a deploy, is read again.
        if (tenantInfo?.ExpiredModules is null)
        {
            var tenant = await tenantRepository.GetByIdAsync(tenantId, ct);
            if (tenant is null)
            {
                _logger.LogWarning("Request rejected: tenant {TenantId} not found.", tenantId);
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsJsonAsync(new { error = "Tenant not found." }, ct);
                return;
            }

            var moduleKeys = await tenantRepository.GetActiveModuleKeysAsync(tenantId, ct);
            var expiredKeys = await tenantRepository.GetExpiredModuleKeysAsync(tenantId, ct);
            tenantInfo = new TenantCacheEntry(
                tenant.Slug,
                tenant.Status.ToString(),
                moduleKeys.ToList(),
                expiredKeys.ToList());

            await cache.SetAsync(cacheKey, tenantInfo, TimeSpan.FromMinutes(5), ct);
        }

        // 3. Validate status — enforced on EVERY request, whether from cache or DB.
        //    A suspension takes effect within the cache TTL without any manual flush.
        if (!string.Equals(tenantInfo.Status, "Active", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "Request rejected: tenant {TenantId} is {Status}.",
                tenantId, tenantInfo.Status);
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new { error = "Tenant account is suspended." }, ct);
            return;
        }

        // 4. Populate ICurrentTenant for this request scope
        currentTenant.Set(tenantId, tenantInfo.Slug, tenantInfo.ActiveModules, tenantInfo.ExpiredModules);

        await _next(context);
    }
}

/// <summary>Serializable cache entry for tenant info.</summary>
internal record TenantCacheEntry(string Slug, string Status, List<string> ActiveModules, List<string>? ExpiredModules = null);

/// <summary>Serializable cache entry for user info.</summary>
internal record UserCacheEntry(Guid TenantId, string Status);
