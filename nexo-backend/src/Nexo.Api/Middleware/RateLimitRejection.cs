using System.Globalization;
using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Nexo.Api.Uploads;

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

        var error = ctx.GetEndpoint()?.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName == UploadAdmission.RateLimitPolicy
            ? $"Muitos envios em pouco tempo. Aguarde {seconds} segundos e tente de novo."
            : "Muitas tentativas. Aguarde um pouco e tente de novo.";
        await ctx.Response.WriteAsync(JsonSerializer.Serialize(Body(ctx, error, Code, seconds), JsonOpts), ct);
    }

    /// <summary>The same 429 for a refusal outside the limiter's own pipeline (an upload over its in-flight bound).</summary>
    public static ObjectResult Refuse(HttpContext ctx, string error, string code, int seconds)
    {
        ctx.Response.Headers.RetryAfter = seconds.ToString(CultureInfo.InvariantCulture);
        return new ObjectResult(Body(ctx, error, code, seconds)) { StatusCode = StatusCodes.Status429TooManyRequests };
    }

    private static object Body(HttpContext ctx, string error, string code, int seconds) => new
    {
        error,
        details = Array.Empty<string>(),
        requestId = ctx.TraceIdentifier,
        code,
        retryAfterSeconds = seconds,
    };

    internal static int RetryAfterSeconds(RateLimitLease lease)
        => lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter)
            ? Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds))
            : FallbackSeconds;
}
