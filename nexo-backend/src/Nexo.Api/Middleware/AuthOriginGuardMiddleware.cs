namespace Nexo.Api.Middleware;

/// <summary>
/// A page of any site can send these routes a simple POST with no preflight (<c>mode: "no-cors"</c>, <c>text/plain</c>). It cannot read the answer and
/// does not need to: the limiters count the request before MVC refuses the content type, so one open tab spends the renewal and sign-in budgets
/// of everyone behind that person's address. A browser always says where such a request came from, so it is answered here, before the limiters,
/// when that place is not ours. With no Origin the caller is not a browser page, and it goes on as before.
/// </summary>
public sealed class AuthOriginGuardMiddleware
{
    public const string RefusedCode = "origin_not_allowed";

    private readonly RequestDelegate _next;
    private readonly AllowedOrigins _origins;

    public AuthOriginGuardMiddleware(RequestDelegate next, AllowedOrigins origins)
    {
        _next = next;
        _origins = origins;
    }

    public static bool IsAuthRoute(PathString path)
        => path.StartsWithSegments("/api/auth", StringComparison.OrdinalIgnoreCase)
           || path.StartsWithSegments("/api/platform/auth", StringComparison.OrdinalIgnoreCase);

    public async Task InvokeAsync(HttpContext context)
    {
        if (IsAuthRoute(context.Request.Path)
            && context.Request.Headers.TryGetValue("Origin", out var origin)
            && !_origins.Contains(origin.ToString()))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new { error = "Origem não permitida.", code = RefusedCode }, context.RequestAborted);
            return;
        }
        await _next(context);
    }
}
