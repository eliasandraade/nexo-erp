using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Nexo.Infrastructure.Health;

namespace Nexo.Api.Health;

/// <summary>
/// Health endpoints (PR 68):
/// <list type="bullet">
/// <item><c>/health/live</c> — the process answers. No dependency is checked: a database or cache
/// outage must not get a healthy process restarted.</item>
/// <item><c>/health/ready</c> — may this instance take traffic. 503 only when PostgreSQL is down;
/// Redis and storage outages show as Degraded (200).</item>
/// <item><c>/health</c> — the full report (every check), same status codes as ready.</item>
/// </list>
/// Bodies carry status, check names and short fixed descriptions — never exception messages,
/// hosts or connection strings.
/// </summary>
public static class HealthEndpoints
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static readonly Dictionary<HealthStatus, int> ResultCodes = new()
    {
        [HealthStatus.Healthy]   = StatusCodes.Status200OK,
        [HealthStatus.Degraded]  = StatusCodes.Status200OK,
        [HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable,
    };

    /// <summary>Request log level: errors stay errors; successful health probes are not logged.</summary>
    public static Serilog.Events.LogEventLevel RequestLogLevel(HttpContext http, double elapsedMs, Exception? ex)
    {
        if (ex is not null || http.Response.StatusCode >= StatusCodes.Status500InternalServerError)
            return Serilog.Events.LogEventLevel.Error;
        if (http.Request.Path.StartsWithSegments("/health"))
            return Serilog.Events.LogEventLevel.Verbose;
        return Serilog.Events.LogEventLevel.Information;
    }

    public static void MapOrkenHealth(this WebApplication app)
    {
        app.MapHealthChecks("/health/live", Options(_ => false));
        app.MapHealthChecks("/health/ready", Options(c => c.Tags.Contains(HealthTags.Ready)));
        app.MapHealthChecks("/health", Options(_ => true));
    }

    private static HealthCheckOptions Options(Func<HealthCheckRegistration, bool> predicate) => new()
    {
        Predicate        = predicate,
        ResultStatusCodes = ResultCodes,
        ResponseWriter   = WriteAsync,
    };

    private static Task WriteAsync(HttpContext ctx, HealthReport report)
    {
        ctx.Response.ContentType = "application/json; charset=utf-8";
        ctx.Response.Headers.CacheControl = "no-store";
        var body = new
        {
            status = report.Status.ToString(),
            totalMs = Math.Round(report.TotalDuration.TotalMilliseconds),
            checks = report.Entries.Select(e => new
            {
                name = e.Key,
                status = e.Value.Status.ToString(),
                // a check that threw has the exception's text as its description — never published
                description = e.Value.Exception is null ? e.Value.Description : "check failed",
                durationMs = Math.Round(e.Value.Duration.TotalMilliseconds),
            }),
        };
        return ctx.Response.WriteAsync(JsonSerializer.Serialize(body, Json), ctx.RequestAborted);
    }
}
