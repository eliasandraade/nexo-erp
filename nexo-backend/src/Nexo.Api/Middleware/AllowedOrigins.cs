namespace Nexo.Api.Middleware;

/// <summary>
/// The origins a browser may call this API from: the apps in production plus <c>Cors:AllowedOrigins</c>. Read by the CORS policy and by
/// <see cref="AuthOriginGuardMiddleware"/>, so the two can never disagree.
/// </summary>
public sealed class AllowedOrigins
{
    private static readonly string[] BuiltIn =
    [
        "http://localhost:3000",
        "http://localhost:8080",
        "http://localhost:5173",
        "https://erp.orken.com.br",
        "https://www.orken.com.br",
    ];

    private readonly HashSet<string> _origins;

    public AllowedOrigins(IConfiguration configuration)
    {
        var configured = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
        _origins = new HashSet<string>(BuiltIn.Concat(configured), StringComparer.OrdinalIgnoreCase);
    }

    public string[] All => [.. _origins];

    /// <summary>Exactly one of the list. A browser always sends the scheme and the host in lower case.</summary>
    public bool Contains(string? origin) => !string.IsNullOrEmpty(origin) && _origins.Contains(origin);
}
