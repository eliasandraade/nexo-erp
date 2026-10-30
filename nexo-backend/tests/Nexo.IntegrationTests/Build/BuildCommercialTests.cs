using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nexo.Domain.Entities;
using Nexo.Domain.Modules.Build;
using Nexo.Infrastructure.Persistence;
using Nexo.IntegrationTests.Common;
using Nexo.IntegrationTests.Helpers;

namespace Nexo.IntegrationTests.Build;

/// <summary>
/// Orken Build Wave 2 "Comercial", end to end: budget kinds/versions/expiry/PDF/conversion (critical path 6),
/// change orders decided through the password portal (critical path 7), selections → change order, materials with
/// partial deliveries and movement link, measurements moving stage progress, and tenant isolation of each resource.
/// </summary>
[Collection("Integration")]
public class BuildCommercialTests
{
    private const string SessionHeader = "X-Build-Portal-Session";
    private readonly TestWebApplicationFactory _factory;

    public BuildCommercialTests(TestWebApplicationFactory factory) => _factory = factory;

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string Slug(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..(prefix.Length + 9)];
    private static string Today => DateOnly.FromDateTime(DateTime.UtcNow.AddHours(-3)).ToString("yyyy-MM-dd");
    private static string Day(int offset) => DateOnly.FromDateTime(DateTime.UtcNow.AddHours(-3)).AddDays(offset).ToString("yyyy-MM-dd");

    private static async Task<JsonElement> Json(HttpResponseMessage r, HttpStatusCode expected)
    {
        var body = await r.Content.ReadAsStringAsync();
        r.StatusCode.Should().Be(expected, body);
        return string.IsNullOrWhiteSpace(body) ? default : JsonDocument.Parse(body).RootElement.Clone();
    }

    private static async Task<string> Code(HttpResponseMessage r, HttpStatusCode expected)
        => (await Json(r, expected)).GetProperty("code").GetString()!;

    private static async Task<Guid> ProjectAsync(HttpClient c, string name = "Residência Comercial")
    {
        var r = await c.PostAsJsonAsync("/api/v1/build/projects", new
        {
            name, clientName = "Cliente Comercial", type = "House", startDate = "2026-01-05", expectedEndDate = "2026-12-20",
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
            slug, displayName = "Construtora Comercial", contactEmail = "contato@" + TestCredentials.TestDomain,
        }), HttpStatusCode.OK);
        return slug;
    }

    /// <summary>Publishes the project's portal under <paramref name="company"/>; with a password when one is given.</summary>
    private static async Task<string> PortalAsync(HttpClient c, string company, Guid projectId, string? password = null,
        object? visibility = null)
    {
        var slug = Slug("obra");
        await Json(await c.PutAsJsonAsync($"/api/v1/build/projects/{projectId}/portal", new
        {
            slug, access = "Public", title = "Acompanhe sua obra", visibility,
        }), HttpStatusCode.OK);
        if (password is not null)
        {
            await Json(await c.PutAsJsonAsync($"/api/v1/build/projects/{projectId}/portal/password", new { password }), HttpStatusCode.OK);
            await Json(await c.PutAsJsonAsync($"/api/v1/build/projects/{projectId}/portal", new
            {
                slug, access = "Password", title = "Acompanhe sua obra", visibility,
            }), HttpStatusCode.OK);
        }
        await Json(await c.PostAsync($"/api/v1/build/projects/{projectId}/portal/publish", null), HttpStatusCode.OK);
        return $"/api/public/build/{company}/{slug}";
    }

    private async Task<HttpClient> SessionAsync(string portalUrl, string password)
    {
        var token = (await Json(await Anonymous().PostAsJsonAsync($"{portalUrl}/session", new { password }), HttpStatusCode.OK))
            .GetProperty("token").GetString()!;
        var client = Anonymous();
        client.DefaultRequestHeaders.Add(SessionHeader, token);
        return client;
    }

    private HttpClient Anonymous() => AuthClientFactory.CreateAnonymousClient(_factory);

    private static string PortalPassword() => "Px!" + Guid.NewGuid().ToString("N")[..12];

    private async Task<HttpClient> OtherTenantWithBuildAsync()
    {
        var email = $"bld2-{Guid.NewGuid():N}"[..17] + "@" + TestCredentials.TestDomain;
        var reg = await _factory.CreateApiClient().PostAsJsonAsync("/api/auth/register",
            new { name = "Outra Construtora W2", email, password = TestCredentials.AdminPassword });
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

    /// <summary>A confirmed outgoing movement of the obra (through the interpreter, like the financial UI does).</summary>
    private static async Task<Guid> MovementAsync(HttpClient c, Guid projectId, decimal amount)
    {
        var analyzed = await Json(await c.PostAsJsonAsync("/api/v1/interpreter/analyze",
            new { text = $"cimento {amount} reais", inputSource = "Text" }), HttpStatusCode.OK);
        var movement = analyzed.GetProperty("draftId").GetGuid();
        await Json(await c.PostAsJsonAsync($"/api/v1/movements/{movement}/confirm", new
        {
            amount, date = DateOnly.FromDateTime(DateTime.UtcNow), description = "Cimento", direction = "Out",
            nature = "Expense", categoryId = (Guid?)null, contextType = "Obra", contextId = projectId, accountId = (Guid?)null,
            originalSuggestionId = analyzed.GetProperty("suggestionId").GetGuid(),
        }), HttpStatusCode.OK);
        return movement;
    }

    private static async Task<(Guid Id, decimal FinalPrice)> ApprovedBudgetAsync(HttpClient c, Guid projectId,
        params (string Name, string Kind, decimal Qty, decimal UnitCost, Guid? StageId)[] items)
    {
        var budget = (await Json(await c.PostAsJsonAsync("/api/v1/build/budgets", new { name = "Contrato", projectId, marginPercent = 0m }),
            HttpStatusCode.Created)).GetProperty("id").GetGuid();
        foreach (var i in items)
            await Json(await c.PostAsJsonAsync($"/api/v1/build/budgets/{budget}/items", new
            {
                name = i.Name, category = i.Kind, kind = i.Kind, quantity = i.Qty, unit = "un", unitCost = i.UnitCost, stageId = i.StageId,
            }), HttpStatusCode.OK);
        var approved = await Json(await c.PostAsync($"/api/v1/build/budgets/{budget}/approve", null), HttpStatusCode.OK);
        return (budget, approved.GetProperty("finalPrice").GetDecimal());
    }

    // ── Critical path 6: budget ───────────────────────────────────────────────

    [Fact]
    public async Task Critical_path_6_budget_with_kinds_is_sent_approved_versioned_and_converted_into_the_project()
    {
        var admin = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var projectId = await ProjectAsync(admin, "Casa CP6");

        var created = await Json(await admin.PostAsJsonAsync("/api/v1/build/budgets", new
        {
            name = "Casa térrea 100 m²", marginPercent = 10m, validUntil = Day(30), notes = "Pagamento em 3 parcelas.",
            internalNotes = "INTERNO-NAO-MOSTRAR", areaM2 = 100m,
        }), HttpStatusCode.Created);
        var budget = created.GetProperty("id").GetGuid();
        created.GetProperty("version").GetInt32().Should().Be(1);

        await Json(await admin.PostAsJsonAsync($"/api/v1/build/budgets/{budget}/items", new
        {
            name = "Concreto", category = "Material", kind = "Material", quantity = 10m, unit = "m³", unitCost = 500m, stageName = "Fundação",
        }), HttpStatusCode.OK);
        await Json(await admin.PostAsJsonAsync($"/api/v1/build/budgets/{budget}/items", new
        {
            name = "Pedreiro", category = "Mão de obra", kind = "Labor", quantity = 20m, unit = "dia", unitCost = 200m, stageName = "Alvenaria",
        }), HttpStatusCode.OK);
        var withItems = await Json(await admin.PostAsJsonAsync($"/api/v1/build/budgets/{budget}/items", new
        {
            name = "Taxas", category = "Outros", quantity = 1m, unit = "vb", unitCost = 1000m,   // no kind → Other (legacy clients)
        }), HttpStatusCode.OK);

        var totals = withItems.GetProperty("totals");
        totals.GetProperty("material").GetDecimal().Should().Be(5000m);
        totals.GetProperty("labor").GetDecimal().Should().Be(4000m);
        totals.GetProperty("other").GetDecimal().Should().Be(1000m);
        totals.GetProperty("subtotal").GetDecimal().Should().Be(10000m);
        totals.GetProperty("total").GetDecimal().Should().Be(11000m);
        totals.GetProperty("totalPerM2").GetDecimal().Should().Be(110m);
        withItems.GetProperty("costPerM2").GetDecimal().Should().Be(100m);
        withItems.GetProperty("finalPrice").GetDecimal().Should().Be(11000m);
        withItems.GetProperty("isExpired").GetBoolean().Should().BeFalse();

        // PDF: application/pdf, a real PDF
        var pdf = await admin.GetAsync($"/api/v1/build/budgets/{budget}/pdf");
        pdf.StatusCode.Should().Be(HttpStatusCode.OK, await pdf.Content.ReadAsStringAsync());
        pdf.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");
        var bytes = await pdf.Content.ReadAsByteArrayAsync();
        bytes.Length.Should().BeGreaterThan(500);
        System.Text.Encoding.ASCII.GetString(bytes, 0, 5).Should().Be("%PDF-");

        await Json(await admin.PostAsync($"/api/v1/build/budgets/{budget}/send", null), HttpStatusCode.OK);
        var approved = await Json(await admin.PostAsync($"/api/v1/build/budgets/{budget}/approve", null), HttpStatusCode.OK);
        approved.GetProperty("status").GetString().Should().Be("Approved");
        approved.GetProperty("approvedByName").GetString().Should().NotBeNullOrWhiteSpace();
        approved.GetProperty("approvedAt").ValueKind.Should().NotBe(JsonValueKind.Null);

        // a new version keeps the approved one untouched
        var v2 = await Json(await admin.PostAsync($"/api/v1/build/budgets/{budget}/versions", null), HttpStatusCode.Created);
        v2.GetProperty("version").GetInt32().Should().Be(2);
        v2.GetProperty("status").GetString().Should().Be("Draft");
        v2.GetProperty("parentBudgetId").GetGuid().Should().Be(budget);
        v2.GetProperty("items").GetArrayLength().Should().Be(3);
        v2.GetProperty("finalPrice").GetDecimal().Should().Be(11000m);
        var versions = await Json(await admin.GetAsync($"/api/v1/build/budgets/{v2.GetProperty("id").GetGuid()}/versions"), HttpStatusCode.OK);
        versions.EnumerateArray().Select(v => v.GetProperty("version").GetInt32()).Should().Equal(1, 2);
        var original = await Json(await admin.GetAsync($"/api/v1/build/budgets/{budget}"), HttpStatusCode.OK);
        original.GetProperty("status").GetString().Should().Be("Approved");
        original.GetProperty("version").GetInt32().Should().Be(1);

        // conversion through the existing endpoint: project gets the price and one stage per item stage name
        var converted = await Json(await admin.PostAsJsonAsync($"/api/v1/build/budgets/{budget}/convert", new { projectId }), HttpStatusCode.OK);
        converted.GetProperty("status").GetString().Should().Be("Converted");
        converted.GetProperty("items").EnumerateArray().Where(i => i.GetProperty("stageName").GetString() is not null)
            .Should().OnlyContain(i => i.GetProperty("stageId").ValueKind == JsonValueKind.String);
        var project = await Json(await admin.GetAsync($"/api/v1/build/projects/{projectId}"), HttpStatusCode.OK);
        project.GetProperty("budgetApproved").GetDecimal().Should().Be(11000m);
        var stages = await Json(await admin.GetAsync($"/api/v1/build/projects/{projectId}/stages"), HttpStatusCode.OK);
        stages.EnumerateArray().Select(s => s.GetProperty("name").GetString()).Should().Equal("Fundação", "Alvenaria");

        // cost per stage now has the budgeted amounts
        var cost = await Json(await admin.GetAsync($"/api/v1/build/projects/{projectId}/cost-by-stage"), HttpStatusCode.OK);
        cost.GetProperty("stages").EnumerateArray().Single(s => s.GetProperty("stageName").GetString() == "Fundação")
            .GetProperty("budgeted").GetDecimal().Should().Be(5000m);
    }

    [Fact]
    public async Task An_expired_budget_cannot_be_approved_and_a_rejection_keeps_the_reason()
    {
        var admin = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var budget = (await Json(await admin.PostAsJsonAsync("/api/v1/build/budgets", new { name = "Vencido", validUntil = Day(-1) }),
            HttpStatusCode.Created)).GetProperty("id").GetGuid();
        await Json(await admin.PostAsJsonAsync($"/api/v1/build/budgets/{budget}/items", new
        {
            name = "Item", category = "Material", kind = "Material", quantity = 1m, unit = "un", unitCost = 10m,
        }), HttpStatusCode.OK);
        var sent = await Json(await admin.PostAsync($"/api/v1/build/budgets/{budget}/send", null), HttpStatusCode.OK);
        sent.GetProperty("isExpired").GetBoolean().Should().BeTrue();
        (await Code(await admin.PostAsync($"/api/v1/build/budgets/{budget}/approve", null), HttpStatusCode.UnprocessableEntity))
            .Should().Be("budget_expired");

        var rejected = await Json(await admin.PostAsJsonAsync($"/api/v1/build/budgets/{budget}/reject", new { reason = "Prazo vencido" }),
            HttpStatusCode.OK);
        rejected.GetProperty("rejectionReason").GetString().Should().Be("Prazo vencido");

        // a new version with a new validity can be approved
        var v2 = (await Json(await admin.PostAsync($"/api/v1/build/budgets/{budget}/versions", null), HttpStatusCode.Created))
            .GetProperty("id").GetGuid();
        await Json(await admin.PutAsJsonAsync($"/api/v1/build/budgets/{v2}/terms", new { validUntil = Day(15) }), HttpStatusCode.OK);
        await Json(await admin.PostAsync($"/api/v1/build/budgets/{v2}/send", null), HttpStatusCode.OK);
        (await Json(await admin.PostAsync($"/api/v1/build/budgets/{v2}/approve", null), HttpStatusCode.OK))
            .GetProperty("status").GetString().Should().Be("Approved");
    }

    [Fact]
    public async Task Templates_scale_per_square_meter_and_round_trip_from_a_budget()
    {
        var admin = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var template = await Json(await admin.PostAsJsonAsync("/api/v1/build/budget-templates", new
        {
            name = "Casa padrão", defaultMarginPercent = 15m,
            items = new object[]
            {
                new { name = "Piso", kind = "Material", unit = "m²", quantityPerM2 = 1.1m, unitCost = 50m, stageName = "Acabamento" },
                new { name = "Projeto", kind = "Service", unit = "un", fixedQuantity = 1m, unitCost = 2000m },
            },
        }), HttpStatusCode.Created);
        var templateId = template.GetProperty("id").GetGuid();

        (await Code(await admin.PostAsJsonAsync($"/api/v1/build/budget-templates/{templateId}/budgets", new { name = "Sem área" }),
            HttpStatusCode.UnprocessableEntity)).Should().Be("template_area_required");

        var budget = await Json(await admin.PostAsJsonAsync($"/api/v1/build/budget-templates/{templateId}/budgets",
            new { name = "Casa 120 m²", areaM2 = 120m }), HttpStatusCode.Created);
        budget.GetProperty("sourceTemplateId").GetGuid().Should().Be(templateId);
        budget.GetProperty("marginPercent").GetDecimal().Should().Be(15m);
        var piso = budget.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("name").GetString() == "Piso");
        piso.GetProperty("quantity").GetDecimal().Should().Be(132m);
        piso.GetProperty("kind").GetString().Should().Be("Material");
        piso.GetProperty("stageName").GetString().Should().Be("Acabamento");
        budget.GetProperty("totalCost").GetDecimal().Should().Be(132m * 50m + 2000m);

        var saved = await Json(await admin.PostAsJsonAsync($"/api/v1/build/budgets/{budget.GetProperty("id").GetGuid()}/save-as-template",
            new { name = "Derivado", perSquareMeter = true }), HttpStatusCode.OK);
        saved.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("name").GetString() == "Piso")
            .GetProperty("quantityPerM2").GetDecimal().Should().Be(1.1m);

        var dup = await Json(await admin.PostAsync($"/api/v1/build/budgets/{budget.GetProperty("id").GetGuid()}/duplicate", null),
            HttpStatusCode.Created);
        dup.GetProperty("items").GetArrayLength().Should().Be(2);
        dup.GetProperty("version").GetInt32().Should().Be(1);

        (await admin.DeleteAsync($"/api/v1/build/budget-templates/{templateId}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await admin.GetAsync($"/api/v1/build/budget-templates/{templateId}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── Critical path 7: change orders ────────────────────────────────────────

    [Fact]
    public async Task Critical_path_7_change_order_decided_by_the_client_updates_contract_and_schedule()
    {
        var admin = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var projectId = await ProjectAsync(admin, "Obra CP7");
        var fundacao = await StageAsync(admin, projectId, "Fundação");
        await StageAsync(admin, projectId, "Alvenaria");
        var (_, contract) = await ApprovedBudgetAsync(admin, projectId, ("Concreto", "Material", 10m, 1000m, fundacao));
        contract.Should().Be(10000m);
        await Json(await admin.PostAsJsonAsync($"/api/v1/build/projects/{projectId}/schedule/baseline", new { }), HttpStatusCode.Created);

        var co = await Json(await admin.PostAsJsonAsync($"/api/v1/build/projects/{projectId}/change-orders", new
        {
            title = "Fundação mais profunda", description = "Solo mais fraco que o previsto.", reason = "SiteCondition",
            scheduleImpactDays = 10, stageId = fundacao,
            items = new object[] { new { name = "Concreto extra", kind = "Material", quantity = 5m, unit = "m³", unitCost = 1000m } },
        }), HttpStatusCode.Created);
        co.GetProperty("number").GetInt32().Should().Be(1);
        co.GetProperty("costImpact").GetDecimal().Should().Be(5000m, "no explicit cost → Σ items");
        var coId = co.GetProperty("id").GetGuid();
        var publicId = co.GetProperty("publicId").GetGuid();
        publicId.Should().NotBe(coId);

        var company = await CompanyAsync(admin);
        var password = PortalPassword();
        var portal = await PortalAsync(admin, company, projectId, password);
        var client = await SessionAsync(portal, password);

        // drafts never reach the client
        (await Json(await client.GetAsync($"{portal}/change-orders"), HttpStatusCode.OK)).GetProperty("items").GetArrayLength().Should().Be(0);
        await Json(await admin.PostAsync($"/api/v1/build/change-orders/{coId}/send", null), HttpStatusCode.OK);

        var list = await Json(await client.GetAsync($"{portal}/change-orders"), HttpStatusCode.OK);
        var pending = list.GetProperty("items").EnumerateArray().Single();
        pending.GetProperty("id").GetGuid().Should().Be(publicId);
        pending.GetProperty("status").GetString().Should().Be("Pending");
        pending.GetProperty("canDecide").GetBoolean().Should().BeTrue();
        pending.GetProperty("stageName").GetString().Should().Be("Fundação");
        pending.TryGetProperty("createdByName", out _).Should().BeFalse();

        // without a session the decision is refused like any password-portal call
        (await Code(await Anonymous().PostAsJsonAsync($"{portal}/change-orders/{publicId}/decision",
            new { decision = "approve", signerName = "Maria" }), HttpStatusCode.Unauthorized)).Should().Be("portal_password_required");
        (await Code(await client.PostAsJsonAsync($"{portal}/change-orders/{publicId}/decision",
            new { decision = "talvez", signerName = "Maria" }), HttpStatusCode.BadRequest)).Should().Be("invalid_decision");
        (await Code(await client.PostAsJsonAsync($"{portal}/change-orders/{publicId}/decision",
            new { decision = "approve" }), HttpStatusCode.BadRequest)).Should().Be("signer_name_required");

        var decided = await Json(await client.PostAsJsonAsync($"{portal}/change-orders/{publicId}/decision",
            new { decision = "approve", comment = "De acordo.", signerName = "Maria Cliente" }), HttpStatusCode.OK);
        decided.GetProperty("status").GetString().Should().Be("Approved");
        decided.GetProperty("decisionChannel").GetString().Should().Be("Portal");
        decided.GetProperty("canDecide").GetBoolean().Should().BeFalse();

        // idempotent: a second decision is a conflict
        (await Code(await client.PostAsJsonAsync($"{portal}/change-orders/{publicId}/decision",
            new { decision = "reject", signerName = "Maria Cliente" }), HttpStatusCode.Conflict)).Should().Be("change_order_already_decided");

        // effects: contract, stage end, schedule revision
        var internalCo = await Json(await admin.GetAsync($"/api/v1/build/change-orders/{coId}"), HttpStatusCode.OK);
        internalCo.GetProperty("decidedBy").GetString().Should().Be("Cliente (portal)");
        internalCo.GetProperty("signerName").GetString().Should().Be("Maria Cliente");
        internalCo.GetProperty("appliedToBudget").GetBoolean().Should().BeTrue();
        var summary = await Json(await admin.GetAsync($"/api/v1/build/projects/{projectId}/financial-summary"), HttpStatusCode.OK);
        summary.GetProperty("approvedBudget").GetDecimal().Should().Be(15000m);
        summary.GetProperty("approvedChangeOrdersTotal").GetDecimal().Should().Be(5000m);
        summary.GetProperty("contractTotal").GetDecimal().Should().Be(15000m);
        var stage = (await Json(await admin.GetAsync($"/api/v1/build/projects/{projectId}/stages"), HttpStatusCode.OK))
            .EnumerateArray().Single(s => s.GetProperty("id").GetGuid() == fundacao);
        stage.GetProperty("plannedEndDate").GetString().Should().Be("2026-04-10");
        var revisions = await Json(await admin.GetAsync($"/api/v1/build/projects/{projectId}/schedule/revisions"), HttpStatusCode.OK);
        var last = revisions.EnumerateArray().Last();
        last.GetProperty("number").GetInt32().Should().Be(2);
        last.GetProperty("kind").GetString().Should().Be("Revision");
        last.GetProperty("reason").GetString().Should().Be("Aditivo #1 aprovado");

        // the company cannot decide it again either
        (await admin.PostAsync($"/api/v1/build/change-orders/{coId}/reject", null)).StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Change_orders_cannot_be_decided_on_a_public_portal_nor_across_projects_or_tenants()
    {
        var admin = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var company = await CompanyAsync(admin);

        // public portal → 403
        var publicProject = await ProjectAsync(admin, "Obra pública");
        var publicCo = await Json(await admin.PostAsJsonAsync($"/api/v1/build/projects/{publicProject}/change-orders",
            new { title = "Pintura extra", costImpact = 800m }), HttpStatusCode.Created);
        await Json(await admin.PostAsync($"/api/v1/build/change-orders/{publicCo.GetProperty("id").GetGuid()}/send", null), HttpStatusCode.OK);
        var publicPortal = await PortalAsync(admin, company, publicProject);
        var listed = await Json(await Anonymous().GetAsync($"{publicPortal}/change-orders"), HttpStatusCode.OK);
        listed.GetProperty("items")[0].GetProperty("canDecide").GetBoolean().Should().BeFalse();
        (await Code(await Anonymous().PostAsJsonAsync($"{publicPortal}/change-orders/{publicCo.GetProperty("publicId").GetGuid()}/decision",
            new { decision = "approve", signerName = "Maria" }), HttpStatusCode.Forbidden)).Should().Be("portal_decision_requires_password");

        // a session of project X cannot decide a change order of project Y
        var password = PortalPassword();
        var x = await ProjectAsync(admin, "Obra X");
        var y = await ProjectAsync(admin, "Obra Y");
        var portalX = await PortalAsync(admin, company, x, password);
        var portalY = await PortalAsync(admin, company, y, password);
        var coY = await Json(await admin.PostAsJsonAsync($"/api/v1/build/projects/{y}/change-orders",
            new { title = "Aditivo de Y", costImpact = 100m }), HttpStatusCode.Created);
        var coYId = coY.GetProperty("id").GetGuid();
        await Json(await admin.PostAsync($"/api/v1/build/change-orders/{coYId}/send", null), HttpStatusCode.OK);
        var sessionX = await SessionAsync(portalX, password);
        (await Code(await sessionX.PostAsJsonAsync($"{portalX}/change-orders/{coY.GetProperty("publicId").GetGuid()}/decision",
            new { decision = "approve", signerName = "Intruso" }), HttpStatusCode.NotFound)).Should().Be("portal_item_not_found");
        (await Code(await sessionX.PostAsJsonAsync($"{portalY}/change-orders/{coY.GetProperty("publicId").GetGuid()}/decision",
            new { decision = "approve", signerName = "Intruso" }), HttpStatusCode.Unauthorized)).Should().Be("portal_session_expired");
        (await Json(await admin.GetAsync($"/api/v1/build/change-orders/{coYId}"), HttpStatusCode.OK))
            .GetProperty("status").GetString().Should().Be("SentToClient");

        // tenant B never sees nor decides tenant A's change order
        var other = await OtherTenantWithBuildAsync();
        (await other.GetAsync($"/api/v1/build/change-orders/{coYId}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await other.PostAsync($"/api/v1/build/change-orders/{coYId}/approve", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await other.PutAsJsonAsync($"/api/v1/build/change-orders/{coYId}", new { title = "x" })).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await other.GetAsync($"/api/v1/build/projects/{y}/change-orders")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await Json(await admin.GetAsync($"/api/v1/build/change-orders/{coYId}"), HttpStatusCode.OK))
            .GetProperty("status").GetString().Should().Be("SentToClient");
    }

    // ── Selections ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Selection_chosen_in_the_portal_and_confirmed_creates_a_draft_change_order()
    {
        var admin = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var projectId = await ProjectAsync(admin, "Obra escolhas");
        var acabamento = await StageAsync(admin, projectId, "Acabamento");
        var selection = await Json(await admin.PostAsJsonAsync($"/api/v1/build/projects/{projectId}/selections",
            new { title = "Piso da sala", stageId = acabamento, deadline = Day(10) }), HttpStatusCode.Created);
        var selectionId = selection.GetProperty("id").GetGuid();
        await Json(await admin.PostAsJsonAsync($"/api/v1/build/selections/{selectionId}/options",
            new { name = "Cerâmica padrão", priceDelta = 0m, isStandard = true }), HttpStatusCode.OK);
        var withOptions = await Json(await admin.PostAsJsonAsync($"/api/v1/build/selections/{selectionId}/options",
            new { name = "Porcelanato 90x90", priceDelta = 2500m, leadTimeDays = 15 }), HttpStatusCode.OK);
        withOptions.GetProperty("options").GetArrayLength().Should().Be(2);

        var hidden = await Json(await admin.PostAsJsonAsync($"/api/v1/build/projects/{projectId}/selections",
            new { title = "Interno", clientVisible = false }), HttpStatusCode.Created);
        var late = await Json(await admin.PostAsJsonAsync($"/api/v1/build/projects/{projectId}/selections",
            new { title = "Bancada", deadline = Day(-1) }), HttpStatusCode.Created);
        await Json(await admin.PostAsJsonAsync($"/api/v1/build/selections/{late.GetProperty("id").GetGuid()}/options",
            new { name = "Granito" }), HttpStatusCode.OK);

        var company = await CompanyAsync(admin);
        var password = PortalPassword();
        var portal = await PortalAsync(admin, company, projectId, password);
        var client = await SessionAsync(portal, password);

        var list = await Json(await client.GetAsync($"{portal}/selections"), HttpStatusCode.OK);
        list.GetProperty("items").EnumerateArray().Select(s => s.GetProperty("title").GetString())
            .Should().BeEquivalentTo("Piso da sala", "Bancada");
        hidden.GetProperty("clientVisible").GetBoolean().Should().BeFalse();
        var piso = list.GetProperty("items").EnumerateArray().Single(s => s.GetProperty("title").GetString() == "Piso da sala");
        piso.GetProperty("canChoose").GetBoolean().Should().BeTrue();
        piso.GetProperty("id").GetGuid().Should().Be(selection.GetProperty("publicId").GetGuid());
        var premium = piso.GetProperty("options").EnumerateArray().Single(o => o.GetProperty("name").GetString() == "Porcelanato 90x90");
        premium.GetProperty("priceDelta").GetDecimal().Should().Be(2500m);

        var bancada = list.GetProperty("items").EnumerateArray().Single(s => s.GetProperty("title").GetString() == "Bancada");
        bancada.GetProperty("deadlinePassed").GetBoolean().Should().BeTrue();
        (await Code(await client.PostAsJsonAsync($"{portal}/selections/{bancada.GetProperty("id").GetGuid()}/choice",
            new { optionId = bancada.GetProperty("options")[0].GetProperty("id").GetGuid() }), HttpStatusCode.UnprocessableEntity))
            .Should().Be("selection_deadline_passed");
        (await Code(await client.PostAsJsonAsync($"{portal}/selections/{piso.GetProperty("id").GetGuid()}/choice",
            new { optionId = Guid.NewGuid() }), HttpStatusCode.BadRequest)).Should().Be("invalid_option");

        var chosen = await Json(await client.PostAsJsonAsync($"{portal}/selections/{piso.GetProperty("id").GetGuid()}/choice",
            new { optionId = premium.GetProperty("id").GetGuid(), comment = "Quero o porcelanato." }), HttpStatusCode.OK);
        chosen.GetProperty("status").GetString().Should().Be("Submitted");
        chosen.GetProperty("chosenOptionId").GetGuid().Should().Be(premium.GetProperty("id").GetGuid());
        (await Code(await client.PostAsJsonAsync($"{portal}/selections/{piso.GetProperty("id").GetGuid()}/choice",
            new { optionId = premium.GetProperty("id").GetGuid() }), HttpStatusCode.Conflict)).Should().Be("selection_closed");

        var confirmed = await Json(await admin.PostAsync($"/api/v1/build/selections/{selectionId}/confirm", null), HttpStatusCode.OK);
        confirmed.GetProperty("status").GetString().Should().Be("Confirmed");
        confirmed.GetProperty("chosenVia").GetString().Should().Be("Portal");
        var coId = confirmed.GetProperty("changeOrderId").GetGuid();
        var co = await Json(await admin.GetAsync($"/api/v1/build/change-orders/{coId}"), HttpStatusCode.OK);
        co.GetProperty("status").GetString().Should().Be("Draft");
        co.GetProperty("costImpact").GetDecimal().Should().Be(2500m);
        co.GetProperty("scheduleImpactDays").GetInt32().Should().Be(15);
        co.GetProperty("stageId").GetGuid().Should().Be(acabamento);
        co.GetProperty("sourceSelectionId").GetGuid().Should().Be(selectionId);
        co.GetProperty("reason").GetString().Should().Be("ClientRequest");

        (await admin.PostAsync($"/api/v1/build/selections/{selectionId}/confirm", null)).StatusCode.Should().Be(HttpStatusCode.Conflict);

        // a public portal shows selections but the client cannot choose there
        var publicProject = await ProjectAsync(admin, "Obra escolhas pública");
        var sel2 = (await Json(await admin.PostAsJsonAsync($"/api/v1/build/projects/{publicProject}/selections",
            new { title = "Louças" }), HttpStatusCode.Created)).GetProperty("publicId").GetGuid();
        var publicPortal = await PortalAsync(admin, company, publicProject);
        (await Code(await Anonymous().PostAsJsonAsync($"{publicPortal}/selections/{sel2}/choice", new { optionId = Guid.NewGuid() }),
            HttpStatusCode.Forbidden)).Should().Be("portal_decision_requires_password");
    }

    // ── Materials ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Material_lifecycle_with_partial_deliveries_late_flag_and_movement_link()
    {
        var admin = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var projectId = await ProjectAsync(admin, "Obra materiais");
        var otherProject = await ProjectAsync(admin, "Outra obra materiais");
        var stage = await StageAsync(admin, projectId, "Estrutura");

        var created = await Json(await admin.PostAsJsonAsync($"/api/v1/build/projects/{projectId}/materials",
            new { materialName = "Cimento CP-II", unit = "saco", quantity = 100m, stageId = stage, neededBy = Day(-2) }), HttpStatusCode.Created);
        var id = created.GetProperty("id").GetGuid();
        created.GetProperty("status").GetString().Should().Be("Requested");
        created.GetProperty("isLate").GetBoolean().Should().BeTrue();
        (await Json(await admin.GetAsync($"/api/v1/build/projects/{projectId}/materials?late=true"), HttpStatusCode.OK))
            .EnumerateArray().Should().Contain(m => m.GetProperty("id").GetGuid() == id);

        (await Code(await admin.PostAsJsonAsync($"/api/v1/build/materials/{id}/deliveries", new { quantity = 10m }),
            HttpStatusCode.UnprocessableEntity)).Should().Be("material_not_ordered");
        (await Json(await admin.PostAsJsonAsync($"/api/v1/build/materials/{id}/quote", new { estimatedUnitCost = 32.5m }), HttpStatusCode.OK))
            .GetProperty("estimatedTotal").GetDecimal().Should().Be(3250m);
        (await Json(await admin.PostAsync($"/api/v1/build/materials/{id}/order", null), HttpStatusCode.OK))
            .GetProperty("status").GetString().Should().Be("Ordered");

        var partial = await Json(await admin.PostAsJsonAsync($"/api/v1/build/materials/{id}/deliveries",
            new { quantity = 40m, receivedBy = "Mestre João", note = "Primeira carga" }), HttpStatusCode.OK);
        partial.GetProperty("status").GetString().Should().Be("PartiallyDelivered");
        partial.GetProperty("remainingQuantity").GetDecimal().Should().Be(60m);
        (await Code(await admin.PostAsJsonAsync($"/api/v1/build/materials/{id}/deliveries", new { quantity = 70m }),
            HttpStatusCode.UnprocessableEntity)).Should().Be("delivery_exceeds_quantity");
        var done = await Json(await admin.PostAsJsonAsync($"/api/v1/build/materials/{id}/deliveries", new { quantity = 60m }), HttpStatusCode.OK);
        done.GetProperty("status").GetString().Should().Be("Delivered");
        done.GetProperty("isLate").GetBoolean().Should().BeFalse();
        done.GetProperty("deliveries").EnumerateArray().Select(d => d.GetProperty("quantity").GetDecimal()).Should().Equal(40m, 60m);
        (await Json(await admin.GetAsync($"/api/v1/build/projects/{projectId}/materials?status=Delivered&stageId={stage}"), HttpStatusCode.OK))
            .EnumerateArray().Should().ContainSingle(m => m.GetProperty("id").GetGuid() == id);

        // movement link: only a movement of this obra
        var foreignMovement = await MovementAsync(admin, otherProject, 50m);
        (await Code(await admin.PutAsJsonAsync($"/api/v1/build/materials/{id}/movement", new { movementId = foreignMovement }),
            HttpStatusCode.UnprocessableEntity)).Should().Be("movement_not_of_project");
        var ownMovement = await MovementAsync(admin, projectId, 3250m);
        (await Json(await admin.PutAsJsonAsync($"/api/v1/build/materials/{id}/movement", new { movementId = ownMovement }), HttpStatusCode.OK))
            .GetProperty("movementId").GetGuid().Should().Be(ownMovement);
        (await Json(await admin.PutAsJsonAsync($"/api/v1/build/materials/{id}/movement", new { movementId = (Guid?)null }), HttpStatusCode.OK))
            .GetProperty("movementId").ValueKind.Should().Be(JsonValueKind.Null);

        var cancelled = await Json(await admin.PostAsJsonAsync($"/api/v1/build/projects/{projectId}/materials",
            new { materialName = "Areia", unit = "m³", quantity = 5m }), HttpStatusCode.Created);
        (await Json(await admin.PostAsync($"/api/v1/build/materials/{cancelled.GetProperty("id").GetGuid()}/cancel", null), HttpStatusCode.OK))
            .GetProperty("status").GetString().Should().Be("Cancelled");
    }

    // ── Measurements ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Measurement_approval_moves_stage_progress_through_the_progress_history()
    {
        var admin = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var projectId = await ProjectAsync(admin, "Obra medições");
        var s1 = await StageAsync(admin, projectId, "Fundação", 2m);
        var s2 = await StageAsync(admin, projectId, "Alvenaria");
        await ApprovedBudgetAsync(admin, projectId, ("Concreto", "Material", 10m, 2000m, s1), ("Blocos", "Material", 1m, 5000m, s2));
        await Json(await admin.PutAsJsonAsync($"/api/v1/build/stages/{s2}/progress", new { progressPercent = 30 }), HttpStatusCode.OK);

        (await Code(await admin.PostAsJsonAsync($"/api/v1/build/projects/{projectId}/measurements", new
        {
            periodStart = Day(-30), periodEnd = Today, items = new[] { new { stageId = s2, measuredPercent = 20 } },
        }), HttpStatusCode.UnprocessableEntity)).Should().Be("measurement_below_previous");
        (await admin.PostAsJsonAsync($"/api/v1/build/projects/{projectId}/measurements", new
        {
            periodStart = Day(-30), periodEnd = Today, items = new[] { new { stageId = s1, measuredPercent = 101 } },
        })).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var m = await Json(await admin.PostAsJsonAsync($"/api/v1/build/projects/{projectId}/measurements", new
        {
            periodStart = Day(-30), periodEnd = Today, notes = "Medição mensal",
            items = new object[] { new { stageId = s1, measuredPercent = 40, amount = 8000m }, new { stageId = s2, measuredPercent = 50 } },
        }), HttpStatusCode.Created);
        var measurementId = m.GetProperty("id").GetGuid();
        m.GetProperty("number").GetInt32().Should().Be(1);
        var item1 = m.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("stageId").GetGuid() == s1);
        item1.GetProperty("previousPercent").GetInt32().Should().Be(0);
        item1.GetProperty("suggestedAmount").GetDecimal().Should().Be(8000m);   // 40% × 20 000 budgeted
        m.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("stageId").GetGuid() == s2)
            .GetProperty("suggestedAmount").GetDecimal().Should().Be(1000m);    // (50 − 30)% × 5 000

        // nothing moved before approval
        (await Json(await admin.GetAsync($"/api/v1/build/projects/{projectId}/stages"), HttpStatusCode.OK)).EnumerateArray()
            .Single(s => s.GetProperty("id").GetGuid() == s1).GetProperty("progressPercent").GetInt32().Should().Be(0);

        await Json(await admin.PostAsync($"/api/v1/build/measurements/{measurementId}/submit", null), HttpStatusCode.OK);
        var approved = await Json(await admin.PostAsync($"/api/v1/build/measurements/{measurementId}/approve", null), HttpStatusCode.OK);
        approved.GetProperty("status").GetString().Should().Be("Approved");
        (await admin.PostAsync($"/api/v1/build/measurements/{measurementId}/approve", null)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await admin.PutAsJsonAsync($"/api/v1/build/measurements/{measurementId}", new { periodStart = Today, periodEnd = Today, items = Array.Empty<object>() }))
            .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        var stages = await Json(await admin.GetAsync($"/api/v1/build/projects/{projectId}/stages"), HttpStatusCode.OK);
        stages.EnumerateArray().Single(s => s.GetProperty("id").GetGuid() == s1).GetProperty("progressPercent").GetInt32().Should().Be(40);
        stages.EnumerateArray().Single(s => s.GetProperty("id").GetGuid() == s2).GetProperty("progressPercent").GetInt32().Should().Be(50);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
            var events = await db.BldStageProgressEvents.IgnoreQueryFilters()
                .Where(e => e.ProjectId == projectId && e.Source == BuildProgressSource.Measurement).ToListAsync();
            events.Should().HaveCount(2).And.OnlyContain(e => e.SourceRef == measurementId);
            events.Single(e => e.StageId == s2).PreviousPercent.Should().Be(30);
            var snapshot = await db.BldProgressSnapshots.IgnoreQueryFilters()
                .Where(s => s.ProjectId == projectId).OrderByDescending(s => s.Date).FirstAsync();
            snapshot.ActualPercent.Should().Be(Math.Round((2m * 40m + 1m * 50m) / 3m, 2));
        }
        var progress = await Json(await admin.GetAsync($"/api/v1/build/projects/{projectId}/progress"), HttpStatusCode.OK);
        progress.GetProperty("actualPercent").GetDecimal().Should().BeApproximately(43.33m, 0.01m);

        // portal: measurements off by default; on → approved ones, values only with the financial summary
        var company = await CompanyAsync(admin);
        var portal = await PortalAsync(admin, company, projectId);
        (await Code(await Anonymous().GetAsync($"{portal}/measurements"), HttpStatusCode.NotFound)).Should().Be("portal_section_disabled");
        var slug = portal.Split('/').Last();
        await Json(await admin.PutAsJsonAsync($"/api/v1/build/projects/{projectId}/portal",
            new { slug, access = "Public", visibility = new { measurements = true } }), HttpStatusCode.OK);
        var pub = await Json(await Anonymous().GetAsync($"{portal}/measurements"), HttpStatusCode.OK);
        var first = pub.GetProperty("items").EnumerateArray().Single();
        first.TryGetProperty("totalAmount", out _).Should().BeFalse();
        first.GetProperty("items").EnumerateArray().Select(i => i.TryGetProperty("amount", out var _a)).Should().OnlyContain(has => !has);
        await Json(await admin.PutAsJsonAsync($"/api/v1/build/projects/{projectId}/portal",
            new { slug, access = "Public", visibility = new { measurements = true, financialSummary = true } }), HttpStatusCode.OK);
        (await Json(await Anonymous().GetAsync($"{portal}/measurements"), HttpStatusCode.OK))
            .GetProperty("items")[0].GetProperty("totalAmount").GetDecimal().Should().Be(8000m);
        var bootstrap = await Json(await Anonymous().GetAsync(portal), HttpStatusCode.OK);
        bootstrap.GetProperty("contractVersion").GetString().Should().Be("1.2");
        bootstrap.GetProperty("sections").EnumerateArray().Select(s => s.GetString()).Should().Contain("measurements");
    }

    // ── Tenant isolation of every new resource ────────────────────────────────

    [Fact]
    public async Task Another_tenant_gets_404_for_every_wave2_resource()
    {
        var admin = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var projectId = await ProjectAsync(admin, "Obra isolada");
        var stage = await StageAsync(admin, projectId, "Etapa isolada");
        var template = (await Json(await admin.PostAsJsonAsync("/api/v1/build/budget-templates",
            new { name = "Modelo isolado", defaultMarginPercent = 0m }), HttpStatusCode.Created)).GetProperty("id").GetGuid();
        var budget = (await Json(await admin.PostAsJsonAsync("/api/v1/build/budgets", new { name = "Orçamento isolado" }),
            HttpStatusCode.Created)).GetProperty("id").GetGuid();
        var selection = (await Json(await admin.PostAsJsonAsync($"/api/v1/build/projects/{projectId}/selections",
            new { title = "Escolha isolada" }), HttpStatusCode.Created)).GetProperty("id").GetGuid();
        var material = (await Json(await admin.PostAsJsonAsync($"/api/v1/build/projects/{projectId}/materials",
            new { materialName = "Aço", unit = "kg", quantity = 10m }), HttpStatusCode.Created)).GetProperty("id").GetGuid();
        var measurement = (await Json(await admin.PostAsJsonAsync($"/api/v1/build/projects/{projectId}/measurements",
            new { periodStart = Today, periodEnd = Today, items = new[] { new { stageId = stage, measuredPercent = 10 } } }),
            HttpStatusCode.Created)).GetProperty("id").GetGuid();

        var other = await OtherTenantWithBuildAsync();
        var notFound = new[]
        {
            await other.GetAsync($"/api/v1/build/budget-templates/{template}"),
            await other.PutAsJsonAsync($"/api/v1/build/budget-templates/{template}", new { name = "x", defaultMarginPercent = 0m }),
            await other.PostAsJsonAsync($"/api/v1/build/budget-templates/{template}/budgets", new { name = "x" }),
            await other.GetAsync($"/api/v1/build/budgets/{budget}/pdf"),
            await other.GetAsync($"/api/v1/build/budgets/{budget}/versions"),
            await other.PostAsync($"/api/v1/build/budgets/{budget}/duplicate", null),
            await other.PutAsJsonAsync($"/api/v1/build/budgets/{budget}/terms", new { notes = "x" }),
            await other.GetAsync($"/api/v1/build/selections/{selection}"),
            await other.PutAsJsonAsync($"/api/v1/build/selections/{selection}", new { title = "x" }),
            await other.PostAsJsonAsync($"/api/v1/build/selections/{selection}/options", new { name = "x" }),
            await other.GetAsync($"/api/v1/build/projects/{projectId}/selections"),
            await other.GetAsync($"/api/v1/build/materials/{material}"),
            await other.PutAsJsonAsync($"/api/v1/build/materials/{material}", new { materialName = "x", unit = "un", quantity = 1m }),
            await other.PostAsync($"/api/v1/build/materials/{material}/cancel", null),
            await other.GetAsync($"/api/v1/build/projects/{projectId}/materials"),
            await other.GetAsync($"/api/v1/build/measurements/{measurement}"),
            await other.PostAsync($"/api/v1/build/measurements/{measurement}/approve", null),
            await other.GetAsync($"/api/v1/build/projects/{projectId}/measurements"),
        };
        foreach (var r in notFound)
            r.StatusCode.Should().Be(HttpStatusCode.NotFound, $"{r.RequestMessage!.Method} {r.RequestMessage.RequestUri}");

        (await other.GetAsync("/api/v1/build/budget-templates")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Json(await other.GetAsync("/api/v1/build/budget-templates"), HttpStatusCode.OK)).EnumerateArray()
            .Should().NotContain(t => t.GetProperty("id").GetGuid() == template);
        (await Json(await admin.GetAsync($"/api/v1/build/materials/{material}"), HttpStatusCode.OK))
            .GetProperty("status").GetString().Should().Be("Requested");
        (await Json(await admin.GetAsync($"/api/v1/build/measurements/{measurement}"), HttpStatusCode.OK))
            .GetProperty("status").GetString().Should().Be("Draft");
    }
}
