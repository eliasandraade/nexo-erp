using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Nexo.Application.Common.Interfaces;
using Nexo.Domain.Modules.Service;

namespace Nexo.Api.Attributes;

/// <summary>
/// Ensures the authenticated tenant has an active subscription to ANY key in the Service
/// family (decision D1: per-vertical SKUs — clinica-medica, salao-beleza, … — all unlock
/// the single Service engine). Returns 403 Forbidden otherwise.
///
/// Family-aware counterpart to <see cref="RequireModuleAttribute"/>; it does NOT modify the
/// single-key attribute. Uses the same cached tenant-modules list to avoid repeated queries.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public sealed class RequireServiceModuleAttribute : Attribute, IAsyncAuthorizationFilter
{
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

        // Reuse the cache key populated by TenantResolutionMiddleware / RequireModuleAttribute.
        var cacheKey = $"tenant:{currentTenant.Id}:modules";
        var cachedModules = await cache.GetAsync<List<string>>(cacheKey);

        if (cachedModules == null)
        {
            cachedModules = (await tenantRepository.GetActiveModuleKeysAsync(currentTenant.Id)).ToList();
            await cache.SetAsync(cacheKey, cachedModules, TimeSpan.FromMinutes(5));
        }

        if (!cachedModules.Any(ServicePresetRegistry.IsServiceFamilyKey))
        {
            context.Result = new ForbidResult();
        }
    }
}
