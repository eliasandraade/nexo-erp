namespace Nexo.Api.Middleware;

/// <summary>Where the client IP of a request is read from (<c>RateLimiting:ClientIpSource</c>).</summary>
public enum ClientIpSource
{
    /// <summary>First entry of <c>X-Forwarded-For</c>, else the connection address (default).</summary>
    ForwardedFor,
    /// <summary><c>X-Real-IP</c>, else the connection address.</summary>
    RealIp,
    /// <summary>The connection address only (no proxy in front, or a proxy that cannot be trusted).</summary>
    Remote,
}

/// <summary>
/// The one place that derives the client IP used to partition the Build client-portal rate limits and the per-client
/// password lockout.
///
/// Why the first <c>X-Forwarded-For</c> entry by default: the API runs behind Railway's edge proxy, which (per Railway staff
/// guidance) drops any <c>X-Forwarded-For</c> the client sends and writes the connecting IP as the first entry. A spoofed
/// header therefore never reaches the app, and the first entry is the real client. If the edge ever stops doing that (or
/// the API moves behind another proxy), ops switch <c>RateLimiting:ClientIpSource</c> to <c>RealIp</c> (<c>X-Real-IP</c>
/// set by the proxy) or <c>Remote</c> (the TCP peer) without a code change. An unknown value falls back to the default.
/// </summary>
public sealed class ClientIpResolver
{
    public const string ConfigKey = "RateLimiting:ClientIpSource";
    public const string Unknown = "unknown";

    public ClientIpResolver(IConfiguration config) => Source = Parse(config[ConfigKey]);

    public ClientIpSource Source { get; }

    public static ClientIpSource Parse(string? value)
        => !string.IsNullOrWhiteSpace(value) && Enum.TryParse<ClientIpSource>(value.Trim(), ignoreCase: true, out var source)
           && Enum.IsDefined(source) && !int.TryParse(value, out _)
            ? source
            : ClientIpSource.ForwardedFor;

    public string Resolve(HttpContext context) => Resolve(context, Source);

    public static string Resolve(HttpContext context, ClientIpSource source)
    {
        var remote = context.Connection.RemoteIpAddress?.ToString() ?? Unknown;
        switch (source)
        {
            case ClientIpSource.ForwardedFor:
                var forwardedFor = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
                var first = forwardedFor?.Split(',')[0].Trim();
                return string.IsNullOrEmpty(first) ? remote : first;
            case ClientIpSource.RealIp:
                var realIp = context.Request.Headers["X-Real-IP"].FirstOrDefault()?.Trim();
                return string.IsNullOrEmpty(realIp) ? remote : realIp;
            default:
                return remote;
        }
    }
}
