using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Nexo.Application.Common.Interfaces;

namespace Nexo.Api.Attributes;

/// <summary>
/// Ensures the authenticated tenant has an active subscription for the specified module.
/// Returns 403 Forbidden if the module is not active.
/// Uses caching to avoid repeated database queries.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public class RequireModuleAttribute : Attribute, IAsyncAuthorizationFilter
{
    private readonly string _moduleKey;

    public RequireModuleAttribute(string moduleKey)
    {
        _moduleKey = moduleKey;
    }

    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var currentTenant = context.HttpContext.RequestServices.GetRequiredService<ICurrentTenant>();
        var cache = context.HttpContext.RequestServices.GetRequiredService<ICacheService>();
        var tenantRepository = context.HttpContext.RequestServices.GetRequiredService<ITenantRepository>();

        if (!currentTenant.IsResolved)
        {
            context.Result = new ForbidResult();
            return;
        }

        // Try to get active modules from cache (already set by TenantResolutionMiddleware)
        // If not present, fetch and cache
        var cacheKey = $"tenant:{currentTenant.Id}:modules";
        var cachedModules = await cache.GetAsync<List<string>>(cacheKey);
        
        if (cachedModules == null)
        {
            cachedModules = (await tenantRepository.GetActiveModuleKeysAsync(currentTenant.Id)).ToList();
            await cache.SetAsync(cacheKey, cachedModules, TimeSpan.FromMinutes(5));
        }
        
        if (!cachedModules.Contains(_moduleKey))
        {
            context.Result = new ForbidResult();
        }
    }
}