using Serilog.Core;
using Serilog.Events;

namespace Nexo.Api.Observability;

/// <summary>
/// Puts our request id (the X-Request-Id, kept in <see cref="HttpContext.TraceIdentifier"/>) on every
/// log event of the request. ASP.NET Core adds its own "RequestId" (Kestrel's connection id) to each
/// event before Serilog's LogContext is applied, and a property already present is not replaced — so
/// pushing it in the middleware is not enough: this enricher overwrites it.
/// </summary>
public sealed class RequestIdEnricher : ILogEventEnricher
{
    private readonly IHttpContextAccessor _http;

    public RequestIdEnricher(IHttpContextAccessor http) => _http = http;

    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        var id = _http.HttpContext?.TraceIdentifier;
        if (!string.IsNullOrEmpty(id))
            logEvent.AddOrUpdateProperty(propertyFactory.CreateProperty("RequestId", id));
    }
}
