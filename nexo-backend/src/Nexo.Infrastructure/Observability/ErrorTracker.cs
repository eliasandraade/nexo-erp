using System.Diagnostics.Metrics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Nexo.Application.Common.Observability;
using Nexo.Infrastructure.Persistence;

namespace Nexo.Infrastructure.Observability;

/// <summary>
/// Stores unexpected errors in <c>error_events</c>, one row per kind (upsert on the fingerprint:
/// count + 1, last seen, last request id). Runs in its own scope — the request's DbContext may be
/// the thing that broke — with a short timeout, and never throws: tracking must not turn one
/// failure into two. Also counts them on the <c>orken.errors.unhandled</c> metric.
/// </summary>
public sealed class ErrorTracker : IErrorTracker
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(2);

    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<ErrorTracker> _logger;
    private readonly Counter<long> _unhandled;

    public ErrorTracker(IServiceScopeFactory scopes, ILogger<ErrorTracker> logger, OrkenMetrics metrics)
    {
        _scopes    = scopes;
        _logger    = logger;
        _unhandled = metrics.UnhandledErrors;
    }

    public async Task TrackAsync(ErrorOccurrence o, CancellationToken ct = default)
    {
        _unhandled.Add(1, new KeyValuePair<string, object?>("route", o.Route));
        try
        {
            var fingerprint = ErrorFingerprint.Compute(o.Exception, o.Route);
            var type = Truncate(o.Exception.GetType().FullName ?? o.Exception.GetType().Name, 200);
            var summary = ErrorFingerprint.Summarize(o.Exception);
            var stack = ErrorFingerprint.StackTop(o.Exception);
            var route = Truncate(o.Route, 200);
            var method = Truncate(o.Method, 10);
            var now = DateTime.UtcNow;

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(Timeout);
            await using var scope = _scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO nexo.error_events
                    (id, fingerprint, exception_type, summary, route, method, first_seen_at, last_seen_at, count,
                     last_request_id, last_tenant_id, stack_top, created_at, updated_at)
                VALUES ({Guid.NewGuid()}, {fingerprint}, {type}, {summary}, {route}, {method}, {now}, {now}, 1,
                        {o.RequestId}, {o.TenantId}, {stack}, {now}, {now})
                ON CONFLICT (fingerprint) DO UPDATE SET
                    count = nexo.error_events.count + 1,
                    last_seen_at = EXCLUDED.last_seen_at,
                    last_request_id = EXCLUDED.last_request_id,
                    last_tenant_id = EXCLUDED.last_tenant_id,
                    summary = EXCLUDED.summary,
                    stack_top = EXCLUDED.stack_top,
                    updated_at = EXCLUDED.updated_at
                """, cts.Token);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error tracking failed; the error itself was logged above.");
        }
    }

    private static string Truncate(string v, int max) => v.Length <= max ? v : v[..max];
}

/// <summary>The API's own metrics (exported through OpenTelemetry when it is configured).</summary>
public sealed class OrkenMetrics
{
    public const string MeterName = "Orken.Api";

    public OrkenMetrics(IMeterFactory meters)
    {
        var meter = meters.Create(MeterName);
        UnhandledErrors = meter.CreateCounter<long>("orken.errors.unhandled", description: "Unexpected errors answered with HTTP 500.");
    }

    public Counter<long> UnhandledErrors { get; }
}
