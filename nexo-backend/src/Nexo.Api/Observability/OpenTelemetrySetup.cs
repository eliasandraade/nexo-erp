using System.Globalization;
using Nexo.Infrastructure.Observability;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Nexo.Api.Observability;

/// <summary>
/// Metrics and selective tracing through OpenTelemetry — on only when an OTLP destination is set
/// (<c>OTEL_EXPORTER_OTLP_ENDPOINT</c>, plus headers/protocol by the standard OTEL_* variables).
/// Without it nothing is collected or sent anywhere.
/// <list type="bullet">
/// <item>Metrics: HTTP server and client, .NET runtime and the API's own meter (<see cref="OrkenMetrics"/>).</item>
/// <item>Traces: a sample of requests (<c>Observability:TraceSampleRatio</c>, default 10%), following
/// the caller's decision when it sends one; health probes are never traced. Query strings are
/// redacted by the instrumentation.</item>
/// </list>
/// </summary>
public static class OpenTelemetrySetup
{
    public const string ServiceName = "orken-api";

    public static IServiceCollection AddOrkenTelemetry(this IServiceCollection services, IConfiguration config, IHostEnvironment env)
    {
        if (string.IsNullOrWhiteSpace(config["OTEL_EXPORTER_OTLP_ENDPOINT"]) || env.IsEnvironment("Testing"))
            return services;

        var ratio = double.TryParse(config["Observability:TraceSampleRatio"], NumberStyles.Float, CultureInfo.InvariantCulture, out var r)
            ? Math.Clamp(r, 0, 1)
            : 0.1;

        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(ServiceName))
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation()
                .AddMeter(OrkenMetrics.MeterName)
                .AddOtlpExporter())
            .WithTracing(tracing => tracing
                .SetSampler(new ParentBasedSampler(new TraceIdRatioBasedSampler(ratio)))
                .AddAspNetCoreInstrumentation(o => o.Filter = http => !http.Request.Path.StartsWithSegments("/health"))
                .AddHttpClientInstrumentation()
                .AddOtlpExporter());

        return services;
    }
}
