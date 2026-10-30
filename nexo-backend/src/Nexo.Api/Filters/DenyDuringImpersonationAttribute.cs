using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Nexo.Application.Common.Interfaces;

namespace Nexo.Api.Filters;

/// <summary>
/// Account acts a platform administrator must not do while impersonating a tenant user: anything that issues new tokens
/// (an impersonation must never turn into a normal session), or changes the user's password, two-step verification or
/// sessions. Answered 403 <c>impersonation_forbidden</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class DenyDuringImpersonationAttribute : Attribute, IAsyncActionFilter
{
    public static bool IsImpersonation(System.Security.Claims.ClaimsPrincipal user) => user.HasClaim(ImpersonationClaims.Marker, "1");

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (IsImpersonation(context.HttpContext.User))
        {
            context.Result = new ObjectResult(new
            {
                error = "Indisponível durante o acesso da plataforma a esta empresa.",
                code = "impersonation_forbidden",
                requestId = context.HttpContext.TraceIdentifier,
            }) { StatusCode = StatusCodes.Status403Forbidden };
            return;
        }
        await next();
    }
}
