namespace Nexo.Api.Middleware;

/// <summary>
/// Two lists, because they answer two questions. <see cref="All"/>: who may call this API from a browser. That is the CORS policy and the guard of the auth routes,
/// and it grows with every site of a client (docs/build/client-portal-contract.md). <see cref="AllowsCookie"/>: who may act on the session cookie. That is the
/// check of <see cref="SessionOriginMiddleware"/> and <see cref="BrowserSession"/>, and it is <c>Auth:Session:AllowedOrigins</c> or, without it, the app of the ERP
/// alone. Being able to call the API gives no authority over the session of whoever visits a page. Whoever is on the second list is on the first by itself.
/// localhost only in Development: an origin on the visitor's own machine is no origin of ours anywhere else, and the policy allows credentials.
/// </summary>
public sealed class AllowedOrigins
{
    private const string SessionApp = "https://erp.orken.com.br";
    private static readonly string[] Production = [SessionApp, "https://www.orken.com.br"];
    private static readonly string[] Development = ["http://localhost:3000", "http://localhost:5000", "http://localhost:8080", "http://localhost:5173"];

    private readonly HashSet<string> _origins;
    private readonly HashSet<string> _sessionOrigins;

    public AllowedOrigins(IConfiguration configuration, IHostEnvironment environment)
    {
        var local = environment.IsDevelopment() ? Development : [];
        var forSession = Read(configuration, "Auth:Session:AllowedOrigins");
        _sessionOrigins = ToSet(forSession.Length > 0 ? forSession : [SessionApp, .. local]);
        _origins = ToSet([.. Production, .. local, .. Read(configuration, "Cors:AllowedOrigins"), .. _sessionOrigins]);
    }

    public string[] All => [.. _origins];

    /// <summary>Exactly one of the list. A browser always sends the scheme and the host in lower case.</summary>
    public bool Contains(string? origin) => !string.IsNullOrEmpty(origin) && _origins.Contains(origin);

    public bool AllowsCookie(string? origin) => !string.IsNullOrEmpty(origin) && _sessionOrigins.Contains(origin);

    private static string[] Read(IConfiguration configuration, string key)
    {
        var origins = (configuration.GetSection(key).Get<string[]>() ?? [])
            .Select(origin => origin.Trim().TrimEnd('/'))
            .Where(origin => origin.Length > 0)
            .ToArray();

        // Credentials with a wildcard is what the CORS protocol forbids, and the check of the Origin compares exact values.
        if (origins.Any(origin => origin.Contains('*')))
            throw new InvalidOperationException($"{key} takes exact origins: a wildcard cannot go with credentials.");
        return origins;
    }

    private static HashSet<string> ToSet(IEnumerable<string> origins) => new(origins, StringComparer.OrdinalIgnoreCase);
}
