using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;

namespace Nexo.IntegrationTests.Helpers;

/// <summary>
/// Stands in for api.anthropic.com in the integration tests: nothing ever leaves the test host. Each test sets what it
/// answers (<see cref="Respond"/>) and reads what was sent (<see cref="Requests"/>). Never disposed by the HttpClient
/// factory, so one instance serves the whole host.
/// </summary>
public sealed class FakeAnthropicHandler : HttpMessageHandler
{
    public const string ExtractionAnswer =
        """{"amount":87.9,"amount_confidence":0.96,"date":"2026-10-02","date_confidence":0.92,"payee":"Posto Avenida","payee_confidence":0.91,"account":"Cartão","account_confidence":0.65}""";

    public sealed record SentRequest(string Model, string Body, string? Authorization, string Host);

    public ConcurrentQueue<SentRequest> Requests { get; } = new();

    public Func<string, HttpResponseMessage> Respond { get; set; } = model => Success(model);

    public void Reset()
    {
        Requests.Clear();
        Respond = model => Success(model);
    }

    public static HttpResponseMessage Success(string model, string text = ExtractionAnswer, int input = 150, int output = 50)
        => Json(HttpStatusCode.OK, JsonSerializer.Serialize(new
        {
            id = "msg_fake", type = "message", role = "assistant", model,
            content = new[] { new { type = "text", text } },
            stop_reason = "end_turn", stop_sequence = (string?)null,
            usage = new { input_tokens = input, output_tokens = output },
        }));

    public static HttpResponseMessage Error(HttpStatusCode status, string type)
        => Json(status, "{\"type\":\"error\",\"error\":{\"type\":\"" + type + "\",\"message\":\"anthropic-internal-detail\"}}");

    private static HttpResponseMessage Json(HttpStatusCode status, string body)
        => new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
        var model = "";
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("model", out var m)) model = m.GetString() ?? "";
        }
        catch (JsonException)
        {
            // not a messages request
        }
        Requests.Enqueue(new SentRequest(model, body, request.Headers.Authorization?.Parameter, request.RequestUri?.Host ?? ""));
        return Respond(model);
    }

    protected override void Dispose(bool disposing)
    {
        // shared by the whole host: the HttpClient factory must not dispose it between handler lifetimes
    }
}
