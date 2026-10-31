using System.Globalization;
using System.Text.Json;
using System.Threading.RateLimiting;

namespace Nexo.Api.Middleware;

/// <summary>
/// The 429 every rate-limiting policy answers with: <c>Retry-After</c> (seconds left in the caller's window) and the same
/// JSON shape as the other API errors, plus <c>retryAfterSeconds</c>.
/// </summary>
public static class RateLimitRejection
{
    public const string Code = "rate_limited";

    /// <summary>Only when a limiter gives no hint (every policy here is a fixed window, which always does).</summary>
    internal const int FallbackSeconds = 60;

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    public static async ValueTask WriteAsync(HttpContext ctx, RateLimitLease lease, CancellationToken ct)
    {
        var seconds = RetryAfterSeconds(lease);
        ctx.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        ctx.Response.Headers.RetryAfter = seconds.ToString(CultureInfo.InvariantCulture);
        ctx.Response.ContentType = "application/json";

        var body = new
        {
            error = "Muitas tentativas. Aguarde um pouco e tente de novo.",
            details = Array.Empty<string>(),
            requestId = ctx.TraceIdentifier,
            code = Code,
            retryAfterSeconds = seconds,
        };
        await ctx.Response.WriteAsync(JsonSerializer.Serialize(body, JsonOpts), ct);
    }

    internal static int RetryAfterSeconds(RateLimitLease lease)
        => lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter)
            ? Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds))
            : FallbackSeconds;
}
