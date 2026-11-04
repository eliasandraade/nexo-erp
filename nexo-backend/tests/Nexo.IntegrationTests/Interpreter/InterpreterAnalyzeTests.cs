using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nexo.Application.Features.Auth;
using Nexo.Infrastructure.Persistence;
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
    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public InterpreterAnalyzeTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
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

    /// <summary>
    /// Issue #100: the memory profile refresh that follows a confirmation runs on the request's DbContext. It used to be
    /// fire-and-forget: still running when the request ended, it raced the DbContext disposal, the pooled connection went
    /// back to the pool mid-command, and the next request that took it failed (a 500 on an unrelated endpoint). The refresh
    /// is now done before the confirmation answers.
    /// </summary>
    [Fact]
    public async Task Confirm_RefreshesTheMemoryProfileBeforeAnswering()
    {
        var admin = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var analyzed = await admin.PostAsJsonAsync("/api/v1/interpreter/analyze", new { text = "areia 50 reais", inputSource = "Text" });
        analyzed.StatusCode.Should().Be(HttpStatusCode.OK, await analyzed.Content.ReadAsStringAsync());
        var draft = await analyzed.Content.ReadFromJsonAsync<JsonElement>();

        var before = DateTime.UtcNow.AddMilliseconds(-1);   // the database keeps microseconds
        await admin.PostAsJsonAsync($"/api/v1/movements/{draft.GetProperty("draftId").GetGuid()}/confirm", new
        {
            amount = 50m, date = "2026-10-01", description = "Areia", direction = "Out", nature = "Expense",
            categoryId = (Guid?)null, contextType = "Loja", contextId = (Guid?)null, accountId = (Guid?)null,
            originalSuggestionId = draft.GetProperty("suggestionId").GetGuid(),
        }).ShouldHaveStatusAsync(HttpStatusCode.OK, _factory);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        var user = await db.Users.IgnoreQueryFilters().SingleAsync(u => u.Login == TestCredentials.AdminLogin);
        var profile = await db.IntMemoryProfiles.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(p => p.TenantId == user.TenantId && p.UserId == user.Id);
        profile.LastRebuildAt.Should().BeOnOrAfter(before, "the refresh finished before the confirmation answered");
    }
}
