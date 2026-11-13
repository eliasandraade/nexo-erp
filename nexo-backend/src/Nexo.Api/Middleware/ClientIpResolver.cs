using System.Net;

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
/// The one place that derives the client IP used to partition the sign-in and renewal limits, the Build client-portal rate
/// limits and the per-client password lockout.
///
/// Why the first <c>X-Forwarded-For</c> entry by default: the API runs behind Railway's edge proxy, which (per Railway staff
/// guidance) drops any <c>X-Forwarded-For</c> the client sends and writes the connecting IP as the first entry. A spoofed
/// header therefore never reaches the app, and the first entry is the real client. If the edge ever stops doing that (or
/// the API moves behind another proxy), ops switch <c>RateLimiting:ClientIpSource</c> to <c>RealIp</c> (<c>X-Real-IP</c>
/// set by the proxy) or <c>Remote</c> (the TCP peer) without a code change. An unknown value falls back to the default.
///
/// What comes out partitions the limits and becomes a Redis key, so a header only counts if it holds an address, and it comes out in one spelling. Anything else
/// (a long zone id, garbage) is the connection address: it never buys a partition or a key of its own.
/// </summary>
public sealed class ClientIpResolver
{
    public const string ConfigKey = "RateLimiting:ClientIpSource";
    public const string Unknown = "unknown";

    /// <summary>The longest text of an address: an IPv6 ending in an IPv4.</summary>
    private const int MaxAddressLength = 45;

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
        return source switch
        {
            ClientIpSource.ForwardedFor => ParsedAddress(context.Request.Headers["X-Forwarded-For"].FirstOrDefault()?.Split(',')[0]) ?? remote,
            ClientIpSource.RealIp => ParsedAddress(context.Request.Headers["X-Real-IP"].FirstOrDefault()) ?? remote,
            _ => remote,
        };
    }

    private static string? ParsedAddress(string? value)
    {
        var text = value?.Trim();
        if (string.IsNullOrEmpty(text) || text.Length > MaxAddressLength || text.Contains('%') || !IPAddress.TryParse(text, out var address)) return null;
        return (address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address).ToString();
    }
}
