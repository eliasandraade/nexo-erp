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
            : TooManyAttempts;
        await ctx.Response.WriteAsync(JsonSerializer.Serialize(Body(ctx, error, Code, seconds), JsonOpts), ct);
    }

    /// <summary>The same 429 for a refusal outside the limiter's own pipeline (an upload over its in-flight bound).</summary>
    public static ObjectResult Refuse(HttpContext ctx, string error, string code, int seconds)
    {
        ctx.Response.Headers.RetryAfter = seconds.ToString(CultureInfo.InvariantCulture);
        return new ObjectResult(Body(ctx, error, code, seconds)) { StatusCode = StatusCodes.Status429TooManyRequests };
    }

    /// <summary>The same 429, from a budget counted inside an endpoint instead of a limiter in front of it.</summary>
    public static ObjectResult OverBudgetResult(ControllerBase controller, TimeSpan retryAfter)
        => Answer(controller, StatusCodes.Status429TooManyRequests, TooManyAttempts, Code, retryAfter);

    /// <summary>A renewal the server could not decide (503): the session is not in question, and the client comes back.</summary>
    public static ObjectResult RenewalUnavailableResult(ControllerBase controller, TimeSpan retryAfter)
        => Answer(controller, StatusCodes.Status503ServiceUnavailable,
            "Não foi possível renovar a sessão agora. Ela continua ativa: tente de novo em instantes.",
            Nexo.Application.Features.Auth.RefreshErrors.Unavailable, retryAfter);

    /// <summary>A token another tab has just rotated (409): nothing changed in the session, and the server judges this token when the time given is over.</summary>
    public static ObjectResult AlreadyRotatedResult(ControllerBase controller, TimeSpan retryAfter)
        => Answer(controller, StatusCodes.Status409Conflict, "Esta sessão acabou de ser renovada em outra aba.",
            Nexo.Application.Features.Auth.RefreshErrors.AlreadyRotated, retryAfter);

    private static ObjectResult Answer(ControllerBase controller, int status, string error, string code, TimeSpan retryAfter)
    {
        var seconds = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds));
        controller.Response.Headers.RetryAfter = seconds.ToString(CultureInfo.InvariantCulture);
        return controller.StatusCode(status, Body(controller.HttpContext, error, code, seconds));
    }

    private const string TooManyAttempts = "Muitas tentativas. Aguarde um pouco e tente de novo.";

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
