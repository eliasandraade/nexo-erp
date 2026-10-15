using Nexo.Infrastructure.Persistence;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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
    private readonly TestWebApplicationFactory _factory;

    public BuildFlowTests(TestWebApplicationFactory factory)
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

    [Fact]
    public async Task Site_photos_only_take_this_tenants_private_daily_log_keys()
    {
        await AuthenticateAsync();
        var project = await CreateProjectAsync();
        var id = project.GetProperty("id").GetGuid();
        var log = await (await _client.PostAsJsonAsync($"/api/v1/build/projects/{id}/daily-logs",
            new { date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-20)).ToString("yyyy-MM-dd"), notes = "Fotos" }))
            .Content.ReadFromJsonAsync<JsonElement>();
        var logId = log.GetProperty("id").GetGuid();

        Guid tenantId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
            tenantId = (await db.Users.IgnoreQueryFilters().FirstAsync(u => u.Login == TestCredentials.AdminLogin)).TenantId;
        }

        foreach (var bad in new[] { $"tenants/{tenantId}/build/daily-logs/a.jpg", $"private/tenants/{Guid.NewGuid()}/build/daily-logs/a.jpg", "https://x.example/a.jpg" })
            (await _client.PostAsJsonAsync($"/api/v1/build/daily-logs/{logId}/photos", new { storageKey = bad, caption = "x" }))
                .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, bad);

        var ok = await _client.PostAsJsonAsync($"/api/v1/build/daily-logs/{logId}/photos",
            new { storageKey = $"private/tenants/{tenantId}/build/daily-logs/a.jpg", caption = "Fundação" });
        ok.IsSuccessStatusCode.Should().BeTrue(await ok.Content.ReadAsStringAsync());
    }

    // ── PR 60: closure ────────────────────────────────────────────────────────

    private async Task<Guid> StageAsync(Guid projectId, string name)
    {
        var r = await _client.PostAsJsonAsync($"/api/v1/build/projects/{projectId}/stages", new { name });
        r.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    [Fact]
    public async Task Project_list_shows_real_stage_and_log_counts()
    {
        await AuthenticateAsync();
        var id = (await CreateProjectAsync()).GetProperty("id").GetGuid();
        var done = await StageAsync(id, "Fundação");
        await StageAsync(id, "Alvenaria");
        (await _client.PutAsJsonAsync($"/api/v1/build/stages/{done}/progress", new { progressPercent = 100, status = "Completed" }))
            .IsSuccessStatusCode.Should().BeTrue();
        (await _client.PostAsJsonAsync($"/api/v1/build/projects/{id}/daily-logs", new { date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-3)), notes = "Concretagem" }))
            .StatusCode.Should().Be(HttpStatusCode.Created);

        var list = await _client.GetFromJsonAsync<JsonElement>("/api/v1/build/projects?pageSize=100");
        var card = list.GetProperty("items").EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == id);
        card.GetProperty("stageCount").GetInt32().Should().Be(2);
        card.GetProperty("completedStageCount").GetInt32().Should().Be(1);
        card.GetProperty("logCount").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task Daily_logs_page_with_the_real_total()
    {
        await AuthenticateAsync();
        var id = (await CreateProjectAsync()).GetProperty("id").GetGuid();
        for (var d = 1; d <= 3; d++)
            (await _client.PostAsJsonAsync($"/api/v1/build/projects/{id}/daily-logs", new { date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-d)), notes = $"Dia {d}" }))
                .StatusCode.Should().Be(HttpStatusCode.Created);

        var first = await _client.GetFromJsonAsync<JsonElement>($"/api/v1/build/projects/{id}/daily-logs?page=1&pageSize=2");
        first.GetProperty("total").GetInt32().Should().Be(3);
        first.GetProperty("items").GetArrayLength().Should().Be(2);
        var second = await _client.GetFromJsonAsync<JsonElement>($"/api/v1/build/projects/{id}/daily-logs?page=2&pageSize=2");
        second.GetProperty("items").GetArrayLength().Should().Be(1);
        (await _client.GetFromJsonAsync<JsonElement>($"/api/v1/build/projects/{id}/daily-logs?pageSize=100000"))
            .GetProperty("pageSize").GetInt32().Should().Be(100, "page size is bounded");
    }

    [Fact]
    public async Task Requests_are_validated_with_readable_messages()
    {
        await AuthenticateAsync();
        var r = await _client.PostAsJsonAsync("/api/v1/build/projects", new { name = "", clientName = "X", type = "House" });
        r.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await r.Content.ReadAsStringAsync()).Should().Contain("Informe o nome da obra.");

        var id = (await CreateProjectAsync()).GetProperty("id").GetGuid();
        (await _client.PostAsJsonAsync($"/api/v1/build/projects/{id}/stages", new { name = "Etapa", plannedStart = "2026-10-10", plannedEnd = "2026-10-01" }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        // the old frontend contract ({ items }) used to crash the reorder with a 500
        (await _client.PutAsJsonAsync($"/api/v1/build/projects/{id}/stages/reorder", new { items = Array.Empty<object>() }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_stage_can_be_edited()
    {
        await AuthenticateAsync();
        var id = (await CreateProjectAsync()).GetProperty("id").GetGuid();
        var stage = await StageAsync(id, "Fundacao");
        var r = await _client.PutAsJsonAsync($"/api/v1/build/stages/{stage}",
            new { name = "Fundação", description = "Sapatas", order = 0, plannedStart = "2026-11-01", plannedEnd = "2026-11-20" });
        r.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await r.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("name").GetString().Should().Be("Fundação");
        body.GetProperty("plannedEndDate").GetString().Should().Be("2026-11-20");
    }

    [Fact]
    public async Task A_wrong_expense_is_reversed_out_of_the_project_and_the_ledger()
    {
        await AuthenticateAsync();
        var id = (await CreateProjectAsync(budgetEstimated: 1000m)).GetProperty("id").GetGuid();
        var analyzed = await (await _client.PostAsJsonAsync("/api/v1/interpreter/analyze",
            new { text = "cimento 100 reais", inputSource = "Text" })).Content.ReadFromJsonAsync<JsonElement>();
        var draft = analyzed.GetProperty("draftId").GetGuid();
        (await _client.PostAsJsonAsync($"/api/v1/movements/{draft}/confirm", new
        {
            amount = 100m, date = DateOnly.FromDateTime(DateTime.UtcNow), description = "Cimento",
            direction = "Out", nature = "Expense", categoryId = (Guid?)null, contextType = "Obra", contextId = id,
            accountId = (Guid?)null, originalSuggestionId = analyzed.GetProperty("suggestionId").GetGuid(),
        })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await _client.GetFromJsonAsync<JsonElement>($"/api/v1/build/projects/{id}/financial-summary"))
            .GetProperty("totalRealizedExpenses").GetDecimal().Should().Be(100m);

        (await _client.PostAsync($"/api/v1/movements/{draft}/void", null)).StatusCode.Should().Be(HttpStatusCode.OK);

        (await _client.GetFromJsonAsync<JsonElement>($"/api/v1/build/projects/{id}/financial-summary"))
            .GetProperty("totalRealizedExpenses").GetDecimal().Should().Be(0m, "a voided expense leaves the project");
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Nexo.Infrastructure.Persistence.NexoDbContext>();
            var mirror = await db.FinancialTransactions.IgnoreQueryFilters().Where(t => t.ReferenceId == draft).ToListAsync();
            mirror.Should().HaveCount(2, "the ledger keeps the expense and gets its counter-entry");
            mirror.Should().ContainSingle(t => t.ReversesTransactionId != null);
        }
        (await _client.PostAsync($"/api/v1/movements/{draft}/void", null)).IsSuccessStatusCode.Should().BeFalse("voided once");
    }
}
