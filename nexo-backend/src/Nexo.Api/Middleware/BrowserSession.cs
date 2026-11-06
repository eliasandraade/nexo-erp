namespace Nexo.Api.Middleware;

/// <summary>
/// Where the refresh token of a browser session travels, decided per request (docs/security/sessao-auditoria-e-migracao.md, section 3).
/// With <c>X-Orken-Session: cookie</c>, in the cookie <see cref="CookieName"/> and never in a response body. Without the header, in the
/// body, and the cookie has no authority at all: it is not read, renewed or ended.
/// </summary>
public sealed class BrowserSession
{
    public const string ModeHeader = "X-Orken-Session";
    public const string ModeValue = "cookie";

    /// <summary><c>__Host-</c>: no other subdomain of orken.com.br can write over it, and a browser only takes it Secure, on Path=/ and with no Domain.</summary>
    public const string CookieName = "__Host-nexo_rt";

    /// <summary>Cookies of earlier versions. Nothing sets or reads them; a sign-out still removes what a browser may have kept.</summary>
    private static readonly string[] OldCookies = ["nexo_access", "nexo_refresh"];

    private readonly AllowedOrigins _origins;
    private readonly SameSiteMode _sameSite;

    public BrowserSession(IConfiguration configuration, AllowedOrigins origins)
    {
        _origins = origins;
        _sameSite = ParseSameSite(configuration["Auth:Session:CookieSameSite"]);
    }

    public static bool IsCookieMode(HttpRequest request)
        => request.Headers.TryGetValue(ModeHeader, out var mode) && mode.Count == 1 && mode[0] == ModeValue;

    /// <summary>
    /// The token of the cookie. Only in the cookie mode and only from a listed Origin: <see cref="SessionOriginMiddleware"/> already
    /// answered anything else, and this is the one place that reads the cookie, so it holds even if the pipeline ever changes.
    /// </summary>
    public string? ReadCookie(HttpRequest request)
        => IsCookieMode(request) && _origins.Contains(request.Headers.Origin.ToString()) ? request.Cookies[CookieName] : null;

    /// <summary>A token in the body wins: that is how a session still in localStorage migrates to the cookie.</summary>
    public string? PresentedToken(HttpRequest request, string? bodyToken)
        => string.IsNullOrWhiteSpace(bodyToken) ? ReadCookie(request) : bodyToken;

    public void SetCookie(HttpResponse response, string refreshToken, DateTime expiresAt)
        => response.Cookies.Append(CookieName, refreshToken, Options(new DateTimeOffset(DateTime.SpecifyKind(expiresAt, DateTimeKind.Utc))));

    /// <summary>A browser only takes the removal of a <c>__Host-</c> cookie with the same attributes, the SameSite of a cross-site response included.</summary>
    public void ExpireCookie(HttpResponse response)
        => response.Cookies.Append(CookieName, "", Options(DateTimeOffset.UtcNow.AddDays(-1)));

    public void ExpireOldCookies(HttpResponse response)
    {
        foreach (var name in OldCookies)
            response.Cookies.Append(name, "", Options(DateTimeOffset.UtcNow.AddDays(-1)));
    }

    // Secure whatever the scheme of the request: TLS ends at the proxy, so this API always sees plain HTTP.
    private CookieOptions Options(DateTimeOffset expires) => new()
    {
        HttpOnly = true,
        Secure = true,
        SameSite = _sameSite,
        Path = "/",
        Expires = expires,
    };

    private static SameSiteMode ParseSameSite(string? setting)
    {
        if (string.IsNullOrWhiteSpace(setting)) return SameSiteMode.Lax;
        return setting.Trim().ToLowerInvariant() switch
        {
            "lax" => SameSiteMode.Lax,
            "strict" => SameSiteMode.Strict,
            "none" => SameSiteMode.None,
            _ => throw new InvalidOperationException($"Auth:Session:CookieSameSite must be Lax, Strict or None, not '{setting}'."),
        };
    }
}
