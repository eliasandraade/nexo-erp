using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Nexo.Api.Middleware;
using Nexo.Application.Common.Interfaces;

namespace Nexo.Api.Filters;

/// <summary>
/// Action filter que verifica se o tenant autenticado tem acesso ativo ao módulo.
/// Usa IModuleAccessService (cache em memória + DB como fonte de verdade).
/// Módulo vencido fica somente leitura e módulo que o tenant nunca teve responde 403: ver <see cref="ExpiredModuleGate"/>.
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
            var refusal = ExpiredModuleGate.Decide(context.HttpContext, _currentTenant,
                key => string.Equals(key, _moduleKey, StringComparison.OrdinalIgnoreCase),
                new ObjectResult(new
                {
                    error   = "Module not available",
                    message = $"Your subscription does not include the '{_moduleKey}' module.",
                    module  = _moduleKey,
                })
                {
                    StatusCode = StatusCodes.Status403Forbidden,
                });
            if (refusal is not null)
            {
                context.Result = refusal;
                return;
            }
        }

        await next();
    }
}
