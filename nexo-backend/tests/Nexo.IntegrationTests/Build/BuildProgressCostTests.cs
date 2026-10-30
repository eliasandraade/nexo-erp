using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nexo.Application.Common;
using Nexo.Infrastructure.Modules.Build;
using Nexo.Infrastructure.Persistence;
using Nexo.IntegrationTests.Common;
using Nexo.IntegrationTests.Helpers;

namespace Nexo.IntegrationTests.Build;

/// <summary>
/// Weighted progress (history + daily snapshot + "como estava em X"), schedule baseline/revisions, and cost
/// per stage on the single ledger (FinancialMovement.StageId) — the existing financial summary unchanged.
/// </summary>
[Collection("Integration")]
public class BuildProgressCostTests
{
    private readonly TestWebApplicationFactory _factory;

    public BuildProgressCostTests(TestWebApplicationFactory factory) => _factory = factory;

    private static async Task<JsonElement> Json(HttpResponseMessage r, HttpStatusCode expected)
    {
        var body = await r.Content.ReadAsStringAsync();
        r.StatusCode.Should().Be(expected, body);
        return string.IsNullOrWhiteSpace(body) ? default : JsonDocument.Parse(body).RootElement.Clone();
    }

    private static async Task<Guid> ProjectAsync(HttpClient c)
        => (await Json(await c.PostAsJsonAsync("/api/v1/build/projects", new
        {
            name = "Obra Progresso", clientName = "Cliente", type = "Commercial",
        }), HttpStatusCode.Created)).GetProperty("id").GetGuid();

    private static async Task<Guid> StageAsync(HttpClient c, Guid projectId, string name, decimal weight = 1m,
        string? plannedStart = null, string? plannedEnd = null)
        => (await Json(await c.PostAsJsonAsync($"/api/v1/build/projects/{projectId}/stages", new
        {
            name, weight, plannedStart, plannedEnd,
        }), HttpStatusCode.Created)).GetProperty("id").GetGuid();

    [Fact]
    public async Task Stage_progress_is_recorded_in_the_history_and_in_todays_snapshot()
    {
        var admin = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var projectId = await ProjectAsync(admin);
        var today = BusinessClock.Today();
        var a = await StageAsync(admin, projectId, "Estrutura", 3m, today.AddDays(-10).ToString("yyyy-MM-dd"), today.AddDays(9).ToString("yyyy-MM-dd"));
        await StageAsync(admin, projectId, "Acabamento", 1m);

        var updated = await Json(await admin.PutAsJsonAsync($"/api/v1/build/stages/{a}/progress",
            new { progressPercent = 40, note = "Medição da semana" }), HttpStatusCode.OK);
        updated.GetProperty("progressPercent").GetInt32().Should().Be(40);
        updated.GetProperty("weight").GetDecimal().Should().Be(3m);
        updated.GetProperty("status").GetString().Should().Be("InProgress");

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
            var events = await db.BldStageProgressEvents.IgnoreQueryFilters().Where(e => e.StageId == a).ToListAsync();
            events.Should().ContainSingle();
            events[0].PreviousPercent.Should().Be(0);
            events[0].Percent.Should().Be(40);
            events[0].Note.Should().Be("Medição da semana");
            events[0].EffectiveDate.Should().Be(today);
            var snap = await db.BldProgressSnapshots.IgnoreQueryFilters().SingleAsync(s => s.ProjectId == projectId && s.Date == today);
            snap.ActualPercent.Should().Be(30m, "(3·40 + 1·0) / 4");
            snap.Methodology.Should().Be("weighted-stages/1");
        }

        var progress = await Json(await admin.GetAsync($"/api/v1/build/projects/{projectId}/progress"), HttpStatusCode.OK);
        progress.GetProperty("actualPercent").GetDecimal().Should().Be(30m);
        progress.GetProperty("plannedPercent").GetDecimal().Should().Be(55m, "11th of 20 planned days; the undated stage is left out");
        progress.GetProperty("planSource").GetString().Should().Be("current");
        progress.GetProperty("stages").EnumerateArray().Single(s => s.GetProperty("name").GetString() == "Acabamento")
            .GetProperty("flags").EnumerateArray().Select(f => f.GetString()).Should().Contain("missing_planned_dates");
        progress.GetProperty("curves").GetProperty("planned").GetArrayLength().Should().BeGreaterThan(1);
        progress.GetProperty("forecastDelivery").GetString().Should().NotBeNullOrEmpty();

        var at = await Json(await admin.GetAsync($"/api/v1/build/projects/{projectId}/progress/at?date={today:yyyy-MM-dd}"), HttpStatusCode.OK);
        at.GetProperty("actualPercent").GetDecimal().Should().Be(30m);
        at.GetProperty("date").GetString().Should().Be(today.ToString("yyyy-MM-dd"));
        at.GetProperty("stages").GetArrayLength().Should().Be(2);
        (await admin.GetAsync($"/api/v1/build/projects/{projectId}/progress/at?date={today.AddDays(-30):yyyy-MM-dd}"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound, "there was no snapshot back then");

        // same value without a note → no new event
        await Json(await admin.PutAsJsonAsync($"/api/v1/build/stages/{a}/progress", new { progressPercent = 40 }), HttpStatusCode.OK);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
            (await db.BldStageProgressEvents.IgnoreQueryFilters().CountAsync(e => e.StageId == a)).Should().Be(1);
        }
    }

    [Fact]
    public async Task Past_snapshots_answer_how_the_project_was_and_the_daily_job_fills_the_gaps()
    {
        var admin = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var projectId = await ProjectAsync(admin);
        await StageAsync(admin, projectId, "Única");
        var today = BusinessClock.Today();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
            // nothing for today yet → the job creates it; running again creates nothing new for this project
            await db.BldProgressSnapshots.IgnoreQueryFilters().Where(s => s.ProjectId == projectId).ExecuteDeleteAsync();
            var engine = scope.ServiceProvider.GetRequiredService<BuildProgressEngine>();
            (await engine.EnsureDailySnapshotsAsync(today, CancellationToken.None)).Should().BeGreaterThan(0);
            (await engine.EnsureDailySnapshotsAsync(today, CancellationToken.None)).Should().Be(0);
            (await db.BldProgressSnapshots.IgnoreQueryFilters().CountAsync(s => s.ProjectId == projectId && s.Date == today)).Should().Be(1);

            // an old snapshot (as written by the job on that day)
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE nexo.bld_progress_snapshots SET date = {today.AddDays(-7)}, actual_percent = 12.5 WHERE project_id = {projectId}");
        }

        var past = await Json(await admin.GetAsync($"/api/v1/build/projects/{projectId}/progress/at?date={today.AddDays(-3):yyyy-MM-dd}"),
            HttpStatusCode.OK);
        past.GetProperty("date").GetString().Should().Be(today.AddDays(-7).ToString("yyyy-MM-dd"));
        past.GetProperty("actualPercent").GetDecimal().Should().Be(12.5m);
    }

    [Fact]
    public async Task Baseline_is_frozen_once_and_later_revisions_need_a_reason()
    {
        var admin = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var projectId = await ProjectAsync(admin);
        (await Json(await admin.PostAsJsonAsync($"/api/v1/build/projects/{projectId}/schedule/baseline", new { }),
            HttpStatusCode.UnprocessableEntity)).GetProperty("code").GetString().Should().Be("schedule_without_stages");

        var stage = await StageAsync(admin, projectId, "Fundação", 1m, "2026-01-01", "2026-01-10");
        var baseline = await Json(await admin.PostAsJsonAsync($"/api/v1/build/projects/{projectId}/schedule/baseline", new { }),
            HttpStatusCode.Created);
        baseline.GetProperty("number").GetInt32().Should().Be(1);
        baseline.GetProperty("kind").GetString().Should().Be("Baseline");
        baseline.GetProperty("items")[0].GetProperty("plannedEnd").GetString().Should().Be("2026-01-10");

        // the plan moves, the baseline does not
        await Json(await admin.PutAsJsonAsync($"/api/v1/build/stages/{stage}", new
        {
            name = "Fundação", order = 1, plannedStart = "2026-01-01", plannedEnd = "2026-02-10",
        }), HttpStatusCode.OK);
        var progress = await Json(await admin.GetAsync($"/api/v1/build/projects/{projectId}/progress"), HttpStatusCode.OK);
        progress.GetProperty("planSource").GetString().Should().Be("baseline");
        progress.GetProperty("plannedDelivery").GetString().Should().Be("2026-01-10");

        (await Json(await admin.PostAsJsonAsync($"/api/v1/build/projects/{projectId}/schedule/baseline", new { }),
            HttpStatusCode.UnprocessableEntity)).GetProperty("code").GetString().Should().Be("schedule_revision_reason_required");
        var revision = await Json(await admin.PostAsJsonAsync($"/api/v1/build/projects/{projectId}/schedule/baseline",
            new { reason = "Chuvas de janeiro" }), HttpStatusCode.Created);
        revision.GetProperty("kind").GetString().Should().Be("Revision");
        revision.GetProperty("number").GetInt32().Should().Be(2);

        var revisions = await Json(await admin.GetAsync($"/api/v1/build/projects/{projectId}/schedule/revisions"), HttpStatusCode.OK);
        revisions.GetArrayLength().Should().Be(2);
        (await Json(await admin.GetAsync($"/api/v1/build/projects/{projectId}/progress"), HttpStatusCode.OK))
            .GetProperty("plannedDelivery").GetString().Should().Be("2026-02-10");
    }

    [Fact]
    public async Task Project_site_and_tracking_mode_round_trip_and_are_validated()
    {
        var admin = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var created = await Json(await admin.PostAsJsonAsync("/api/v1/build/projects", new
        {
            name = "Galpão", clientName = "Cliente", type = "Commercial", trackingMode = "ThreeD",
            latitude = -22.9m, longitude = -43.2m, areaM2 = 1500m,
        }), HttpStatusCode.Created);
        created.GetProperty("trackingMode").GetString().Should().Be("ThreeD");
        created.GetProperty("areaM2").GetDecimal().Should().Be(1500m);
        var id = created.GetProperty("id").GetGuid();

        var old = await Json(await admin.PostAsJsonAsync("/api/v1/build/projects", new
        {
            name = "Casa", clientName = "Cliente", type = "House",
        }), HttpStatusCode.Created);
        old.GetProperty("trackingMode").GetString().Should().Be("Standard", "default for clients that do not send it");

        (await admin.PostAsJsonAsync("/api/v1/build/projects", new { name = "X", clientName = "Y", type = "House", latitude = 95m, longitude = 0m }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await admin.PostAsJsonAsync("/api/v1/build/projects", new { name = "X", clientName = "Y", type = "House", trackingMode = "VR" }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var details = await Json(await admin.GetAsync($"/api/v1/build/projects/{id}/details"), HttpStatusCode.OK);
        details.GetProperty("latitude").GetDecimal().Should().Be(-22.9m);
        details.GetProperty("trackingMode").GetString().Should().Be("ThreeD");

        var updated = await Json(await admin.PutAsJsonAsync($"/api/v1/build/projects/{id}", new
        {
            name = "Galpão", clientName = "Cliente", type = "Commercial", location = (string?)null, startDate = (string?)null,
            expectedEndDate = (string?)null, budgetEstimated = (decimal?)null, budgetApproved = (decimal?)null,
            latitude = -22.95m, longitude = -43.25m,
        }), HttpStatusCode.OK);
        updated.GetProperty("trackingMode").GetString().Should().Be("ThreeD", "an update without a mode keeps it");
        updated.GetProperty("longitude").GetDecimal().Should().Be(-43.25m);
    }

    // ── (5) cost per stage ────────────────────────────────────────────────────

    [Fact]
    public async Task A_cost_tagged_with_a_stage_shows_up_in_the_cost_by_stage()
    {
        var admin = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var projectId = await ProjectAsync(admin);
        var fundacao = await StageAsync(admin, projectId, "Fundação");
        await StageAsync(admin, projectId, "Alvenaria");

        var budget = (await Json(await admin.PostAsJsonAsync("/api/v1/build/budgets", new { name = "Orçamento", projectId, marginPercent = 0m }),
            HttpStatusCode.Created)).GetProperty("id").GetGuid();
        await Json(await admin.PostAsJsonAsync($"/api/v1/build/budgets/{budget}/items", new
        {
            name = "Concreto", category = "Material", quantity = 10m, unit = "m³", unitCost = 42m, stageId = fundacao,
        }), HttpStatusCode.OK);
        await Json(await admin.PostAsJsonAsync($"/api/v1/build/budgets/{budget}/items", new
        {
            name = "Projeto", category = "Serviço", quantity = 1m, unit = "un", unitCost = 80m,
        }), HttpStatusCode.OK);
        await Json(await admin.PostAsync($"/api/v1/build/budgets/{budget}/approve", null), HttpStatusCode.OK);

        var analyzed = await Json(await admin.PostAsJsonAsync("/api/v1/interpreter/analyze",
            new { text = "cimento 100 reais", inputSource = "Text" }), HttpStatusCode.OK);
        var movement = analyzed.GetProperty("draftId").GetGuid();
        await Json(await admin.PostAsJsonAsync($"/api/v1/movements/{movement}/confirm", new
        {
            amount = 100m, date = DateOnly.FromDateTime(DateTime.UtcNow), description = "Cimento", direction = "Out",
            nature = "Expense", categoryId = (Guid?)null, contextType = "Obra", contextId = projectId, accountId = (Guid?)null,
            originalSuggestionId = analyzed.GetProperty("suggestionId").GetGuid(),
        }), HttpStatusCode.OK);

        var before = await Json(await admin.GetAsync($"/api/v1/build/projects/{projectId}/cost-by-stage"), HttpStatusCode.OK);
        var noStage = before.GetProperty("stages").EnumerateArray().Single(s => s.GetProperty("stageId").ValueKind == JsonValueKind.Null);
        noStage.GetProperty("stageName").GetString().Should().Be("Sem etapa");
        noStage.GetProperty("realized").GetDecimal().Should().Be(100m);
        noStage.GetProperty("budgeted").GetDecimal().Should().Be(80m);

        var assigned = await Json(await admin.PutAsJsonAsync($"/api/v1/build/projects/{projectId}/movements/{movement}/stage",
            new { stageId = fundacao }), HttpStatusCode.OK);
        assigned.GetProperty("stageName").GetString().Should().Be("Fundação");

        var after = await Json(await admin.GetAsync($"/api/v1/build/projects/{projectId}/cost-by-stage"), HttpStatusCode.OK);
        var row = after.GetProperty("stages").EnumerateArray().Single(s => s.GetProperty("stageName").GetString() == "Fundação");
        row.GetProperty("budgeted").GetDecimal().Should().Be(420m);
        row.GetProperty("realized").GetDecimal().Should().Be(100m);
        row.GetProperty("variance").GetDecimal().Should().Be(320m);
        row.GetProperty("movementCount").GetInt32().Should().Be(1);
        after.GetProperty("totalBudgeted").GetDecimal().Should().Be(500m);
        after.GetProperty("totalRealized").GetDecimal().Should().Be(100m);
        after.GetProperty("stages").EnumerateArray().Single(s => s.GetProperty("stageName").GetString() == "Alvenaria")
            .GetProperty("realized").GetDecimal().Should().Be(0m);

        // the obra's movement list carries the stage, so the UI shows the current assignment
        var list = await Json(await admin.GetAsync($"/api/v1/movements?contextType=Obra&contextId={projectId}"), HttpStatusCode.OK);
        list.GetProperty("items").EnumerateArray().Single(m => m.GetProperty("id").GetGuid() == movement)
            .GetProperty("stageId").GetGuid().Should().Be(fundacao);

        // the existing financial summary is unchanged
        (await Json(await admin.GetAsync($"/api/v1/build/projects/{projectId}/financial-summary"), HttpStatusCode.OK))
            .GetProperty("totalRealizedExpenses").GetDecimal().Should().Be(100m);

        // a stage of another project is refused; clearing works
        var otherProject = await ProjectAsync(admin);
        var foreignStage = await StageAsync(admin, otherProject, "Outra");
        (await Json(await admin.PutAsJsonAsync($"/api/v1/build/projects/{projectId}/movements/{movement}/stage",
            new { stageId = foreignStage }), HttpStatusCode.UnprocessableEntity)).GetProperty("code").GetString().Should().Be("stage_not_of_project");
        (await Json(await admin.PutAsJsonAsync($"/api/v1/build/projects/{otherProject}/movements/{movement}/stage",
            new { stageId = foreignStage }), HttpStatusCode.UnprocessableEntity)).GetProperty("code").GetString().Should().Be("movement_not_of_project");
        (await Json(await admin.PutAsJsonAsync($"/api/v1/build/projects/{projectId}/movements/{movement}/stage",
            new { stageId = (Guid?)null }), HttpStatusCode.OK)).GetProperty("stageId").ValueKind.Should().Be(JsonValueKind.Null);
    }
}
