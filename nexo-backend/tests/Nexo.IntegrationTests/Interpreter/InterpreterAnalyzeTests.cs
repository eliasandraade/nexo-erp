using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Nexo.Application.Features.Auth;
using Nexo.IntegrationTests.Common;
using Nexo.IntegrationTests.Helpers;

namespace Nexo.IntegrationTests.Interpreter;

/// <summary>
/// Exercises the real /api/v1/interpreter/analyze pipeline with the exact text payload
/// the Orken Build expense dialog sends. Reproduces (or rules out) the production 500.
/// The analyze step must never return 500 for a valid text payload.
/// </summary>
[Collection("Integration")]
public class InterpreterAnalyzeTests
{
    private readonly HttpClient _client;

    public InterpreterAnalyzeTests(TestWebApplicationFactory factory)
    {
        _client = factory.CreateApiClient();
    }

    private async Task AuthenticateAsync()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login",
            TestCredentials.AdminLoginPayload());
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<LoginResponse>();
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", body!.AccessToken);
    }

    [Fact]
    public async Task Analyze_BuildExpenseText_DoesNotReturn500()
    {
        await AuthenticateAsync();

        var resp = await _client.PostAsJsonAsync("/api/v1/interpreter/analyze",
            new { text = "cimento 50 sacos 42 reais cada, obra torres", inputSource = "Text" });

        // Whatever happens, it must be a controlled response — never an unhandled 500.
        ((int)resp.StatusCode).Should().BeLessThan(500,
            because: $"analyze returned {(int)resp.StatusCode}; body: {await resp.Content.ReadAsStringAsync()}");

        // For a valid text payload the rule-based pipeline should succeed.
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.TryGetProperty("draftId", out _).Should().BeTrue();
        body.TryGetProperty("suggestionId", out _).Should().BeTrue();
    }
}
