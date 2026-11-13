namespace Nexo.Api.Middleware;

/// <summary>
/// The part of a limit's key that says which route was called.
///
/// Never the address as the caller wrote it: routing ignores letter case and a trailing slash, so <c>/api/auth/Login</c>,
/// <c>/API/auth/login</c> and <c>/api/auth/login/</c> all reach the same action, and a key built from the written address gave
/// each spelling a budget of its own. The key is the route the request was matched to, as the code declares it, in one spelling.
/// </summary>
public static class RateLimitKeys
{
    public static string Route(HttpContext context)
    {
        if (context.GetEndpoint() is RouteEndpoint { RoutePattern.RawText: { Length: > 0 } template })
            return Normalize(template);

        // No matched route (a limit applied outside routing): the written address, in one spelling.
        return Normalize(context.Request.Path.Value ?? string.Empty);
    }

    private static string Normalize(string route) => route.Trim('/').ToLowerInvariant();
}
