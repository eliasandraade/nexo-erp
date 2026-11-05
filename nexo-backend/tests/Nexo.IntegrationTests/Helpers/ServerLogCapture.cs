using System.Collections.Concurrent;
using System.Net;
using System.Text;
using Serilog.Core;
using Serilog.Events;

namespace Nexo.IntegrationTests.Helpers;

/// <summary>
/// Keeps the latest warnings and errors the test host logged (message, request id, exception with stack), so a
/// test that gets an unexpected status can show what happened on the server. Without it a 500 in CI shows only
/// the status: the host logs to its console, which the test run does not print. Registered as a Serilog sink
/// (Program reads sinks from the container). Bounded: the oldest entries are dropped.
/// </summary>
public sealed class ServerLogCapture : ILogEventSink
{
    private const int Capacity = 1000;

    private readonly ConcurrentQueue<Entry> _entries = new();

    private sealed record Entry(DateTimeOffset At, LogEventLevel Level, string? RequestId, string Message, string? Exception, string Properties);

    public void Emit(LogEvent logEvent)
    {
        if (logEvent.Level < LogEventLevel.Warning) return;
        var requestId = logEvent.Properties.TryGetValue("RequestId", out var v) && v is ScalarValue { Value: string id } ? id : null;
        _entries.Enqueue(new Entry(logEvent.Timestamp, logEvent.Level, requestId, logEvent.RenderMessage(),
            logEvent.Exception?.ToString(), string.Join(" ", logEvent.Properties.Select(p => $"{p.Key}={p.Value}"))));
        while (_entries.Count > Capacity && _entries.TryDequeue(out _)) { }
    }

    /// <summary>The warnings and errors logged for one request (its X-Request-Id), oldest first.</summary>
    public string For(string? requestId)
    {
        if (string.IsNullOrEmpty(requestId)) return "(response without X-Request-Id)";
        var sb = new StringBuilder();
        foreach (var e in _entries.Where(e => e.RequestId == requestId))
        {
            sb.Append(e.At.ToString("O")).Append(' ').Append(e.Level).Append(": ").AppendLine(e.Message);
            if (e.Exception is not null) sb.AppendLine(e.Exception);
        }
        return sb.Length == 0 ? "(no warning or error logged for this request)" : sb.ToString();
    }

    /// <summary>The structured properties of the warnings and errors logged for one request: what a log search sees beyond the message.</summary>
    public string PropertiesFor(string requestId)
        => string.Join(Environment.NewLine, _entries.Where(e => e.RequestId == requestId).Select(e => e.Properties));

    /// <summary>Status, body and the server log of a response — the "because" of a status assertion.</summary>
    public async Task<string> DescribeAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        var requestId = response.Headers.TryGetValues("X-Request-Id", out var ids) ? ids.FirstOrDefault() : null;
        return $"{(int)response.StatusCode} {response.StatusCode} from {response.RequestMessage?.Method} " +
               $"{response.RequestMessage?.RequestUri?.AbsolutePath}{Environment.NewLine}" +
               $"body: {(string.IsNullOrWhiteSpace(body) ? "<empty>" : body)}{Environment.NewLine}" +
               $"server log ({requestId}):{Environment.NewLine}{For(requestId)}";
    }
}

public static class ServerLogAssertions
{
    /// <summary>
    /// Asserts the status of a response; when it differs, the failure carries the body and the server log of that
    /// request (exception type, message and stack), not just the two status codes.
    /// </summary>
    public static async Task ShouldHaveStatusAsync(this Task<HttpResponseMessage> request, HttpStatusCode expected,
        TestWebApplicationFactory factory, string? because = null)
    {
        using var response = await request;
        if (response.StatusCode == expected) return;
        throw new Xunit.Sdk.XunitException(
            $"Expected {(int)expected} {expected}{(because is null ? "" : $" ({because})")}, got {await factory.ServerLog.DescribeAsync(response)}");
    }
}
