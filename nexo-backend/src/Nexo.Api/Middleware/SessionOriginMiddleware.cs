namespace Nexo.Api.Middleware;

/// <summary>
/// The second barrier against CSRF in the cookie mode: a request with <c>X-Orken-Session: cookie</c> from an Origin out of the list of the
/// session (<see cref="AllowedOrigins.AllowsCookie"/>, not the wider one of CORS), or with no Origin at all, is answered 403 here. It comes before authentication and the rate limiters, so such a request reads no
/// cookie, checks no password, spends no permit and changes nothing. SameSite does not cover this: every *.orken.com.br is the same
/// site. A request without the header is not looked at, and it has no authority over the cookie either.
/// </summary>
public sealed class SessionOriginMiddleware
{
    public const string RefusedCode = "origin_not_allowed";

    private readonly RequestDelegate _next;
    private readonly AllowedOrigins _origins;

    public SessionOriginMiddleware(RequestDelegate next, AllowedOrigins origins)
    {
        _next = next;
        _origins = origins;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (BrowserSession.IsCookieMode(context.Request) && !_origins.AllowsCookie(context.Request.Headers.Origin.ToString()))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new { error = "Origem não permitida.", code = RefusedCode }, context.RequestAborted);
            return;
        }
        await _next(context);
    }
}
