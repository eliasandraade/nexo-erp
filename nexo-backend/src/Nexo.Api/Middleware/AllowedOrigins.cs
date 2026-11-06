namespace Nexo.Api.Middleware;

/// <summary>
/// The origins a browser may call this API from: one list, read by the CORS policy and by <see cref="SessionOriginMiddleware"/>,
/// so the two can never disagree. The apps in production plus <c>Cors:AllowedOrigins</c>. localhost only in Development: an origin
/// on the visitor's own machine is no origin of ours anywhere else, and the policy allows credentials.
/// </summary>
public sealed class AllowedOrigins
{
    private static readonly string[] Production = ["https://erp.orken.com.br", "https://www.orken.com.br"];
    private static readonly string[] Development = ["http://localhost:3000", "http://localhost:8080", "http://localhost:5173"];

    private readonly HashSet<string> _origins;

    public AllowedOrigins(IConfiguration configuration, IHostEnvironment environment)
    {
        var configured = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
        var origins = Production
            .Concat(environment.IsDevelopment() ? Development : [])
            .Concat(configured.Select(origin => origin.Trim().TrimEnd('/')));
        _origins = new HashSet<string>(origins.Where(origin => origin.Length > 0), StringComparer.OrdinalIgnoreCase);

        // Credentials with a wildcard is what the CORS protocol forbids, and the check of the Origin compares exact values.
        if (_origins.Any(origin => origin.Contains('*')))
            throw new InvalidOperationException("Cors:AllowedOrigins takes exact origins: a wildcard cannot go with credentials.");
    }

    public string[] All => [.. _origins];

    /// <summary>Exactly one of the list. A browser always sends the scheme and the host in lower case.</summary>
    public bool Contains(string? origin) => !string.IsNullOrEmpty(origin) && _origins.Contains(origin);
}
