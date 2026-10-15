using Microsoft.AspNetCore.Routing;
using Serilog;
using Serilog.Events;
using Serilog.Templates;

namespace Nexo.Api.Observability;

/// <summary>How requests and events are written to the log.</summary>
public static class RequestLogging
{
    /// <summary>
    /// One JSON object per line with the keys the hosting log viewer reads (<c>time</c>, <c>level</c>
    /// as debug/info/warn/error, <c>message</c>) plus every property (RequestId, Route, TenantId…).
    /// </summary>
    public static ExpressionTemplate JsonFormatter() => new(
        "{ {time: UtcDateTime(@t), " +
        "level: if @l = 'Fatal' or @l = 'Error' then 'error' else if @l = 'Warning' then 'warn' else if @l = 'Information' then 'info' else 'debug', " +
        "message: @m, exception: @x, ..@p} }\n");

    /// <summary>
    /// Request completion lines. Runs OUTSIDE the exception handler, so it logs the status the client
    /// really got (a 404/422 is not a "500"). The path written is the route template
    /// ("api/service/public/bookings/{token}"), never the raw path — that would leak booking tokens,
    /// CNPJs and ids. Ids of tenant/store/user only, never names, e-mails or bodies.
    /// </summary>
    public static IApplicationBuilder UseOrkenRequestLogging(this IApplicationBuilder app)
        => app.UseSerilogRequestLogging(opts =>
        {
            opts.MessageTemplate = "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000}ms";
            opts.GetLevel = Health.HealthEndpoints.RequestLogLevel;
            opts.GetMessageTemplateProperties = (http, _, elapsedMs, statusCode) =>
            [
                new LogEventProperty("RequestMethod", new ScalarValue(http.Request.Method)),
                new LogEventProperty("RequestPath", new ScalarValue(RouteOf(http))),
                new LogEventProperty("StatusCode", new ScalarValue(statusCode)),
                new LogEventProperty("Elapsed", new ScalarValue(elapsedMs)),
            ];
            opts.EnrichDiagnosticContext = (diag, http) =>
            {
                foreach (var (claim, field) in new[] { ("tenantId", "TenantId"), ("storeId", "StoreId"), ("userId", "UserId") })
                    if (http.User.FindFirst(claim)?.Value is { } value) diag.Set(field, value);
            };
        });

    /// <summary>The matched route template, or "unmatched" — never the raw request path.</summary>
    public static string RouteOf(HttpContext http)
        => http.GetEndpoint() is RouteEndpoint route ? "/" + (route.RoutePattern.RawText ?? string.Empty).TrimStart('/') : "unmatched";
}
