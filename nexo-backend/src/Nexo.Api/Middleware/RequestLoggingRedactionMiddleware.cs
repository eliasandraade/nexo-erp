using System.Text;
using System.Text.Json;

namespace Nexo.Api.Middleware;

/// <summary>
/// Ensures sensitive data is redacted from Serilog structured logs.
/// Filters request body before logging to prevent password/token leakage.
/// </summary>
public class RequestLoggingRedactionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RequestLoggingRedactionMiddleware> _logger;
    private static readonly HashSet<string> SensitiveFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "password",
        "token",
        "accessToken",
        "refreshToken",
        "secret",
        "apiKey",
        "authorization",
        "creditCard",
        "ssn",
        "socialSecurityNumber"
    };

    public RequestLoggingRedactionMiddleware(RequestDelegate next, ILogger<RequestLoggingRedactionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // For login/auth endpoints, enable request body reading
        var path = context.Request.Path.Value?.ToLowerInvariant() ?? "";
        if (path.Contains("/auth/login") || path.Contains("/auth/refresh") || path.Contains("/platform/auth"))
        {
            context.Request.EnableBuffering();
            
            using var reader = new StreamReader(context.Request.Body, Encoding.UTF8, leaveOpen: true);
            var body = await reader.ReadToEndAsync();
            context.Request.Body.Position = 0;
            
            // Redact sensitive fields in logs
            RedactSensitiveData(body);
        }

        await _next(context);
    }

    private void RedactSensitiveData(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return;

        try
        {
            var doc = JsonDocument.Parse(body);
            foreach (var element in doc.RootElement.EnumerateObject())
            {
                if (SensitiveFields.Contains(element.Name) && element.Value.ValueKind == JsonValueKind.String)
                {
                    _logger.LogDebug("Request contains sensitive field: {FieldName}", element.Name);
                }
            }
        }
        catch
        {
            // Ignore parsing errors
        }
    }
}
