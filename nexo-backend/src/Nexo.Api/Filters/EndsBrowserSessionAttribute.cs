using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Nexo.Api.Middleware;

namespace Nexo.Api.Filters;

/// <summary>
/// The action ends the session of the browser calling it (a new password, two-step verification turned on, every session revoked):
/// when it succeeds in the cookie mode, the cookie goes too. Without this it would stay until it expires, refused by the server.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class EndsBrowserSessionAttribute : Attribute, IResultFilter
{
    public void OnResultExecuting(ResultExecutingContext context)
    {
        if (context.Result is IStatusCodeActionResult { StatusCode: >= 200 and < 300 } && BrowserSession.IsCookieMode(context.HttpContext.Request))
            context.HttpContext.RequestServices.GetRequiredService<BrowserSession>().ExpireCookie(context.HttpContext.Response);
    }

    public void OnResultExecuted(ResultExecutedContext context)
    {
    }
}
