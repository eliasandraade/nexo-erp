using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Nexo.Application.Common.Interfaces;

namespace Nexo.Api.Filters;

/// <summary>
/// Action filter que verifica se o tenant autenticado tem acesso ativo ao módulo.
/// Usa IModuleAccessService (cache em memória + DB como fonte de verdade).
/// Retorna 403 se o módulo não estiver ativo.
/// </summary>
public class RequireModuleFilter : IAsyncActionFilter
{
    private readonly string               _moduleKey;
    private readonly IModuleAccessService _moduleAccess;
    private readonly ICurrentTenant       _currentTenant;

    public RequireModuleFilter(
        string               moduleKey,
        IModuleAccessService moduleAccess,
        ICurrentTenant       currentTenant)
    {
        _moduleKey     = moduleKey;
        _moduleAccess  = moduleAccess;
        _currentTenant = currentTenant;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (!_currentTenant.IsResolved)
        {
            context.Result = new ForbidResult();
            return;
        }

        var hasAccess = await _moduleAccess.HasActiveModuleAsync(
            _currentTenant.Id, _moduleKey, context.HttpContext.RequestAborted);

        if (!hasAccess)
        {
            context.Result = new ObjectResult(new
            {
                error   = "Module not available",
                message = $"Your subscription does not include the '{_moduleKey}' module.",
                module  = _moduleKey,
            })
            {
                StatusCode = StatusCodes.Status403Forbidden,
            };
            return;
        }

        await next();
    }
}
