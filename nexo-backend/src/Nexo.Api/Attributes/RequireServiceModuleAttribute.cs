using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Nexo.Application.Common.Interfaces;
using Nexo.Domain.Modules.Service;

namespace Nexo.Api.Attributes;

/// <summary>
/// Ensures the authenticated tenant is entitled to the Service engine: the single commercial
/// module "service". Returns 403 Forbidden otherwise. Retired per-vertical keys are rewritten
/// to "service" by the ConvertLegacyServiceSubscriptions migration, so no fallback is needed
/// here. The internal preset (vertical "ramo") is configured separately via SvcSettings, NOT
/// via the module key.
///
/// Reads the active module keys already resolved onto <see cref="ICurrentTenant"/> by
/// TenantResolutionMiddleware (which runs before authorization filters); it does not re-implement
/// the single-key attribute's cache lookup.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public sealed class RequireServiceModuleAttribute : Attribute, IAuthorizationFilter
{
    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var currentTenant = context.HttpContext.RequestServices.GetRequiredService<ICurrentTenant>();

        if (!currentTenant.IsResolved ||
            !currentTenant.ActiveModules.Any(ServicePresetRegistry.IsServiceEntitlement))
        {
            context.Result = new ForbidResult();
        }
    }
}
