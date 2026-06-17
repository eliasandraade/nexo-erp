using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Nexo.Application.Common.Interfaces;
using Nexo.Domain.Modules.Service;

namespace Nexo.Api.Attributes;

/// <summary>
/// Ensures the authenticated tenant has an active subscription to ANY key in the Service
/// family (decision D1: per-vertical SKUs — clinica-medica, salao-beleza, … — all unlock the
/// single Service engine). Returns 403 Forbidden otherwise.
///
/// Reads the active module keys already resolved onto <see cref="ICurrentTenant"/> by
/// TenantResolutionMiddleware (which runs before authorization filters). This is the
/// family-aware counterpart to <see cref="RequireModuleAttribute"/>; it does not modify the
/// single-key attribute nor re-implement its cache lookup.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public sealed class RequireServiceModuleAttribute : Attribute, IAuthorizationFilter
{
    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var currentTenant = context.HttpContext.RequestServices.GetRequiredService<ICurrentTenant>();

        if (!currentTenant.IsResolved ||
            !currentTenant.ActiveModules.Any(ServicePresetRegistry.IsServiceFamilyKey))
        {
            context.Result = new ForbidResult();
        }
    }
}
