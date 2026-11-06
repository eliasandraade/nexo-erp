using System.Net;
using System.Net.Http.Headers;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Nexo.Api.Controllers;
using Nexo.IntegrationTests.Helpers;
using Xunit;

namespace Nexo.IntegrationTests.Security;

/// <summary>
/// The address browsers send Content-Security-Policy violations to (docs/security/csp.md). Anyone can call it and a browser
/// ignores the answer, so it always says 204, writes at most one log line per request, touches no table, and what reaches the
/// log is reduced first: signed URLs and tokens live in the query string and in some paths of the app.
/// </summary>
[Collection("Integration")]
public class CspReportTests
{
    private const string Route = "/api/csp-reports";
    private const string CspReport = "application/csp-report";
    private const string ReportsJson = "application/reports+json";
    private const string Tracer = "VALOR-QUE-NAO-PODE-IR-PARA-O-LOG";

    /// <summary>
    /// The endpoint's lines in a log of their own. The host's logger is not safe to read here: every host that stops closes Serilog's
    /// static logger (Program), which is whichever host was built last, and from then on that host writes to nowhere. With other test
    /// classes starting and stopping hosts, a line would sometimes not arrive, and "nothing was logged" would prove nothing.
    /// </summary>
    private static readonly ServerLogCapture EndpointLog = new();

    private static WebApplicationFactory<Program>? _host;

    private readonly TestWebApplicationFactory _factory;

    public CspReportTests(TestWebApplicationFactory factory) => _factory = factory;

    private WebApplicationFactory<Program> Host => _host ??= HostWith();

    private WebApplicationFactory<Program> HostWith(params (string Key, string Value)[] settings)
        => _factory.WithWebHostBuilder(b =>
        {
            foreach (var (key, value) in settings) b.UseSetting(key, value);
            b.ConfigureTestServices(services => services.AddSingleton<ILogger<CspReportsController>>(provider =>
                new Serilog.Extensions.Logging.SerilogLoggerFactory(new Serilog.LoggerConfiguration()
                        .Enrich.With(new Nexo.Api.Observability.RequestIdEnricher(provider.GetRequiredService<IHttpContextAccessor>()))
                        .WriteTo.Sink(EndpointLog)
                        .CreateLogger(), dispose: true)
                    .CreateLogger<CspReportsController>()));
        });

    // ── What a browser sends ──────────────────────────────────────────────────

    [Fact]
    public async Task A_report_answers_204_with_no_body_and_logs_one_structured_line()
    {
        using var response = await PostAsync(CspReport, ReportUri(
            document: "https://erp.orken.com.br/login",
            blocked: "inline",
            directive: "script-src-elem",
            source: "https://erp.orken.com.br/assets/index-Abc123.js"));

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await response.Content.ReadAsByteArrayAsync()).Should().BeEmpty();

        LogLines(response).Should().ContainSingle().Which.Should()
            .Contain("script-src-elem").And.Contain("inline").And.Contain("/login").And.Contain("/assets/index-Abc123.js");
        // a field of its own, for a log search: not only inside the sentence
        Properties(response).Should().Contain("Violations=").And.Contain("BlockedOrigin").And.Contain("EffectiveDirective");
    }

    [Fact]
    public async Task A_reporting_api_batch_logs_one_line_with_its_csp_violations_and_nothing_else()
    {
        var body = $$"""
            [
              { "type": "csp-violation", "age": 10, "url": "https://erp.orken.com.br/dashboard", "user_agent": "{{Tracer}}",
                "body": { "documentURL": "https://erp.orken.com.br/dashboard", "blockedURL": "https://a.example/x.png",
                          "effectiveDirective": "img-src", "disposition": "report", "statusCode": 200, "lineNumber": 7,
                          "sourceFile": "https://erp.orken.com.br/assets/a-1.js", "sample": "{{Tracer}}", "referrer": "{{Tracer}}" } },
              { "type": "csp-violation", "age": 11, "url": "https://www.orken.com.br/agendar/barbearia",
                "body": { "documentURL": "https://www.orken.com.br/agendar/barbearia", "blockedURL": "https://b.example/y.js",
                          "effectiveDirective": "script-src-elem", "disposition": "report" } },
              { "type": "deprecation", "url": "https://erp.orken.com.br/", "body": { "id": "{{Tracer}}" } }
            ]
            """;

        using var response = await PostAsync(ReportsJson, body);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var line = LogLines(response).Should().ContainSingle().Subject;
        line.Should().Contain("img-src").And.Contain("https://a.example").And.Contain("/dashboard")
            .And.Contain("script-src-elem").And.Contain("https://b.example").And.Contain("/agendar/*");
        (line + Properties(response)).Should().NotContain(Tracer);
    }

    [Fact]
    public async Task The_media_type_may_carry_parameters_and_any_casing()
    {
        using var response = await PostAsync("Application/CSP-Report; charset=utf-8", ReportUri());

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        LogLines(response).Should().ContainSingle().Which.Should().Contain("script-src-elem");
    }

    [Fact]
    public async Task A_batch_logs_a_bounded_number_of_violations_and_repeats_only_once()
    {
        var same = Enumerable.Repeat(ViolationItem("https://same.example/a.png"), 20);
        var many = Enumerable.Range(0, 60).Select(i => ViolationItem($"https://h{i}.example/a.png"));

        using var response = await PostAsync(ReportsJson, "[" + string.Join(",", same.Concat(many)) + "]");

        var line = LogLines(response).Should().ContainSingle().Subject;
        CountOf(line, "https://same.example").Should().Be(1);
        CountOf(line, ".example").Should().BeLessThanOrEqualTo(20);
    }

    // ── What never reaches the log ────────────────────────────────────────────

    [Fact]
    public async Task Query_strings_fragments_tokens_and_samples_never_reach_the_log()
    {
        var body = $$"""
            { "csp-report": {
                "document-uri": "https://erp.orken.com.br/agendar/barbearia/reserva/{{Tracer}}?token={{Tracer}}#{{Tracer}}",
                "referrer": "https://erp.orken.com.br/rastrear/{{Tracer}}?a={{Tracer}}",
                "blocked-uri": "https://acct.r2.cloudflarestorage.com/bucket/private/tenants/{{Tracer}}/foto.png?X-Amz-Signature={{Tracer}}#{{Tracer}}",
                "violated-directive": "img-src",
                "effective-directive": "img-src",
                "original-policy": "default-src 'none'; report-uri /{{Tracer}}",
                "source-file": "https://erp.orken.com.br/rastrear/{{Tracer}}?x={{Tracer}}#{{Tracer}}",
                "script-sample": "cliente@exemplo.test {{Tracer}}",
                "disposition": "report", "status-code": 200, "line-number": 3, "column-number": 9
            } }
            """;

        using var response = await PostAsync(CspReport, body);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var everything = string.Join('\n', LogLines(response)) + Properties(response);
        everything.Should().NotContain(Tracer).And.NotContain("cliente@exemplo.test").And.NotContain("X-Amz-Signature")
            .And.NotContain("token=").And.NotContain("?").And.NotContain("#")
            .And.Contain("https://acct.r2.cloudflarestorage.com").And.Contain("/agendar/*");
    }

    [Theory]
    [InlineData("data:image/png;base64,QUJDRA==", "data", "QUJDRA")]
    [InlineData("blob:https://erp.orken.com.br/9f1c", "blob", "9f1c")]
    [InlineData("chrome-extension://abcdefghijkl/inject.js", "chrome-extension", "abcdefghijkl")]
    [InlineData("about:srcdoc", "about", "srcdoc")]
    [InlineData("inline", "inline", "(none)")]
    [InlineData("eval", "eval", "(none)")]
    [InlineData("wasm-eval", "wasm-eval", "(none)")]
    [InlineData("https://user:senha@host.example:8443/caminho/segredo?q=1", "https://host.example:8443", "senha")]
    [InlineData("wss://host.example/hubs/restaurant?access_token=abc", "wss://host.example", "access_token")]
    public async Task A_blocked_address_is_reduced_to_its_origin_or_scheme(string blocked, string kept, string gone)
    {
        using var response = await PostAsync(CspReport, ReportUri(blocked: blocked));

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var line = LogLines(response).Should().ContainSingle().Subject;
        line.Should().Contain(kept).And.NotContain(gone);
        line.Should().NotContain("/caminho");
    }

    [Fact]
    public async Task Field_lengths_are_capped_and_line_breaks_cannot_forge_a_log_line()
    {
        var longHost = new string('a', 2000) + ".example";
        using var response = await PostAsync(CspReport, ReportUri(
            directive: "img-src\r\n[ERROR] forjado" + new string('x', 2000),
            blocked: $"https://{longHost}/x",
            document: "https://erp.orken.com.br/" + new string('p', 2000)));

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var line = LogLines(response).Should().ContainSingle().Subject;
        line.Length.Should().BeLessThan(1500);
        line.Should().NotContain("\r").And.NotContain("\n").And.NotContain(new string('a', 300));
    }

    // ── Whatever comes in, the answer is the same ─────────────────────────────

    [Theory]
    [InlineData("application/json")]
    [InlineData("text/plain")]
    [InlineData("multipart/form-data; boundary=x")]
    public async Task Another_media_type_answers_204_and_its_content_is_not_read(string contentType)
    {
        using var response = await PostAsync(contentType, ReportUri(blocked: $"https://{Tracer}.example/x"));

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (string.Join('\n', LogLines(response)) + Properties(response)).Should().NotContain(Tracer);
    }

    [Fact]
    public async Task No_media_type_at_all_answers_204()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Route) { Content = new ByteArrayContent(Encoding.UTF8.GetBytes(ReportUri())) };
        request.Headers.Add("X-Forwarded-For", NewClientAddress());
        using var response = await Host.CreateClient().SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Theory]
    [InlineData(CspReport, "isto nao e json")]
    [InlineData(CspReport, "")]
    [InlineData(CspReport, "[]")]
    [InlineData(CspReport, "null")]
    [InlineData(CspReport, "{ \"csp-report\": 7 }")]
    [InlineData(CspReport, "{ \"csp-report\": { \"blocked-uri\": 7, \"effective-directive\": [1] } }")]
    [InlineData(ReportsJson, "{ \"csp-report\": {} }")]
    [InlineData(ReportsJson, "[1, \"a\", null, { \"type\": \"csp-violation\" }]")]
    public async Task A_malformed_report_answers_204_and_logs_no_violation(string contentType, string body)
    {
        using var response = await PostAsync(contentType, body);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        Properties(response).Should().NotContain("EffectiveDirective");
    }

    [Theory]
    [InlineData("{ \"csp-report\": { \"effective-directive\": \"img-src\", \"blocked-uri\": \"\\ud800\" } }")]
    [InlineData("[{ \"type\": \"\\ud800\", \"body\": {} }]")]
    public async Task A_string_that_cannot_be_decoded_answers_204(string body)
    {
        using var response = await PostAsync(body.StartsWith('[') ? ReportsJson : CspReport, body);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Bytes_that_are_not_text_answer_204()
    {
        byte[] start = "{ \"csp-report\": { \"effective-directive\": \"img-src\", \"blocked-uri\": \""u8.ToArray();
        byte[] end = "\" } }"u8.ToArray();
        using var request = NewReport(CspReport, "");
        request.Content = new ByteArrayContent([.. start, 0xC3, 0x28, 0xFF, .. end]);
        request.Content.Headers.TryAddWithoutValidation("Content-Type", CspReport);

        using var response = await _factory.CreateApiClient().SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task A_deeply_nested_report_answers_204()
    {
        var body = new string('[', 5000) + new string(']', 5000);

        using var response = await PostAsync(ReportsJson, body);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task A_body_over_the_limit_answers_204_and_is_not_parsed()
    {
        var body = ReportUri(blocked: $"https://{Tracer}.example/x") + new string(' ', 17 * 1024);

        using var response = await PostAsync(CspReport, body);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (string.Join('\n', LogLines(response)) + Properties(response)).Should().NotContain(Tracer);
    }

    [Fact]
    public async Task A_body_over_the_limit_without_a_length_header_answers_204_and_is_not_parsed()
    {
        var body = Encoding.UTF8.GetBytes(ReportUri(blocked: $"https://{Tracer}.example/x") + new string(' ', 17 * 1024));
        using var request = new HttpRequestMessage(HttpMethod.Post, Route) { Content = new StreamContent(new NoLengthStream(body)) };
        request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(CspReport);
        request.Content.Headers.ContentLength = null;
        request.Headers.TransferEncodingChunked = true;
        request.Headers.Add("X-Forwarded-For", NewClientAddress());

        using var response = await Host.CreateClient().SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (string.Join('\n', LogLines(response)) + Properties(response)).Should().NotContain(Tracer);
    }

    // ── Rate limit: per client, through ClientIpResolver, and silent ──────────

    [Fact]
    public async Task Over_the_limit_the_answer_is_still_204_and_nothing_more_is_logged()
    {
        var client = HostWith(("RateLimiting:CspReport:PermitLimit", "2")).CreateClient();
        var address = NewClientAddress();

        (await SendAsync(client, address)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var second = await SendAsync(client, address);
        second.StatusCode.Should().Be(HttpStatusCode.NoContent);
        LogLines(second).Should().ContainSingle("the second one is inside the limit");

        var third = await SendAsync(client, address);
        third.StatusCode.Should().Be(HttpStatusCode.NoContent);
        third.Headers.Contains("Retry-After").Should().BeFalse("a browser ignores the answer, and nobody gets told about the limit");
        (await third.Content.ReadAsByteArrayAsync()).Should().BeEmpty();
        LogLines(third).Should().BeEmpty("a report over the limit writes nothing");

        var other = await SendAsync(client, NewClientAddress());
        LogLines(other).Should().ContainSingle("another client has its own limit");
    }

    [Fact]
    public async Task Behind_a_proxy_that_sets_x_real_ip_a_forged_x_forwarded_for_does_not_escape_the_limit()
    {
        var client = HostWith(("RateLimiting:CspReport:PermitLimit", "1"), ("RateLimiting:ClientIpSource", "RealIp")).CreateClient();
        var real = NewClientAddress();

        LogLines(await SendAsync(client, NewClientAddress(), realIp: real)).Should().ContainSingle();
        LogLines(await SendAsync(client, NewClientAddress(), realIp: real)).Should().BeEmpty("the forged header changed, the client did not");
        LogLines(await SendAsync(client, real, realIp: NewClientAddress())).Should().ContainSingle("the proxy's header is the one read");
    }

    [Fact]
    public async Task With_the_limit_off_every_report_is_logged()
    {
        var client = HostWith(("RateLimiting:CspReport:PermitLimit", "1"), ("RateLimiting:CspReport:Enabled", "false")).CreateClient();
        var address = NewClientAddress();

        LogLines(await SendAsync(client, address)).Should().ContainSingle();
        LogLines(await SendAsync(client, address)).Should().ContainSingle();
    }

    // ── Anonymous, no tables, reachable from the app ──────────────────────────

    [Fact]
    public async Task It_runs_no_sql_and_sets_no_cookie()
    {
        var marker = Guid.NewGuid().ToString("N");
        using var request = NewReport(CspReport, ReportUri());
        request.Headers.Add(SqlCommandCounter.Header, marker);

        using var response = await _factory.CreateApiClient().SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        _factory.Services.GetRequiredService<SqlCommandCounter>().CountFor(marker).Should().Be(0);
        response.Headers.Contains("Set-Cookie").Should().BeFalse();
    }

    [Fact]
    public async Task A_broken_session_header_does_not_change_the_answer()
    {
        using var request = NewReport(CspReport, ReportUri());
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "isto-nao-e-um-token");

        using var response = await _factory.CreateApiClient().SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Only_post_exists()
    {
        (await _factory.CreateApiClient().GetAsync(Route)).StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed);
    }

    [Theory]
    [InlineData("https://erp.orken.com.br")]
    [InlineData("https://www.orken.com.br")]
    public async Task The_preflight_of_the_reporting_api_is_answered_for_the_app(string origin)
    {
        using var request = new HttpRequestMessage(HttpMethod.Options, Route);
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", "POST");
        request.Headers.Add("Access-Control-Request-Headers", "content-type");

        using var response = await _factory.CreateApiClient().SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        response.Headers.GetValues("Access-Control-Allow-Origin").Should().Equal(origin);
        response.Headers.GetValues("Access-Control-Allow-Headers").Should().Contain(h => h.Contains("content-type", StringComparison.OrdinalIgnoreCase));
    }

    // ── Apoio ─────────────────────────────────────────────────────────────────

    private static string ReportUri(
        string document = "https://erp.orken.com.br/login",
        string blocked = "inline",
        string directive = "script-src-elem",
        string source = "https://erp.orken.com.br/assets/index-Abc123.js")
        => System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["csp-report"] = new Dictionary<string, object>
            {
                ["document-uri"] = document,
                ["blocked-uri"] = blocked,
                ["violated-directive"] = directive,
                ["effective-directive"] = directive,
                ["source-file"] = source,
                ["line-number"] = 12,
                ["disposition"] = "report",
                ["status-code"] = 200,
            },
        });

    private static string ViolationItem(string blocked)
        => $$"""{ "type": "csp-violation", "url": "https://erp.orken.com.br/", "body": { "documentURL": "https://erp.orken.com.br/", "blockedURL": "{{blocked}}", "effectiveDirective": "img-src" } }""";

    private static string NewClientAddress() => $"10.{Random.Shared.Next(256)}.{Random.Shared.Next(256)}.{Random.Shared.Next(1, 255)}";

    private static HttpRequestMessage NewReport(string contentType, string body, string? forwardedFor = null, string? realIp = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, Route) { Content = new ByteArrayContent(Encoding.UTF8.GetBytes(body)) };
        request.Content.Headers.TryAddWithoutValidation("Content-Type", contentType);
        request.Headers.Add("X-Forwarded-For", forwardedFor ?? NewClientAddress());
        if (realIp is not null) request.Headers.Add("X-Real-IP", realIp);
        return request;
    }

    private async Task<HttpResponseMessage> PostAsync(string contentType, string body)
    {
        using var request = NewReport(contentType, body);
        return await Host.CreateClient().SendAsync(request);
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, string forwardedFor, string? realIp = null)
    {
        using var request = NewReport(CspReport, ReportUri(), forwardedFor, realIp);
        return await client.SendAsync(request);
    }

    /// <summary>The violation lines of this request.</summary>
    private string[] LogLines(HttpResponseMessage response)
    {
        var id = response.Headers.GetValues("X-Request-Id").Single();
        return EndpointLog.For(id).Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
            .Where(l => l.Contains("CSP report", StringComparison.Ordinal))
            .ToArray();
    }

    private string Properties(HttpResponseMessage response)
        => EndpointLog.PropertiesFor(response.Headers.GetValues("X-Request-Id").Single());

    private static int CountOf(string text, string part)
    {
        var count = 0;
        for (var at = text.IndexOf(part, StringComparison.Ordinal); at >= 0; at = text.IndexOf(part, at + part.Length, StringComparison.Ordinal)) count++;
        return count;
    }

    /// <summary>A body whose length the server only learns by reading it, as with a chunked request.</summary>
    private sealed class NoLengthStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override bool CanSeek => false;
    }
}
