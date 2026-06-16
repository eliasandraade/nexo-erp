using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Nexo.Application.Features.Auth;
using Nexo.IntegrationTests.Common;
using Nexo.IntegrationTests.Helpers;

namespace Nexo.IntegrationTests.Build;

/// <summary>
/// End-to-end coverage for the ORKEN BUILD module (previously untested).
/// Exercises the real pipeline: auth → module gate → controller → service → repo → DB.
///
/// Guards in particular:
///   - financial-summary serializes estimatedBudget/approvedBudget (P0 contract fix);
///   - approving a project-linked budget propagates finalPrice → project.budgetApproved;
///   - one daily log per project per date.
/// </summary>
[Collection("Integration")]
public class BuildFlowTests
{
    private readonly HttpClient _client;

    public BuildFlowTests(TestWebApplicationFactory factory)
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

    private async Task<JsonElement> CreateProjectAsync(decimal? budgetEstimated = null)
    {
        var resp = await _client.PostAsJsonAsync("/api/v1/build/projects", new
        {
            name            = $"Obra {Guid.NewGuid():N}".Substring(0, 14),
            clientName      = "Cliente Teste",
            type            = "House",
            budgetEstimated = budgetEstimated,
        });
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        return await resp.Content.ReadFromJsonAsync<JsonElement>();
    }

    [Fact]
    public async Task FinancialSummary_Exposes_CamelCaseBudgetFields()
    {
        await AuthenticateAsync();
        var project = await CreateProjectAsync(budgetEstimated: 50000m);
        var id = project.GetProperty("id").GetGuid();

        var resp = await _client.GetAsync($"/api/v1/build/projects/{id}/financial-summary");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        // P0 regression: the frontend reads estimatedBudget / approvedBudget.
        body.TryGetProperty("estimatedBudget", out var est).Should().BeTrue();
        body.TryGetProperty("approvedBudget", out _).Should().BeTrue();
        est.GetDecimal().Should().Be(50000m);
        body.GetProperty("totalRealizedExpenses").GetDecimal().Should().Be(0m);
    }

    [Fact]
    public async Task ApprovingProjectLinkedBudget_Sets_ProjectBudgetApproved()
    {
        await AuthenticateAsync();
        var project = await CreateProjectAsync();
        var projectId = project.GetProperty("id").GetGuid();

        var budgetResp = await _client.PostAsJsonAsync("/api/v1/build/budgets", new
        {
            name          = "Orçamento V1",
            projectId,
            marginPercent = 0m,
        });
        budgetResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var budget = await budgetResp.Content.ReadFromJsonAsync<JsonElement>();
        var budgetId = budget.GetProperty("id").GetGuid();

        var itemResp = await _client.PostAsJsonAsync($"/api/v1/build/budgets/{budgetId}/items", new
        {
            name     = "Cimento",
            category = "Materiais",
            quantity = 10m,
            unit     = "sc",
            unitCost = 42m,
        });
        itemResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var withItem = await itemResp.Content.ReadFromJsonAsync<JsonElement>();
        var finalPrice = withItem.GetProperty("finalPrice").GetDecimal();
        finalPrice.Should().Be(420m); // 10 × 42, margin 0

        var approveResp = await _client.PostAsync($"/api/v1/build/budgets/{budgetId}/approve", null);
        approveResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var projResp = await _client.GetAsync($"/api/v1/build/projects/{projectId}");
        projResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var proj = await projResp.Content.ReadFromJsonAsync<JsonElement>();
        proj.GetProperty("budgetApproved").GetDecimal().Should().Be(finalPrice);
    }

    [Fact]
    public async Task ProjectLifecycle_Start_TransitionsToInProgress()
    {
        await AuthenticateAsync();
        var project = await CreateProjectAsync();
        var id = project.GetProperty("id").GetGuid();
        project.GetProperty("status").GetString().Should().Be("Planning");

        var startResp = await _client.PostAsync($"/api/v1/build/projects/{id}/start", null);
        startResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var started = await startResp.Content.ReadFromJsonAsync<JsonElement>();
        started.GetProperty("status").GetString().Should().Be("InProgress");
    }

    [Fact]
    public async Task DailyLog_DuplicateDate_IsRejected()
    {
        await AuthenticateAsync();
        var project = await CreateProjectAsync();
        var id = project.GetProperty("id").GetGuid();
        var date = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd");

        var first = await _client.PostAsJsonAsync($"/api/v1/build/projects/{id}/daily-logs",
            new { date, notes = "Concretagem da fundação." });
        first.StatusCode.Should().Be(HttpStatusCode.Created);

        var dup = await _client.PostAsJsonAsync($"/api/v1/build/projects/{id}/daily-logs",
            new { date, notes = "Segundo registro mesma data." });
        dup.IsSuccessStatusCode.Should().BeFalse();
    }
}
