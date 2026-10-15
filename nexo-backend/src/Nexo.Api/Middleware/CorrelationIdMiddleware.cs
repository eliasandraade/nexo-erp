using Serilog.Context;

namespace Nexo.Api.Middleware;

/// <summary>
/// One id per request, end to end: taken from <c>X-Request-Id</c> when the caller (proxy, frontend)
/// sends a sane one, generated otherwise. It becomes <see cref="HttpContext.TraceIdentifier"/>, goes
/// back in the response header and on every log line of the request (<c>RequestId</c>), and error
/// bodies carry it — so "the screen said error" can be matched to the exact log lines.
/// </summary>
public sealed class CorrelationIdMiddleware
{
    public const string Header = "X-Request-Id";
    private const int MaxLength = 64;

    private readonly RequestDelegate _next;

    public CorrelationIdMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        var id = Sanitize(context.Request.Headers[Header].ToString()) ?? Guid.NewGuid().ToString("N");
        context.TraceIdentifier = id;
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[Header] = id;
            return Task.CompletedTask;
        });

        using (LogContext.PushProperty("RequestId", id))
        {
            await _next(context);
        }
    }

    /// <summary>Only letters, digits, '-', '_' and '.', 8 to 64 chars — never log what a caller injects.</summary>
    public static string? Sanitize(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Length is < 8 or > MaxLength) return null;
        foreach (var c in value)
            if (!(char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.')) return null;
        return value;
    }
}
