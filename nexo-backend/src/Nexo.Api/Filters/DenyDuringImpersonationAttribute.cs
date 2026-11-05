using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Nexo.Application.Common.Interfaces;

namespace Nexo.Api.Filters;

/// <summary>
/// Acts a platform administrator must not do while impersonating a tenant user: anything that issues new tokens
/// (an impersonation must never turn into a normal session), changes the user's password, two-step verification or
/// sessions, touches the tenant's contract with Stripe, or records a decision in the owner's name (sealed tax decisions, the
/// tax policies and the credit ledger, a change order that moves the contract value). Answered 403 <c>impersonation_forbidden</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class DenyDuringImpersonationAttribute : Attribute, IAsyncActionFilter
{
    public static bool IsImpersonation(System.Security.Claims.ClaimsPrincipal user) => user.HasClaim(ImpersonationClaims.Marker, "1");

    /// <summary>The refusal, also for an action that closes only part of what it does (the ledger movements of the credit route).</summary>
    public static ObjectResult Refusal(HttpContext context) => new(new
    {
        error = "Indisponível durante o acesso da plataforma a esta empresa.",
        code = "impersonation_forbidden",
        requestId = context.TraceIdentifier,
    }) { StatusCode = StatusCodes.Status403Forbidden };

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (IsImpersonation(context.HttpContext.User))
        {
            context.Result = Refusal(context.HttpContext);
            return;
        }
        await next();
    }
}
