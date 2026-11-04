using System.Net;
using System.Text;
using FluentAssertions;
using NSubstitute;
using Nexo.Domain.Modules.Interpreter;
using Nexo.Infrastructure.Modules.Interpreter;

namespace Nexo.UnitTests.Interpreter;

/// <summary>
/// The real Claude analyzer over a fake HTTP transport — Anthropic is never called. Covers the structured answer, token
/// usage and cost, and every expected failure mapped to a sanitized <see cref="AiProviderCallException"/>.
/// </summary>
public sealed class ClaudeAnalyzerTests
{
    private const string Model = "claude-haiku-5-5";
    private const string FakeKey = "sk-ant-api03-fake-key-for-unit-tests-only-0000";

    private static readonly AnalyzerConfig Config = new(AnalyzerProvider.Claude, Model, FakeKey,
        new ActivePrompt("extraction", "1.0.0", "abc123", "Extraia os campos."), 100_000, 500_000);

    private static AnalysisInput Input(string? text = "Paguei R$ 120,50 para Padaria Central em 03/10/2026 no Pix")
        => new(InputSourceType.Text, text, null, Guid.NewGuid(), Guid.NewGuid());

    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];
        public List<string> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            Bodies.Add(request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken));
            return respond(request);
        }
    }

    private static (ClaudeAnalyzer Analyzer, FakeHandler Handler) Build(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var handler = new FakeHandler(respond);
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(AnthropicClientFactory.HttpClientName).Returns(_ => new HttpClient(handler));
        return (new ClaudeAnalyzer(new AnthropicClientFactory(factory)), handler);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body)
        => new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage Message(string text, string stopReason = "end_turn", int input = 120, int output = 40)
        => Json(HttpStatusCode.OK, System.Text.Json.JsonSerializer.Serialize(new
        {
            id = "msg_test", type = "message", role = "assistant", model = Model,
            content = new[] { new { type = "text", text } },
            stop_reason = stopReason, stop_sequence = (string?)null,
            usage = new { input_tokens = input, output_tokens = output },
        }));

    private static HttpResponseMessage Error(HttpStatusCode status, string type)
        => Json(status, "{\"type\":\"error\",\"error\":{\"type\":\"" + type + "\",\"message\":\"detalhe interno da Anthropic que nunca deve vazar\"}}");

    private const string Answer = """
        {"amount":120.5,"amount_confidence":0.97,"date":"2026-10-03","date_confidence":0.93,"payee":"Padaria Central","payee_confidence":0.9,"account":"Pix","account_confidence":0.7}
        """;

    [Fact]
    public async Task A_structured_answer_becomes_validated_fields_with_tokens_cost_model_and_prompt()
    {
        var (sut, handler) = Build(_ => Message(Answer));

        var output = await sut.AnalyzeAsync(Input(), Config);

        output.Amount.Value.Should().Be(120.50m);
        output.Amount.Status.Should().Be(FieldStatus.AutoFilled);
        output.Date.Value.Should().Be(new DateOnly(2026, 10, 3));
        output.Payee.Value.Should().Be("Padaria Central");
        output.Account.Value.Should().Be("Pix");
        output.Account.Status.Should().Be(FieldStatus.NeedsAttention);
        output.Provider.Should().Be(AnalyzerProvider.Claude);
        output.ModelId.Should().Be(Model);
        output.InputTokens.Should().Be(120);
        output.OutputTokens.Should().Be(40);
        output.EstimatedCostMicros.Should().Be(12 + 20, "120 × $0.10/MTok + 40 × $0.50/MTok, in micro-dollars");
        output.Prompt.PromptVersion.Should().Be("1.0.0");
        output.RawProviderResponse.Should().NotContain("msg_test", "only the validated values are kept, never the provider's raw body");

        handler.Requests.Should().ContainSingle();
        var request = handler.Requests[0];
        request.Headers.Authorization!.Parameter.Should().Be(FakeKey, "the call carries exactly the key configured in the SuperAdmin");
        request.RequestUri!.Host.Should().Be("api.anthropic.com", "the base URL is pinned, never taken from the environment");
        request.Headers.Contains("anthropic-version").Should().BeTrue();
        handler.Bodies[0].Should().Contain(Model).And.Contain("json_schema").And.Contain("lancamento").And.Contain("output_config");
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "authentication_error", AiCallFailure.InvalidCredentials)]
    [InlineData(HttpStatusCode.Forbidden, "permission_error", AiCallFailure.InvalidCredentials)]
    [InlineData(HttpStatusCode.NotFound, "not_found_error", AiCallFailure.ModelNotFound)]
    [InlineData(HttpStatusCode.TooManyRequests, "rate_limit_error", AiCallFailure.RateLimited)]
    [InlineData(HttpStatusCode.InternalServerError, "api_error", AiCallFailure.ProviderError)]
    [InlineData((HttpStatusCode)529, "overloaded_error", AiCallFailure.ProviderError)]
    [InlineData(HttpStatusCode.BadRequest, "invalid_request_error", AiCallFailure.BadRequest)]
    public async Task Provider_errors_become_a_sanitized_failure_with_the_right_reason(HttpStatusCode status, string type, AiCallFailure expected)
    {
        var (sut, _) = Build(_ => Error(status, type));

        var act = () => sut.AnalyzeAsync(Input(), Config);

        var ex = (await act.Should().ThrowAsync<AiProviderCallException>()).Which;
        ex.Failure.Should().Be(expected);
        ex.Provider.Should().Be(AnalyzerProvider.Claude);
        ex.Message.Should().NotContain("detalhe interno", "the provider's body never reaches the caller");
        ex.Message.Should().NotContain(FakeKey);
    }

    [Fact]
    public async Task A_timeout_is_reported_as_timeout()
    {
        var (sut, _) = Build(_ => throw new TaskCanceledException("timeout", new TimeoutException()));

        var ex = (await ((Func<Task>)(() => sut.AnalyzeAsync(Input(), Config))).Should().ThrowAsync<AiProviderCallException>()).Which;

        ex.Failure.Should().Be(AiCallFailure.Timeout);
    }

    [Fact]
    public async Task A_network_failure_is_reported_as_network()
    {
        var (sut, _) = Build(_ => throw new HttpRequestException("No such host is known."));

        var ex = (await ((Func<Task>)(() => sut.AnalyzeAsync(Input(), Config))).Should().ThrowAsync<AiProviderCallException>()).Which;

        ex.Failure.Should().BeOneOf(AiCallFailure.Network, AiCallFailure.Timeout);
        ex.Message.Should().NotContain("No such host");
    }

    [Theory]
    [InlineData("isto não é JSON")]
    [InlineData("[1,2,3]")]
    [InlineData("""{"amount":"cento e vinte","amount_confidence":1,"date":null,"date_confidence":0,"payee":null,"payee_confidence":0,"account":null,"account_confidence":0}""")]
    public async Task A_malformed_answer_is_an_invalid_response_never_a_crash(string text)
    {
        var (sut, _) = Build(_ => Message(text));

        var ex = (await ((Func<Task>)(() => sut.AnalyzeAsync(Input(), Config))).Should().ThrowAsync<AiProviderCallException>()).Which;

        ex.Failure.Should().Be(AiCallFailure.InvalidResponse);
    }

    [Fact]
    public async Task A_refusal_or_a_truncated_answer_is_never_read_as_data()
    {
        var (refused, _) = Build(_ => Message("{}", stopReason: "refusal"));
        (await ((Func<Task>)(() => refused.AnalyzeAsync(Input(), Config))).Should().ThrowAsync<AiProviderCallException>())
            .Which.Failure.Should().Be(AiCallFailure.Refused);

        var (truncated, _) = Build(_ => Message("""{"amount":1""", stopReason: "max_tokens"));
        (await ((Func<Task>)(() => truncated.AnalyzeAsync(Input(), Config))).Should().ThrowAsync<AiProviderCallException>())
            .Which.Failure.Should().Be(AiCallFailure.InvalidResponse);
    }

    [Fact]
    public async Task Without_text_or_configuration_nothing_is_sent()
    {
        var (sut, handler) = Build(_ => Message(Answer));

        (await ((Func<Task>)(() => sut.AnalyzeAsync(Input(null), Config))).Should().ThrowAsync<AiProviderCallException>())
            .Which.Failure.Should().Be(AiCallFailure.UnsupportedInput);
        (await ((Func<Task>)(() => sut.AnalyzeAsync(Input(), null))).Should().ThrowAsync<AiProviderCallException>())
            .Which.Failure.Should().Be(AiCallFailure.BadRequest);
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task The_connection_test_sends_a_minimal_request_and_returns_the_model()
    {
        var (sut, handler) = Build(_ => Message("ok", input: 10, output: 2));

        var model = await sut.PingAsync(FakeKey, Model, CancellationToken.None);

        model.Should().Be(Model);
        handler.Bodies.Single().Should().Contain("\"max_tokens\":16");
    }

    [Fact]
    public void The_runtime_configuration_never_prints_the_key()
    {
        Config.ToString().Should().NotContain(FakeKey).And.Contain("***");
    }
}
