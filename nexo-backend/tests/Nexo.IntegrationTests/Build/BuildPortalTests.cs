using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nexo.Domain.Entities;
using Nexo.Infrastructure.Persistence;
using Nexo.IntegrationTests.Common;
using Nexo.IntegrationTests.Helpers;

namespace Nexo.IntegrationTests.Build;

/// <summary>
/// Client portal of Orken Build, end to end: company/project slugs, publication, public bootstrap limited by
/// visibility, password sessions (invalid, expired, revoked by a password change), financial data only
/// when enabled, timeline/photos never leaking internal notes or hidden photos, and tenant isolation.
/// </summary>
[Collection("Integration")]
public class BuildPortalTests
{
    private const string SessionHeader = "X-Build-Portal-Session";
    private readonly TestWebApplicationFactory _factory;

    public BuildPortalTests(TestWebApplicationFactory factory) => _factory = factory;

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string Slug(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..(prefix.Length + 9)];

    private static async Task<JsonElement> Json(HttpResponseMessage r, HttpStatusCode expected)
    {
        var body = await r.Content.ReadAsStringAsync();
        r.StatusCode.Should().Be(expected, body);
        return string.IsNullOrWhiteSpace(body) ? default : JsonDocument.Parse(body).RootElement.Clone();
    }

    private static async Task<Guid> ProjectAsync(HttpClient c, object? extra = null)
    {
        var r = await c.PostAsJsonAsync("/api/v1/build/projects", extra ?? new
        {
            name = "Residência Alfa", clientName = "Cliente Portal", type = "House", startDate = "2026-01-05",
            expectedEndDate = "2026-12-20",
        });
        return (await Json(r, HttpStatusCode.Created)).GetProperty("id").GetGuid();
    }

    private static async Task<Guid> StageAsync(HttpClient c, Guid projectId, string name, decimal weight = 1m)
    {
        var r = await c.PostAsJsonAsync($"/api/v1/build/projects/{projectId}/stages", new
        {
            name, plannedStart = "2026-01-05", plannedEnd = "2026-03-31", weight,
        });
        return (await Json(r, HttpStatusCode.Created)).GetProperty("id").GetGuid();
    }

    private static async Task<string> CompanyAsync(HttpClient c)
    {
        var slug = Slug("constr");
        await Json(await c.PutAsJsonAsync("/api/v1/build/portal/company", new
        {
            slug, displayName = "Construtora Teste", contactEmail = "contato@" + TestCredentials.TestDomain,
            contactPhone = "11 4000-0000",
        }), HttpStatusCode.OK);
        return slug;
    }

    private static async Task<(string Company, string Project)> PublishedPortalAsync(
        HttpClient c, Guid projectId, object? visibility = null, string access = "Public", string? password = null)
    {
        var company = await CompanyAsync(c);
        var slug = Slug("obra");
        await Json(await c.PutAsJsonAsync($"/api/v1/build/projects/{projectId}/portal", new
        {
            slug, access = "Public", title = "Acompanhe sua obra", welcomeMessage = "Bem-vindo!",
            primaryColor = "#0A3D62", contactName = "Engenheira Ana", contactWhatsApp = "11999990000", visibility,
        }), HttpStatusCode.OK);
        if (password is not null)
            await Json(await c.PutAsJsonAsync($"/api/v1/build/projects/{projectId}/portal/password", new { password }),
                HttpStatusCode.OK);
        if (access != "Public")
            await Json(await c.PutAsJsonAsync($"/api/v1/build/projects/{projectId}/portal", new
            {
                slug, access, title = "Acompanhe sua obra", visibility,
            }), HttpStatusCode.OK);
        await Json(await c.PostAsync($"/api/v1/build/projects/{projectId}/portal/publish", null), HttpStatusCode.OK);
        return (company, slug);
    }

    private HttpClient Anonymous() => AuthClientFactory.CreateAnonymousClient(_factory);

    private static string PortalPassword() => "Bx!" + Guid.NewGuid().ToString("N")[..12];

    private async Task<Guid> AdminTenantIdAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        return (await db.Users.IgnoreQueryFilters().FirstAsync(u => u.Login == TestCredentials.AdminLogin)).TenantId;
    }

    private async Task<HttpClient> OtherTenantWithBuildAsync()
    {
        var email = $"bld-{Guid.NewGuid():N}"[..16] + "@" + TestCredentials.TestDomain;
        var reg = await _factory.CreateApiClient().PostAsJsonAsync("/api/auth/register",
            new { name = "Outra Construtora", email, password = TestCredentials.AdminPassword });
        reg.StatusCode.Should().Be(HttpStatusCode.OK, await reg.Content.ReadAsStringAsync());
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
            var user = await db.Users.IgnoreQueryFilters().SingleAsync(u => u.Login == email);
            user.Activate();
            db.ModuleSubscriptions.Add(ModuleSubscription.CreateAdminGrant(user.TenantId, "build"));
            await db.SaveChangesAsync();
        }
        return await AuthClientFactory.LoginAsync(_factory, email, TestCredentials.AdminPassword);
    }

    // ── (1) public portal ─────────────────────────────────────────────────────

    [Fact]
    public async Task Published_standard_portal_shows_only_the_enabled_sections()
    {
        var admin = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var created = await Json(await admin.PostAsJsonAsync("/api/v1/build/projects", new
        {
            name = "Casa Beta", clientName = "Cliente", type = "House", trackingMode = "Standard",
            latitude = -23.55m, longitude = -46.63m, areaM2 = 180m,
        }), HttpStatusCode.Created);
        created.GetProperty("trackingMode").GetString().Should().Be("Standard");
        created.GetProperty("latitude").GetDecimal().Should().Be(-23.55m);
        var projectId = created.GetProperty("id").GetGuid();
        var stage = await StageAsync(admin, projectId, "Fundação", 2m);
        await StageAsync(admin, projectId, "Alvenaria");
        await Json(await admin.PutAsJsonAsync($"/api/v1/build/stages/{stage}/progress", new { progressPercent = 60 }),
            HttpStatusCode.OK);

        var update = await Json(await admin.PostAsJsonAsync($"/api/v1/build/projects/{projectId}/client-updates",
            new { title = "Fundação avançando", body = "As sapatas foram concretadas.", kind = "Milestone" }), HttpStatusCode.Created);
        await Json(await admin.PostAsJsonAsync($"/api/v1/build/projects/{projectId}/client-updates",
            new { title = "Rascunho interno", body = "Não publicar." }), HttpStatusCode.Created);
        await Json(await admin.PostAsync($"/api/v1/build/client-updates/{update.GetProperty("id").GetGuid()}/publish", null),
            HttpStatusCode.OK);

        var (company, project) = await PublishedPortalAsync(admin, projectId);

        var settings = await Json(await admin.GetAsync($"/api/v1/build/projects/{projectId}/portal"), HttpStatusCode.OK);
        settings.GetProperty("publicPath").GetString().Should().Be($"/{company}/{project}");
        settings.GetProperty("hasPassword").GetBoolean().Should().BeFalse();
        settings.TryGetProperty("passwordHash", out _).Should().BeFalse();

        var anon = Anonymous();
        var resp = await anon.GetAsync($"/api/public/build/{company}/{project}");
        var raw = await resp.Content.ReadAsStringAsync();
        var body = await Json(resp, HttpStatusCode.OK);
        resp.Headers.CacheControl!.NoStore.Should().BeTrue();

        body.GetProperty("contractVersion").GetString().Should().Be("1");
        body.GetProperty("company").GetProperty("name").GetString().Should().Be("Construtora Teste");
        body.GetProperty("project").GetProperty("name").GetString().Should().Be("Casa Beta");
        body.GetProperty("portal").GetProperty("primaryColor").GetString().Should().Be("#0a3d62");
        body.GetProperty("progress").GetProperty("actualPercent").GetDecimal().Should().Be(40m, "(2·60 + 1·0) / 3");
        body.GetProperty("stages").GetArrayLength().Should().Be(2);
        body.GetProperty("schedule").GetProperty("stages").GetArrayLength().Should().Be(2);
        body.GetProperty("contact").GetProperty("whatsApp").GetString().Should().Be("11999990000");
        var updates = body.GetProperty("latestUpdates");
        updates.GetArrayLength().Should().Be(1, "drafts never reach the client");
        updates[0].GetProperty("title").GetString().Should().Be("Fundação avançando");

        body.TryGetProperty("financialSummary", out _).Should().BeFalse();
        body.GetProperty("progress").TryGetProperty("forecastDelivery", out _).Should().BeFalse("forecast is off by default");
        raw.Should().NotContain(projectId.ToString()).And.NotContain("Cliente").And.NotContain("Rascunho interno");
        body.GetProperty("sections").EnumerateArray().Select(x => x.GetString()).Should()
            .Contain(["overallProgress", "photos", "clientUpdates"]).And.NotContain("financialSummary");
    }

    [Fact]
    public async Task Unknown_and_unpublished_portals_answer_the_same_404()
    {
        var admin = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var projectId = await ProjectAsync(admin);
        var (company, project) = await PublishedPortalAsync(admin, projectId);
        await Json(await admin.PostAsync($"/api/v1/build/projects/{projectId}/portal/unpublish", null), HttpStatusCode.OK);

        var anon = Anonymous();
        var unpublished = await anon.GetAsync($"/api/public/build/{company}/{project}");
        var unknownProject = await anon.GetAsync($"/api/public/build/{company}/{Slug("nada")}");
        var unknownCompany = await anon.GetAsync($"/api/public/build/{Slug("nada")}/{project}");
        var reserved = await anon.GetAsync($"/api/public/build/login/{project}");

        var expected = await unknownProject.Content.ReadAsStringAsync();
        foreach (var r in new[] { unpublished, unknownProject, unknownCompany, reserved })
        {
            r.StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await r.Content.ReadAsStringAsync()).Should().Be(expected);
        }
        JsonDocument.Parse(expected).RootElement.GetProperty("code").GetString().Should().Be("portal_not_found");
    }

    [Fact]
    public async Task Slug_rules_and_publication_prerequisites_are_enforced()
    {
        var admin = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var projectId = await ProjectAsync(admin);
        (await admin.PutAsJsonAsync("/api/v1/build/portal/company", new { slug = "login", displayName = "X" }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await admin.PutAsJsonAsync($"/api/v1/build/projects/{projectId}/portal", new { slug = "Obra Nova!" }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // no portal yet → cannot publish
        var noPortal = await Json(await admin.PostAsync($"/api/v1/build/projects/{projectId}/portal/publish", null),
            HttpStatusCode.UnprocessableEntity);
        noPortal.GetProperty("code").GetString().Should().Be("portal_not_configured");

        // password access without a password → cannot publish
        await CompanyAsync(admin);
        await Json(await admin.PutAsJsonAsync($"/api/v1/build/projects/{projectId}/portal",
            new { slug = Slug("obra"), access = "Password" }), HttpStatusCode.OK);
        var noPwd = await Json(await admin.PostAsync($"/api/v1/build/projects/{projectId}/portal/publish", null),
            HttpStatusCode.UnprocessableEntity);
        noPwd.GetProperty("code").GetString().Should().Be("portal_password_missing");

        // the project slug is unique per company
        var other = await ProjectAsync(admin);
        var taken = (await Json(await admin.GetAsync($"/api/v1/build/projects/{projectId}/portal"), HttpStatusCode.OK))
            .GetProperty("slug").GetString();
        (await admin.PutAsJsonAsync($"/api/v1/build/projects/{other}/portal", new { slug = taken }))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    // ── (2) password portal ───────────────────────────────────────────────────

    [Fact]
    public async Task Password_portal_needs_a_valid_session()
    {
        var admin = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var projectId = await ProjectAsync(admin);
        var password = PortalPassword();
        var (company, project) = await PublishedPortalAsync(admin, projectId, access: "Password", password: password);
        var url = $"/api/public/build/{company}/{project}";
        var anon = Anonymous();

        var locked = await Json(await anon.GetAsync(url), HttpStatusCode.Unauthorized);
        locked.GetProperty("code").GetString().Should().Be("portal_password_required");
        locked.GetProperty("portal").GetProperty("name").GetString().Should().Be("Construtora Teste");
        locked.TryGetProperty("project", out _).Should().BeFalse();
        locked.TryGetProperty("stages", out _).Should().BeFalse();

        var wrong = await Json(await anon.PostAsJsonAsync($"{url}/session", new { password = "errada-123" }), HttpStatusCode.Unauthorized);
        wrong.GetProperty("code").GetString().Should().Be("portal_password_invalid");

        var session = await Json(await anon.PostAsJsonAsync($"{url}/session", new { password }), HttpStatusCode.OK);
        var token = session.GetProperty("token").GetString()!;
        token.Length.Should().BeGreaterThanOrEqualTo(43);
        session.GetProperty("expiresAt").GetDateTimeOffset().Should().BeAfter(DateTimeOffset.UtcNow.AddHours(11));

        var withSession = Anonymous();
        withSession.DefaultRequestHeaders.Add(SessionHeader, token);
        (await Json(await withSession.GetAsync(url), HttpStatusCode.OK)).GetProperty("project").GetProperty("name")
            .GetString().Should().Be("Residência Alfa");
        (await withSession.GetAsync($"{url}/timeline")).StatusCode.Should().Be(HttpStatusCode.OK);

        // only the hash is stored
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
            (await db.BldPortalSessions.IgnoreQueryFilters().AnyAsync(s => s.TokenHash == token)).Should().BeFalse();
            // expire it
            var hash = Nexo.Infrastructure.Modules.Build.BuildPublicPortalService.HashToken(token);
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE nexo.bld_portal_sessions SET expires_at = now() - interval '1 minute' WHERE token_hash = {hash}");
        }
        (await Json(await withSession.GetAsync(url), HttpStatusCode.Unauthorized))
            .GetProperty("code").GetString().Should().Be("portal_session_expired");

        // a fresh session dies when the company changes the password
        var second = (await Json(await anon.PostAsJsonAsync($"{url}/session", new { password }), HttpStatusCode.OK))
            .GetProperty("token").GetString()!;
        var client2 = Anonymous();
        client2.DefaultRequestHeaders.Add(SessionHeader, second);
        (await client2.GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.OK);
        var newPassword = PortalPassword();
        await Json(await admin.PutAsJsonAsync($"/api/v1/build/projects/{projectId}/portal/password", new { password = newPassword }),
            HttpStatusCode.OK);
        (await client2.GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await anon.PostAsJsonAsync($"{url}/session", new { password })).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await anon.PostAsJsonAsync($"{url}/session", new { password = newPassword })).StatusCode.Should().Be(HttpStatusCode.OK);

        // logout revokes
        var third = (await Json(await anon.PostAsJsonAsync($"{url}/session", new { password = newPassword }), HttpStatusCode.OK))
            .GetProperty("token").GetString()!;
        var client3 = Anonymous();
        client3.DefaultRequestHeaders.Add(SessionHeader, third);
        (await client3.DeleteAsync($"{url}/session")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await client3.GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Too_many_wrong_passwords_lock_the_portal()
    {
        var admin = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var projectId = await ProjectAsync(admin);
        var password = PortalPassword();
        var (company, project) = await PublishedPortalAsync(admin, projectId, access: "Password", password: password);
        var url = $"/api/public/build/{company}/{project}/session";
        var anon = Anonymous();
        for (var i = 0; i < 20; i++)
            (await anon.PostAsJsonAsync(url, new { password = "nao-e-esta" })).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var locked = await anon.PostAsJsonAsync(url, new { password });
        (await Json(locked, (HttpStatusCode)429)).GetProperty("code").GetString().Should().Be("portal_locked");
        locked.Headers.RetryAfter.Should().NotBeNull();

        (await Json(await admin.GetAsync($"/api/v1/build/projects/{projectId}/portal"), HttpStatusCode.OK))
            .GetProperty("isLocked").GetBoolean().Should().BeTrue();
    }

    // ── (3) financial visibility ──────────────────────────────────────────────

    [Fact]
    public async Task Financial_data_is_absent_unless_enabled()
    {
        var admin = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var projectId = await ProjectAsync(admin);
        await Json(await admin.PutAsJsonAsync($"/api/v1/build/projects/{projectId}", new
        {
            name = "Residência Alfa", clientName = "Cliente Portal", type = "House", location = (string?)null,
            startDate = "2026-01-05", expectedEndDate = "2026-12-20", budgetEstimated = 87654.21m, budgetApproved = 98765.43m,
        }), HttpStatusCode.OK);
        var (company, project) = await PublishedPortalAsync(admin, projectId);
        var url = $"/api/public/build/{company}/{project}";

        var hidden = await Anonymous().GetAsync(url);
        var hiddenRaw = await hidden.Content.ReadAsStringAsync();
        hidden.StatusCode.Should().Be(HttpStatusCode.OK);
        var hiddenJson = JsonDocument.Parse(hiddenRaw).RootElement;
        hiddenJson.TryGetProperty("financialSummary", out _).Should().BeFalse();
        hiddenRaw.Should().NotContain("98765").And.NotContain("87654").And.NotContainEquivalentOf("budget");

        await Json(await admin.PutAsJsonAsync($"/api/v1/build/projects/{projectId}/portal", new
        {
            slug = project, access = "Public", visibility = new { overallProgress = true, stageProgress = false, financialSummary = true },
        }), HttpStatusCode.OK);
        var shown = await Json(await Anonymous().GetAsync(url), HttpStatusCode.OK);
        var fin = shown.GetProperty("financialSummary");
        fin.GetProperty("approvedBudget").GetDecimal().Should().Be(98765.43m);
        fin.GetProperty("realizedTotal").GetDecimal().Should().Be(0m);
        fin.GetProperty("currency").GetString().Should().Be("BRL");
        shown.TryGetProperty("stages", out _).Should().BeFalse("stage progress was switched off; flags not sent keep their defaults");
    }

    // ── timeline and photos ───────────────────────────────────────────────────

    [Fact]
    public async Task Timeline_and_photos_only_show_what_was_marked_for_the_client()
    {
        var admin = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var tenantId = await AdminTenantIdAsync();
        var projectId = await ProjectAsync(admin);
        var stage = await StageAsync(admin, projectId, "Fundação");
        await Json(await admin.PutAsJsonAsync($"/api/v1/build/stages/{stage}/progress",
            new { progressPercent = 100, note = "nota interna da etapa" }), HttpStatusCode.OK);

        var secret = "SEGREDO-INTERNO-" + Guid.NewGuid().ToString("N")[..6];
        var visibleLog = (await Json(await admin.PostAsJsonAsync($"/api/v1/build/projects/{projectId}/daily-logs",
            new { date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-2)), notes = secret }), HttpStatusCode.Created))
            .GetProperty("id").GetGuid();
        var hiddenLog = (await Json(await admin.PostAsJsonAsync($"/api/v1/build/projects/{projectId}/daily-logs",
            new { date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-3)), notes = "Outro registro interno" }), HttpStatusCode.Created))
            .GetProperty("id").GetGuid();

        (await admin.PutAsJsonAsync($"/api/v1/build/daily-logs/{visibleLog}/client-visibility", new { clientVisible = true }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest, "a visible entry needs the client summary");
        await Json(await admin.PutAsJsonAsync($"/api/v1/build/daily-logs/{visibleLog}/client-visibility",
            new { clientVisible = true, clientSummary = "Concretagem da laje concluída." }), HttpStatusCode.OK);

        async Task<Guid> PhotoAsync(Guid logId, string caption)
        {
            var dto = await Json(await admin.PostAsJsonAsync($"/api/v1/build/daily-logs/{logId}/photos", new
            {
                storageKey = $"private/tenants/{tenantId}/build/daily-logs/{Guid.NewGuid():N}.jpg", caption,
            }), HttpStatusCode.OK);
            return dto.GetProperty("photos").EnumerateArray().Single(p => p.GetProperty("caption").GetString() == caption)
                .GetProperty("id").GetGuid();
        }
        var shown = await PhotoAsync(visibleLog, "Laje pronta");
        await PhotoAsync(visibleLog, "Foto interna");
        var ofHiddenLog = await PhotoAsync(hiddenLog, "Foto de log oculto");
        await Json(await admin.PutAsJsonAsync($"/api/v1/build/daily-log-photos/{shown}/client-visibility", new { clientVisible = true }),
            HttpStatusCode.OK);
        await Json(await admin.PutAsJsonAsync($"/api/v1/build/daily-log-photos/{ofHiddenLog}/client-visibility", new { clientVisible = true }),
            HttpStatusCode.OK);

        var (company, project) = await PublishedPortalAsync(admin, projectId,
            visibility: new { overallProgress = true, stageProgress = true, dailyLogs = true, photos = true, clientUpdates = true });
        var url = $"/api/public/build/{company}/{project}";
        var anon = Anonymous();

        var timelineResp = await anon.GetAsync($"{url}/timeline?take=50");
        var timelineRaw = await timelineResp.Content.ReadAsStringAsync();
        var timeline = await Json(timelineResp, HttpStatusCode.OK);
        timelineRaw.Should().NotContain(secret).And.NotContain("Outro registro interno").And.NotContain("nota interna da etapa")
            .And.NotContain("Foto interna").And.NotContain("Foto de log oculto").And.NotContain(tenantId.ToString());
        var items = timeline.GetProperty("items").EnumerateArray().ToList();
        items.Should().Contain(i => i.GetProperty("type").GetString() == "daily_log"
                                 && i.GetProperty("text").GetString() == "Concretagem da laje concluída.");
        items.Should().Contain(i => i.GetProperty("type").GetString() == "stage_completed"
                                 && i.GetProperty("stageName").GetString() == "Fundação");
        items.Where(i => i.GetProperty("type").GetString() == "photo").Should().ContainSingle()
            .Which.GetProperty("caption").GetString().Should().Be("Laje pronta");
        var occurred = items.Select(i => i.GetProperty("occurredAt").GetDateTimeOffset()).ToList();
        occurred.Should().BeInDescendingOrder();

        var paged = await Json(await anon.GetAsync($"{url}/timeline?take=1"), HttpStatusCode.OK);
        paged.GetProperty("items").GetArrayLength().Should().Be(1);
        var next = paged.GetProperty("nextBefore").GetDateTimeOffset();
        var page2 = await Json(await anon.GetAsync($"{url}/timeline?take=50&before={Uri.EscapeDataString(next.ToString("O"))}"),
            HttpStatusCode.OK);
        page2.GetProperty("items").EnumerateArray().Should()
            .OnlyContain(i => i.GetProperty("occurredAt").GetDateTimeOffset() < next);

        var photos = await Json(await anon.GetAsync($"{url}/photos"), HttpStatusCode.OK);
        photos.GetProperty("total").GetInt32().Should().Be(1);
        photos.GetProperty("items")[0].GetProperty("caption").GetString().Should().Be("Laje pronta");

        var progress = await Json(await anon.GetAsync($"{url}/progress"), HttpStatusCode.OK);
        progress.GetProperty("actualPercent").GetDecimal().Should().Be(100m);
        progress.GetProperty("curves").GetProperty("actual").GetArrayLength().Should().BeGreaterThan(0);
        progress.GetProperty("curves").TryGetProperty("forecast", out _).Should().BeFalse();
    }

    // ── (9) tenant isolation ──────────────────────────────────────────────────

    [Fact]
    public async Task Another_tenant_cannot_touch_this_tenants_portal_or_costs()
    {
        var admin = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var projectId = await ProjectAsync(admin);
        var (companyA, projectA) = await PublishedPortalAsync(admin, projectId);

        var analyzed = await Json(await admin.PostAsJsonAsync("/api/v1/interpreter/analyze",
            new { text = "areia 50 reais", inputSource = "Text" }), HttpStatusCode.OK);
        var movement = analyzed.GetProperty("draftId").GetGuid();
        await Json(await admin.PostAsJsonAsync($"/api/v1/movements/{movement}/confirm", new
        {
            amount = 50m, date = DateOnly.FromDateTime(DateTime.UtcNow), description = "Areia", direction = "Out",
            nature = "Expense", categoryId = (Guid?)null, contextType = "Obra", contextId = projectId, accountId = (Guid?)null,
            originalSuggestionId = analyzed.GetProperty("suggestionId").GetGuid(),
        }), HttpStatusCode.OK);

        var other = await OtherTenantWithBuildAsync();
        (await other.GetAsync($"/api/v1/build/projects/{projectId}/portal")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await other.PutAsJsonAsync($"/api/v1/build/projects/{projectId}/portal", new { slug = Slug("hack") }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await other.PostAsync($"/api/v1/build/projects/{projectId}/portal/unpublish", null))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await other.PutAsJsonAsync($"/api/v1/build/projects/{projectId}/portal/password", new { password = PortalPassword() }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await other.GetAsync($"/api/v1/build/projects/{projectId}/cost-by-stage")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await other.GetAsync($"/api/v1/build/projects/{projectId}/progress")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        var ownProject = await ProjectAsync(other);
        var ownStage = await StageAsync(other, ownProject, "Etapa B");
        (await other.PutAsJsonAsync($"/api/v1/build/projects/{ownProject}/movements/{movement}/stage", new { stageId = ownStage }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await other.PutAsJsonAsync($"/api/v1/build/projects/{projectId}/movements/{movement}/stage", new { stageId = ownStage }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);

        // B's company slug never opens A's project, and A's portal never shows B's data
        var (companyB, projectB) = await PublishedPortalAsync(other, ownProject);
        var anon = Anonymous();
        (await anon.GetAsync($"/api/public/build/{companyB}/{projectA}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await anon.GetAsync($"/api/public/build/{companyA}/{projectB}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        var a = await anon.GetStringAsync($"/api/public/build/{companyA}/{projectA}");
        a.Should().NotContain("Etapa B").And.NotContain("Outra Construtora");

        // A's portal survived B's attempts
        (await Json(await admin.GetAsync($"/api/v1/build/projects/{projectId}/portal"), HttpStatusCode.OK))
            .GetProperty("isPublished").GetBoolean().Should().BeTrue();
    }
}
