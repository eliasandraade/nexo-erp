using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nexo.Application.Integrations.Contracts;
using Nexo.Domain.Entities;
using Nexo.Domain.Modules.Build;
using Nexo.Infrastructure.Modules.Build;
using Nexo.Infrastructure.Persistence;
using Nexo.IntegrationTests.Common;
using Nexo.IntegrationTests.Helpers;

namespace Nexo.IntegrationTests.Build;

/// <summary>
/// Orken Build Wave 3 "Qualidade e pós-obra", end to end: critical path 8 (quality inspection → NC → stage 100% refused on
/// both progress paths → NC resolved → allowed; walkthrough → delivery refused with a critical NC → delivered → default
/// warranty → portal acceptance → "Meu Imóvel" → assistance opened in the portal with a real photo upload → triage with
/// warranty coverage → resolved → client rating), plus portal and tenant isolation of every new resource.
/// </summary>
[Collection("Integration")]
public class BuildQualityTests
{
    private const string SessionHeader = "X-Build-Portal-Session";
    private readonly TestWebApplicationFactory _factory;

    public BuildQualityTests(TestWebApplicationFactory factory) => _factory = factory;

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string Slug(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..(prefix.Length + 9)];
    private static DateOnly LocalToday => DateOnly.FromDateTime(DateTime.UtcNow.AddHours(-3));
    private static string Today => LocalToday.ToString("yyyy-MM-dd");
    private static string Day(int offset) => LocalToday.AddDays(offset).ToString("yyyy-MM-dd");

    private static async Task<JsonElement> Json(HttpResponseMessage r, HttpStatusCode expected)
    {
        var body = await r.Content.ReadAsStringAsync();
        r.StatusCode.Should().Be(expected, body);
        return string.IsNullOrWhiteSpace(body) ? default : JsonDocument.Parse(body).RootElement.Clone();
    }

    private static async Task<string> Code(HttpResponseMessage r, HttpStatusCode expected)
        => (await Json(r, expected)).GetProperty("code").GetString()!;

    private static async Task<Guid> ProjectAsync(HttpClient c, string name)
    {
        var r = await c.PostAsJsonAsync("/api/v1/build/projects", new
        {
            name, clientName = "Cliente Qualidade", type = "House", startDate = "2026-01-05", expectedEndDate = "2026-12-20",
        });
        return (await Json(r, HttpStatusCode.Created)).GetProperty("id").GetGuid();
    }

    private static async Task<Guid> StageAsync(HttpClient c, Guid projectId, string name)
    {
        var r = await c.PostAsJsonAsync($"/api/v1/build/projects/{projectId}/stages", new
        {
            name, plannedStart = "2026-01-05", plannedEnd = "2026-03-31", weight = 1m,
        });
        return (await Json(r, HttpStatusCode.Created)).GetProperty("id").GetGuid();
    }

    private static async Task<string> CompanyAsync(HttpClient c)
    {
        var slug = Slug("qualid");
        await Json(await c.PutAsJsonAsync("/api/v1/build/portal/company", new
        {
            slug, displayName = "Construtora Qualidade", contactEmail = "contato@" + TestCredentials.TestDomain,
        }), HttpStatusCode.OK);
        return slug;
    }

    private static async Task<string> PortalAsync(HttpClient c, string company, Guid projectId, string? password = null,
        object? visibility = null)
    {
        var slug = Slug("obra");
        await Json(await c.PutAsJsonAsync($"/api/v1/build/projects/{projectId}/portal", new
        {
            slug, access = "Public", title = "Minha obra", visibility,
        }), HttpStatusCode.OK);
        if (password is not null)
        {
            await Json(await c.PutAsJsonAsync($"/api/v1/build/projects/{projectId}/portal/password", new { password }), HttpStatusCode.OK);
            await Json(await c.PutAsJsonAsync($"/api/v1/build/projects/{projectId}/portal", new
            {
                slug, access = "Password", title = "Minha obra", visibility,
            }), HttpStatusCode.OK);
        }
        await Json(await c.PostAsync($"/api/v1/build/projects/{projectId}/portal/publish", null), HttpStatusCode.OK);
        return $"/api/public/build/{company}/{slug}";
    }

    private async Task<string> TokenAsync(string portalUrl, string password)
        => (await Json(await Anonymous().PostAsJsonAsync($"{portalUrl}/session", new { password }), HttpStatusCode.OK))
            .GetProperty("token").GetString()!;

    private static HttpClient WithSession(HttpClient client, string token)
    {
        client.DefaultRequestHeaders.Add(SessionHeader, token);
        return client;
    }

    private HttpClient Anonymous() => AuthClientFactory.CreateAnonymousClient(_factory);

    private static string PortalPassword() => "Qx!" + Guid.NewGuid().ToString("N")[..12];

    private async Task<Guid> TenantOfAsync(Guid projectId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        return await db.BldProjects.IgnoreQueryFilters().Where(p => p.Id == projectId).Select(p => p.TenantId).SingleAsync();
    }

    private async Task<HttpClient> OtherTenantWithBuildAsync()
    {
        var email = $"bld3-{Guid.NewGuid():N}"[..17] + "@" + TestCredentials.TestDomain;
        var reg = await _factory.CreateApiClient().PostAsJsonAsync("/api/auth/register",
            new { name = "Outra Construtora W3", email, password = TestCredentials.AdminPassword });
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

    /// <summary>A host with storage enabled and an in-memory object store (test-only), sharing the suite's database.</summary>
    private WebApplicationFactory<Program> StorageHost(InMemoryStorageProvider storage)
        => _factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("Integrations:Features:StorageEnabled", "true");
            b.ConfigureTestServices(s =>
            {
                s.RemoveAll<IStorageProvider>();
                s.AddSingleton<IStorageProvider>(storage);
            });
        });

    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private static byte[] Png(int size = 64)
    {
        var bytes = new byte[size];
        PngSignature.CopyTo(bytes, 0);
        return bytes;
    }

    private static MultipartFormDataContent Upload(byte[] bytes, string contentType, string fileName)
    {
        var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        content.Add(file, "file", fileName);
        return content;
    }

    private static JsonElement ById(JsonElement array, Guid id)
        => array.EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == id);

    private static int StageProgress(JsonElement stages, Guid id) => ById(stages, id).GetProperty("progressPercent").GetInt32();

    // ── Critical path 8 ───────────────────────────────────────────────────────

    [Fact]
    public async Task Critical_path_8_quality_handover_warranty_and_assistance_through_the_portal()
    {
        var admin = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var projectId = await ProjectAsync(admin, "Obra CP8");
        var estrutura = await StageAsync(admin, projectId, "Estrutura");
        var tenant = await TenantOfAsync(projectId);
        string QualityKey(string name) => $"private/tenants/{tenant}/build/quality/{name}";

        // ── checklist template → inspection (snapshot of the items) ──
        var template = await Json(await admin.PostAsJsonAsync("/api/v1/build/checklist-templates", new
        {
            name = "Estrutura — conferência", category = "Estrutura", stageNameHint = "Estrutura",
            items = new object[]
            {
                new { text = "Prumo e esquadro", required = true, requiresPhoto = true },
                new { text = "Cobrimento da armadura", required = true },
                new { text = "Limpeza da área", required = false },
            },
        }), HttpStatusCode.Created);
        var templateId = template.GetProperty("id").GetGuid();
        template.GetProperty("items").GetArrayLength().Should().Be(3);

        var inspection = await Json(await admin.PostAsJsonAsync($"/api/v1/build/projects/{projectId}/inspections", new
        {
            title = "Vistoria da estrutura", stageId = estrutura, templateId, inspectorName = "Eng. Paulo", notes = "Nota interna da vistoria",
        }), HttpStatusCode.Created);
        var inspectionId = inspection.GetProperty("id").GetGuid();
        inspection.GetProperty("status").GetString().Should().Be("Planned");
        inspection.GetProperty("kind").GetString().Should().Be("Quality");
        var items = inspection.GetProperty("items").EnumerateArray().ToList();
        items.Should().HaveCount(3);
        var prumo = items[0].GetProperty("id").GetGuid();
        var cobrimento = items[1].GetProperty("id").GetGuid();

        // editing the template never changes the inspection already created
        await Json(await admin.PutAsJsonAsync($"/api/v1/build/checklist-templates/{templateId}", new
        {
            name = "Estrutura — v2", items = new object[] { new { text = "Outro item" } },
        }), HttpStatusCode.OK);
        (await Json(await admin.GetAsync($"/api/v1/build/inspections/{inspectionId}"), HttpStatusCode.OK))
            .GetProperty("items")[0].GetProperty("text").GetString().Should().Be("Prumo e esquadro");

        // outcome rules: required pending → 422; photo required → 422; foreign keys refused
        (await Code(await admin.PostAsync($"/api/v1/build/inspections/{inspectionId}/complete", null), HttpStatusCode.UnprocessableEntity))
            .Should().Be("inspection_required_items_pending");
        await Json(await admin.PutAsJsonAsync($"/api/v1/build/inspections/{inspectionId}/items/{prumo}", new { result = "Ok" }), HttpStatusCode.OK);
        await Json(await admin.PutAsJsonAsync($"/api/v1/build/inspections/{inspectionId}/items/{cobrimento}",
            new { result = "NotOk", severity = "Critical", comment = "Cobrimento insuficiente no pilar P3" }), HttpStatusCode.OK);
        (await Code(await admin.PostAsync($"/api/v1/build/inspections/{inspectionId}/complete", null), HttpStatusCode.UnprocessableEntity))
            .Should().Be("inspection_photo_required");
        (await Code(await admin.PutAsJsonAsync($"/api/v1/build/inspections/{inspectionId}/items/{prumo}",
            new { result = "Ok", photoKeys = new[] { $"private/tenants/{Guid.NewGuid()}/build/quality/x.jpg" } }), HttpStatusCode.UnprocessableEntity))
            .Should().Be("invalid_file_key");
        (await admin.PutAsJsonAsync($"/api/v1/build/inspections/{inspectionId}/items/{prumo}", new { result = "Talvez" }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await Json(await admin.PutAsJsonAsync($"/api/v1/build/inspections/{inspectionId}/items/{prumo}",
            new { result = "Ok", photoKeys = new[] { QualityKey("prumo.jpg") } }), HttpStatusCode.OK);

        var completed = await Json(await admin.PostAsJsonAsync($"/api/v1/build/inspections/{inspectionId}/complete", new { }), HttpStatusCode.OK);
        completed.GetProperty("status").GetString().Should().Be("Failed", "a required item is NotOk");
        completed.GetProperty("notOkItems").GetInt32().Should().Be(1);
        var ncId = ById(completed.GetProperty("items"), cobrimento).GetProperty("nonConformityId").GetGuid();
        (await admin.PostAsJsonAsync($"/api/v1/build/inspections/{inspectionId}/complete", new { })).StatusCode.Should().Be(HttpStatusCode.Conflict);

        var ncs = await Json(await admin.GetAsync($"/api/v1/build/projects/{projectId}/non-conformities"), HttpStatusCode.OK);
        var nc = ncs.EnumerateArray().Single();
        nc.GetProperty("id").GetGuid().Should().Be(ncId);
        nc.GetProperty("severity").GetString().Should().Be("Critical");
        nc.GetProperty("status").GetString().Should().Be("Open");
        nc.GetProperty("stageId").GetGuid().Should().Be(estrutura);
        nc.GetProperty("inspectionId").GetGuid().Should().Be(inspectionId);
        nc.GetProperty("description").GetString().Should().Be("Cobrimento insuficiente no pilar P3");

        // ── stage 100% refused while the critical NC is open: manual, status override and measurement paths ──
        (await Code(await admin.PutAsJsonAsync($"/api/v1/build/stages/{estrutura}/progress", new { progressPercent = 100 }),
            HttpStatusCode.UnprocessableEntity)).Should().Be("stage_has_critical_nonconformity");
        await Json(await admin.PutAsJsonAsync($"/api/v1/build/stages/{estrutura}/progress", new { progressPercent = 90 }), HttpStatusCode.OK);
        (await Code(await admin.PutAsJsonAsync($"/api/v1/build/stages/{estrutura}/progress", new { progressPercent = 90, status = "Completed" }),
            HttpStatusCode.UnprocessableEntity)).Should().Be("stage_has_critical_nonconformity");
        var measurement = await Json(await admin.PostAsJsonAsync($"/api/v1/build/projects/{projectId}/measurements", new
        {
            periodStart = Day(-10), periodEnd = Today, items = new[] { new { stageId = estrutura, measuredPercent = 100 } },
        }), HttpStatusCode.Created);
        var measurementId = measurement.GetProperty("id").GetGuid();
        (await Code(await admin.PostAsync($"/api/v1/build/measurements/{measurementId}/approve", null), HttpStatusCode.UnprocessableEntity))
            .Should().Be("stage_has_critical_nonconformity");
        StageProgress(await Json(await admin.GetAsync($"/api/v1/build/projects/{projectId}/stages"), HttpStatusCode.OK), estrutura).Should().Be(90);

        var summary = await Json(await admin.GetAsync($"/api/v1/build/projects/{projectId}/quality/summary"), HttpStatusCode.OK);
        summary.GetProperty("criticalOpen").GetInt32().Should().Be(1);
        summary.GetProperty("completedInspections").GetInt32().Should().Be(1);
        summary.GetProperty("passRate").GetDecimal().Should().Be(0m);
        summary.GetProperty("inspectionsByStatus").GetProperty("Failed").GetInt32().Should().Be(1);
        summary.GetProperty("openNonConformitiesBySeverity").GetProperty("Critical").GetInt32().Should().Be(1);

        // ── NC transitions ──
        (await Code(await admin.PostAsync($"/api/v1/build/non-conformities/{ncId}/verify", null), HttpStatusCode.UnprocessableEntity))
            .Should().Be("nc_not_resolved");
        (await Code(await admin.PostAsJsonAsync($"/api/v1/build/non-conformities/{ncId}/resolve", new { }), HttpStatusCode.UnprocessableEntity))
            .Should().Be("nc_corrective_action_required");
        (await Json(await admin.PostAsync($"/api/v1/build/non-conformities/{ncId}/start", null), HttpStatusCode.OK))
            .GetProperty("status").GetString().Should().Be("InTreatment");
        (await Code(await admin.PutAsJsonAsync($"/api/v1/build/stages/{estrutura}/progress", new { progressPercent = 100 }),
            HttpStatusCode.UnprocessableEntity)).Should().Be("stage_has_critical_nonconformity", "in treatment still blocks");
        (await Json(await admin.PostAsJsonAsync($"/api/v1/build/non-conformities/{ncId}/resolve",
            new { correctiveAction = "Recomposição do cobrimento com graute", rootCause = "Espaçadores ausentes" }), HttpStatusCode.OK))
            .GetProperty("status").GetString().Should().Be("Resolved");
        (await admin.PostAsJsonAsync($"/api/v1/build/non-conformities/{ncId}/resolve", new { correctiveAction = "x" }))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);

        // resolved → the measurement path and the manual path accept 100%
        (await Json(await admin.PostAsync($"/api/v1/build/measurements/{measurementId}/approve", null), HttpStatusCode.OK))
            .GetProperty("status").GetString().Should().Be("Approved");
        StageProgress(await Json(await admin.GetAsync($"/api/v1/build/projects/{projectId}/stages"), HttpStatusCode.OK), estrutura).Should().Be(100);
        await Json(await admin.PutAsJsonAsync($"/api/v1/build/stages/{estrutura}/progress", new { progressPercent = 95 }), HttpStatusCode.OK);
        await Json(await admin.PutAsJsonAsync($"/api/v1/build/stages/{estrutura}/progress", new { progressPercent = 100 }), HttpStatusCode.OK);
        var verified = await Json(await admin.PostAsync($"/api/v1/build/non-conformities/{ncId}/verify", null), HttpStatusCode.OK);
        verified.GetProperty("status").GetString().Should().Be("Verified");
        verified.GetProperty("verifiedBySameUser").GetBoolean().Should().BeTrue("allowed, but recorded");

        // ── handover: schedule, walkthrough, delivery refused with a critical NC ──
        var handover = await Json(await admin.PutAsJsonAsync($"/api/v1/build/projects/{projectId}/handover", new
        {
            scheduledFor = Day(7), notes = "Nota interna da entrega",
            documents = new[] { new { name = "Manual do proprietário", fileKey = $"private/tenants/{tenant}/build/handover/manual.pdf" } },
        }), HttpStatusCode.OK);
        handover.GetProperty("status").GetString().Should().Be("Scheduled");
        var walkthrough = await Json(await admin.PostAsJsonAsync($"/api/v1/build/projects/{projectId}/handover/walkthrough", new
        {
            items = new object[] { new { text = "Portas e janelas", required = true }, new { text = "Pintura da sala", required = false } },
        }), HttpStatusCode.Created);
        walkthrough.GetProperty("kind").GetString().Should().Be("ClientWalkthrough");
        var walkthroughId = walkthrough.GetProperty("id").GetGuid();
        (await admin.PostAsJsonAsync($"/api/v1/build/projects/{projectId}/handover/walkthrough", new { }))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);

        var fissura = await Json(await admin.PostAsJsonAsync($"/api/v1/build/projects/{projectId}/non-conformities",
            new { title = "Fissura na laje", severity = "Critical", dueDate = Day(-1) }), HttpStatusCode.Created);
        fissura.GetProperty("number").GetInt32().Should().Be(2);
        fissura.GetProperty("isOverdue").GetBoolean().Should().BeTrue();
        (await Code(await admin.PostAsync($"/api/v1/build/projects/{projectId}/handover/deliver", null), HttpStatusCode.UnprocessableEntity))
            .Should().Be("handover_critical_nonconformity");
        await Json(await admin.PostAsJsonAsync($"/api/v1/build/non-conformities/{fissura.GetProperty("id").GetGuid()}/cancel",
            new { reason = "Aberta em duplicidade" }), HttpStatusCode.OK);

        // ── portal with password: accept refused before the walkthrough ──
        var company = await CompanyAsync(admin);
        var password = PortalPassword();
        var portal = await PortalAsync(admin, company, projectId, password);
        var token = await TokenAsync(portal, password);
        var client = WithSession(Anonymous(), token);
        (await Code(await Anonymous().PostAsJsonAsync($"{portal}/handover/accept", new { signerName = "Maria" }), HttpStatusCode.Unauthorized))
            .Should().Be("portal_password_required");
        (await Code(await client.PostAsJsonAsync($"{portal}/handover/accept", new { signerName = "Maria" }), HttpStatusCode.UnprocessableEntity))
            .Should().Be("handover_not_ready");
        var beforeDelivery = await Json(await client.GetAsync(portal), HttpStatusCode.OK);
        beforeDelivery.GetProperty("phase").GetString().Should().Be("construction");
        beforeDelivery.TryGetProperty("property", out _).Should().BeFalse();
        (await Code(await client.PostAsJsonAsync($"{portal}/assistance",
            new { category = "Outros", title = "Cedo demais", description = "Antes da entrega" }), HttpStatusCode.UnprocessableEntity))
            .Should().Be("property_not_delivered");

        // walkthrough completed with a remark → NC (low) and handover InspectionDone
        var wItems = walkthrough.GetProperty("items").EnumerateArray().ToList();
        await Json(await admin.PutAsJsonAsync($"/api/v1/build/inspections/{walkthroughId}/items/{wItems[0].GetProperty("id").GetGuid()}",
            new { result = "Ok" }), HttpStatusCode.OK);
        await Json(await admin.PutAsJsonAsync($"/api/v1/build/inspections/{walkthroughId}/items/{wItems[1].GetProperty("id").GetGuid()}",
            new { result = "NotOk", severity = "Low", comment = "Retoque na parede da sala (comentário interno)" }), HttpStatusCode.OK);
        (await Json(await admin.PostAsync($"/api/v1/build/inspections/{walkthroughId}/complete", null), HttpStatusCode.OK))
            .GetProperty("status").GetString().Should().Be("PassedWithRemarks");
        (await Json(await admin.GetAsync($"/api/v1/build/projects/{projectId}/non-conformities?severity=Low"), HttpStatusCode.OK))
            .GetArrayLength().Should().Be(1);
        (await Json(await admin.GetAsync($"/api/v1/build/projects/{projectId}/handover"), HttpStatusCode.OK))
            .GetProperty("status").GetString().Should().Be("InspectionDone");

        // ── delivery: project completed, default warranty terms ──
        var delivered = await Json(await admin.PostAsJsonAsync($"/api/v1/build/projects/{projectId}/handover/deliver",
            new { keysDelivered = true }), HttpStatusCode.OK);
        delivered.GetProperty("status").GetString().Should().Be("Delivered");
        delivered.GetProperty("projectStatus").GetString().Should().Be("Completed");
        delivered.GetProperty("keysDelivered").GetBoolean().Should().BeTrue();
        (await admin.PostAsync($"/api/v1/build/projects/{projectId}/handover/deliver", null)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        var warranty = await Json(await admin.GetAsync($"/api/v1/build/projects/{projectId}/warranty"), HttpStatusCode.OK);
        warranty.GetArrayLength().Should().Be(7);
        var estruturaTerm = warranty.EnumerateArray().Single(t => t.GetProperty("system").GetString() == "Estrutura");
        estruturaTerm.GetProperty("months").GetInt32().Should().Be(60);
        estruturaTerm.GetProperty("startsAt").GetString().Should().Be(Today);
        estruturaTerm.GetProperty("expiresAt").GetString().Should().Be((LocalToday.AddMonths(60).Day == LocalToday.Day ? LocalToday.AddMonths(60) : LocalToday.AddMonths(60).AddDays(1)).ToString("yyyy-MM-dd"));
        estruturaTerm.GetProperty("isActive").GetBoolean().Should().BeTrue();

        // ── portal: "Meu Imóvel", acceptance ──
        var rawBootstrap = await (await client.GetAsync(portal)).Content.ReadAsStringAsync();
        var bootstrap = JsonDocument.Parse(rawBootstrap).RootElement;
        bootstrap.GetProperty("contractVersion").GetString().Should().Be("1.5");
        bootstrap.GetProperty("phase").GetString().Should().Be("delivered");
        bootstrap.GetProperty("sections").EnumerateArray().Select(s => s.GetString()).Should().Contain("postConstruction");
        var property = bootstrap.GetProperty("property");
        property.GetProperty("handoverDate").GetString().Should().Be(Today);
        property.GetProperty("warrantyTerms").GetArrayLength().Should().Be(7);
        property.GetProperty("openAssistanceCount").GetInt32().Should().Be(0);

        var rawHandover = await (await client.GetAsync($"{portal}/handover")).Content.ReadAsStringAsync();
        var publicHandover = JsonDocument.Parse(rawHandover).RootElement;
        publicHandover.GetProperty("status").GetString().Should().Be("Delivered");
        publicHandover.GetProperty("canAccept").GetBoolean().Should().BeTrue();
        publicHandover.GetProperty("documents")[0].GetProperty("name").GetString().Should().Be("Manual do proprietário");
        publicHandover.GetProperty("walkthrough").GetProperty("pendingItems").GetInt32().Should().Be(1);
        rawHandover.Should().NotContain("Nota interna").And.NotContain("comentário interno").And.NotContain("build/handover")
            .And.NotContain(projectId.ToString());

        (await Code(await client.PostAsJsonAsync($"{portal}/handover/accept", new { comment = "sem nome" }), HttpStatusCode.BadRequest))
            .Should().Be("signer_name_required");
        var accepted = await Json(await client.PostAsJsonAsync($"{portal}/handover/accept",
            new { signerName = "Maria Cliente", comment = "Recebido em ordem." }), HttpStatusCode.OK);
        accepted.GetProperty("acceptedByName").GetString().Should().Be("Maria Cliente");
        accepted.GetProperty("canAccept").GetBoolean().Should().BeFalse();
        (await Code(await client.PostAsJsonAsync($"{portal}/handover/accept", new { signerName = "Maria Cliente" }), HttpStatusCode.Conflict))
            .Should().Be("handover_already_accepted");
        (await Json(await admin.GetAsync($"/api/v1/build/projects/{projectId}/handover"), HttpStatusCode.OK))
            .GetProperty("acceptanceChannel").GetString().Should().Be("Portal");

        var publicWarranty = await Json(await client.GetAsync($"{portal}/warranty"), HttpStatusCode.OK);
        publicWarranty.GetProperty("items").GetArrayLength().Should().Be(7);
        publicWarranty.GetProperty("notice").GetString().Should().NotBeNullOrWhiteSpace();

        // ── assistance opened by the client ──
        var assistanceList = await Json(await client.GetAsync($"{portal}/assistance"), HttpStatusCode.OK);
        assistanceList.GetProperty("canOpen").GetBoolean().Should().BeTrue();
        assistanceList.GetProperty("categories").EnumerateArray().Select(c => c.GetString()).Should().Contain(["Impermeabilização", "Outros"]);
        (await Code(await client.PostAsJsonAsync($"{portal}/assistance",
            new { category = "Piscina", title = "x", description = "y" }), HttpStatusCode.BadRequest)).Should().Be("invalid_category");
        (await Code(await client.PostAsJsonAsync($"{portal}/assistance",
            new { category = "Outros", description = "sem título" }), HttpStatusCode.BadRequest)).Should().Be("invalid_assistance_request");
        var opened = await Json(await client.PostAsJsonAsync($"{portal}/assistance", new
        {
            category = "impermeabilização", title = "Infiltração no banheiro", description = "Mancha de umidade no teto do banheiro.",
            location = "Banheiro social", contactPhone = "11988887777", openedByName = "Maria Cliente",
        }), HttpStatusCode.Created);
        var publicId = opened.GetProperty("id").GetGuid();
        opened.GetProperty("status").GetString().Should().Be("Open");
        opened.GetProperty("category").GetString().Should().Be("Impermeabilização", "stored with the warranty system's spelling");
        opened.GetProperty("canAddPhotos").GetBoolean().Should().BeTrue();
        opened.TryGetProperty("contactPhone", out _).Should().BeFalse();

        // photo upload: storage off → 503; on (in-memory store) → stored privately, magic bytes and size enforced
        (await Code(await client.PostAsync($"{portal}/assistance/{publicId}/photos", Upload(Png(), "image/png", "foto.png")),
            HttpStatusCode.ServiceUnavailable)).Should().Be("storage_unavailable");
        var storage = new InMemoryStorageProvider();
        await using (var host = StorageHost(storage))
        {
            var storageClient = WithSession(host.CreateClient(), token);
            (await Code(await storageClient.PostAsync($"{portal}/assistance/{publicId}/photos",
                Upload("isto não é uma imagem"u8.ToArray(), "image/png", "falsa.png")), HttpStatusCode.BadRequest))
                .Should().Be("invalid_file_content");
            (await Code(await storageClient.PostAsync($"{portal}/assistance/{publicId}/photos",
                Upload(Png(), "application/pdf", "foto.pdf")), HttpStatusCode.BadRequest)).Should().Be("invalid_file_type");
            (await Code(await storageClient.PostAsync($"{portal}/assistance/{publicId}/photos",
                Upload(Png(8 * 1024 * 1024 + 1), "image/png", "grande.png")), (HttpStatusCode)413)).Should().Be("file_too_large");
            (await Code(await WithSession(host.CreateClient(), "token-invalido").PostAsync($"{portal}/assistance/{publicId}/photos",
                Upload(Png(), "image/png", "foto.png")), HttpStatusCode.Unauthorized)).Should().Be("portal_session_expired");
            storage.Objects.Should().BeEmpty("nothing refused reaches the store");

            var withPhoto = await Json(await storageClient.PostAsync($"{portal}/assistance/{publicId}/photos",
                Upload(Png(), "image/png", "../../etc/passwd.png")), HttpStatusCode.OK);
            var photoUrl = withPhoto.GetProperty("photoUrls").EnumerateArray().Single().GetString()!;
            photoUrl.Should().StartWith(InMemoryStorageProvider.Host + $"private/tenants/{tenant}/build/assistance/").And.Contain("signature=");
            photoUrl.Should().NotContain("passwd");
            storage.Objects.Keys.Should().ContainSingle().Which.Should().EndWith(".png");
        }

        // ── internal triage computes the coverage; notes stay internal ──
        var internalList = await Json(await admin.GetAsync($"/api/v1/build/projects/{projectId}/assistance"), HttpStatusCode.OK);
        var request = internalList.EnumerateArray().Single();
        request.GetProperty("openedVia").GetString().Should().Be("Portal");
        request.GetProperty("contactPhone").GetString().Should().Be("11988887777");
        request.GetProperty("photoKeys").GetArrayLength().Should().Be(1);
        var requestId = request.GetProperty("id").GetGuid();
        var triaged = await Json(await admin.PostAsJsonAsync($"/api/v1/build/assistance/{requestId}/triage",
            new { notes = "Nota interna da triagem: verificar rejunte" }), HttpStatusCode.OK);
        triaged.GetProperty("warrantyCovered").GetBoolean().Should().BeTrue();
        triaged.GetProperty("warrantySystem").GetString().Should().Be("Impermeabilização");
        triaged.GetProperty("status").GetString().Should().Be("Triaged");

        var rawAssistance = await (await client.GetAsync($"{portal}/assistance")).Content.ReadAsStringAsync();
        rawAssistance.Should().NotContain("Nota interna da triagem").And.NotContain("11988887777").And.NotContain(requestId.ToString());
        var publicRequest = JsonDocument.Parse(rawAssistance).RootElement.GetProperty("items")[0];
        publicRequest.GetProperty("warrantyCovered").GetBoolean().Should().BeTrue();
        publicRequest.GetProperty("timeline").GetArrayLength().Should().Be(2);
        (await Code(await client.PostAsJsonAsync($"{portal}/assistance/{publicId}/feedback", new { rating = 5 }), HttpStatusCode.UnprocessableEntity))
            .Should().Be("assistance_not_resolved");

        await Json(await admin.PostAsJsonAsync($"/api/v1/build/assistance/{requestId}/schedule",
            new { scheduledFor = DateTimeOffset.UtcNow.AddDays(2) }), HttpStatusCode.OK);
        await Json(await admin.PostAsync($"/api/v1/build/assistance/{requestId}/start", null), HttpStatusCode.OK);
        await Json(await admin.PostAsJsonAsync($"/api/v1/build/assistance/{requestId}/resolve",
            new { resolutionNotes = "Refeita a impermeabilização do box." }), HttpStatusCode.OK);

        // ── client feedback, once ──
        (await Code(await client.PostAsJsonAsync($"{portal}/assistance/{publicId}/feedback", new { rating = 6 }), HttpStatusCode.BadRequest))
            .Should().Be("invalid_rating");
        var rated = await Json(await client.PostAsJsonAsync($"{portal}/assistance/{publicId}/feedback",
            new { rating = 5, comment = "Atendimento rápido." }), HttpStatusCode.OK);
        rated.GetProperty("rating").GetInt32().Should().Be(5);
        rated.GetProperty("resolutionNotes").GetString().Should().Be("Refeita a impermeabilização do box.");
        rated.GetProperty("canGiveFeedback").GetBoolean().Should().BeFalse();
        (await Code(await client.PostAsJsonAsync($"{portal}/assistance/{publicId}/feedback", new { rating = 4 }), HttpStatusCode.Conflict))
            .Should().Be("feedback_already_given");
        (await Json(await admin.PostAsync($"/api/v1/build/assistance/{requestId}/close", null), HttpStatusCode.OK))
            .GetProperty("clientRating").GetInt32().Should().Be(5);

        // ── coverage: no term for "Outros" → undecided; an expired term → not covered ──
        var outros = await Json(await admin.PostAsJsonAsync($"/api/v1/build/projects/{projectId}/assistance",
            new { category = "Outros", title = "Campainha", description = "Não toca." }), HttpStatusCode.Created);
        var outrosTriaged = await Json(await admin.PostAsync($"/api/v1/build/assistance/{outros.GetProperty("id").GetGuid()}/triage", null),
            HttpStatusCode.OK);
        outrosTriaged.GetProperty("warrantyCovered").ValueKind.Should().Be(JsonValueKind.Null);
        var pintura = warranty.EnumerateArray().Single(t => t.GetProperty("system").GetString() == "Pintura").GetProperty("id").GetGuid();
        await Json(await admin.PutAsJsonAsync($"/api/v1/build/warranty-terms/{pintura}",
            new { system = "Pintura", months = 24, startsAt = "2020-01-01" }), HttpStatusCode.OK);
        var paint = await Json(await admin.PostAsJsonAsync($"/api/v1/build/projects/{projectId}/assistance",
            new { category = "Pintura", title = "Descascando", description = "Parede da sala." }), HttpStatusCode.Created);
        (await Json(await admin.PostAsync($"/api/v1/build/assistance/{paint.GetProperty("id").GetGuid()}/triage", null), HttpStatusCode.OK))
            .GetProperty("warrantyCovered").GetBoolean().Should().BeFalse();
        (await Json(await admin.PostAsJsonAsync($"/api/v1/build/assistance/{paint.GetProperty("id").GetGuid()}/reject",
            new { reason = "Fora da garantia." }), HttpStatusCode.OK)).GetProperty("status").GetString().Should().Be("Rejected");
    }

    // ── Portal rules and isolation ────────────────────────────────────────────

    [Fact]
    public async Task Public_portal_cannot_write_sessions_cannot_cross_projects_and_the_flag_hides_the_section()
    {
        var admin = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var company = await CompanyAsync(admin);

        async Task<Guid> DeliveredProjectAsync(string name)
        {
            var id = await ProjectAsync(admin, name);
            await Json(await admin.PutAsJsonAsync($"/api/v1/build/projects/{id}/handover", new { scheduledFor = Today }), HttpStatusCode.OK);
            await Json(await admin.PostAsync($"/api/v1/build/projects/{id}/handover/deliver", null), HttpStatusCode.OK);
            return id;
        }

        // v1.5: a public portal never serves "Meu Imóvel" (personal/residential data) even with the flag on; writes stay 403
        var publicProject = await DeliveredProjectAsync("Obra pública W3");
        var publicPortal = await PortalAsync(admin, company, publicProject, visibility: new { postConstruction = true });
        foreach (var section in new[] { "warranty", "assistance", "handover" })
            (await Code(await Anonymous().GetAsync($"{publicPortal}/{section}"), HttpStatusCode.NotFound)).Should().Be("portal_section_disabled");
        (await Code(await Anonymous().PostAsJsonAsync($"{publicPortal}/assistance",
            new { category = "Outros", title = "Teste", description = "Teste" }), HttpStatusCode.Forbidden))
            .Should().Be("portal_decision_requires_password");
        (await Code(await Anonymous().PostAsJsonAsync($"{publicPortal}/handover/accept", new { signerName = "Maria" }), HttpStatusCode.Forbidden))
            .Should().Be("portal_decision_requires_password");
        var publicBootstrap = await Json(await Anonymous().GetAsync(publicPortal), HttpStatusCode.OK);
        publicBootstrap.GetProperty("phase").GetString().Should().Be("delivered");
        publicBootstrap.TryGetProperty("property", out _).Should().BeFalse();
        publicBootstrap.GetProperty("sections").EnumerateArray().Select(s => s.GetString()).Should().NotContain("postConstruction");

        // a session of project X never touches project Y's requests
        var password = PortalPassword();
        var x = await DeliveredProjectAsync("Obra X W3");
        var y = await DeliveredProjectAsync("Obra Y W3");
        var portalX = await PortalAsync(admin, company, x, password);
        var portalY = await PortalAsync(admin, company, y, password);
        var requestY = await Json(await admin.PostAsJsonAsync($"/api/v1/build/projects/{y}/assistance",
            new { category = "Estrutura", title = "Trinca", description = "Trinca na viga." }), HttpStatusCode.Created);
        var requestYId = requestY.GetProperty("id").GetGuid();
        var publicY = requestY.GetProperty("publicId").GetGuid();
        await Json(await admin.PostAsync($"/api/v1/build/assistance/{requestYId}/triage", null), HttpStatusCode.OK);
        await Json(await admin.PostAsJsonAsync($"/api/v1/build/assistance/{requestYId}/resolve", new { resolutionNotes = "Ok" }), HttpStatusCode.OK);
        var sessionX = WithSession(Anonymous(), await TokenAsync(portalX, password));
        (await Code(await sessionX.PostAsJsonAsync($"{portalX}/assistance/{publicY}/feedback", new { rating = 1 }), HttpStatusCode.NotFound))
            .Should().Be("portal_item_not_found");
        (await Code(await sessionX.PostAsync($"{portalX}/assistance/{publicY}/photos", Upload(Png(), "image/png", "a.png")), HttpStatusCode.NotFound))
            .Should().Be("portal_item_not_found");
        (await Code(await sessionX.PostAsJsonAsync($"{portalY}/assistance/{publicY}/feedback", new { rating = 1 }), HttpStatusCode.Unauthorized))
            .Should().Be("portal_session_expired");
        (await Json(await sessionX.GetAsync($"{portalX}/assistance"), HttpStatusCode.OK)).GetProperty("items").GetArrayLength().Should().Be(0);
        (await Json(await admin.GetAsync($"/api/v1/build/assistance/{requestYId}"), HttpStatusCode.OK))
            .GetProperty("clientRating").ValueKind.Should().Be(JsonValueKind.Null);

        // the upload is authorized BEFORE its body is read: a malformed multipart body never gets parsed for a caller
        // without a session (401) nor for a request of another project (404)
        HttpContent Garbage()
        {
            var body = new ByteArrayContent(new byte[64 * 1024]);
            body.Headers.ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.Parse("multipart/form-data; boundary=nope");
            return body;
        }
        (await Code(await Anonymous().PostAsync($"{portalX}/assistance/{publicY}/photos", Garbage()), HttpStatusCode.Unauthorized))
            .Should().Be("portal_password_required");
        (await Code(await sessionX.PostAsync($"{portalX}/assistance/{publicY}/photos", Garbage()), HttpStatusCode.NotFound))
            .Should().Be("portal_item_not_found");

        // flag off → section gone (password portal); without a session the answer is 401 first — the disabled section
        // (portal configuration) is never revealed to someone without the password
        var slugX = portalX.Split('/').Last();
        await Json(await admin.PutAsJsonAsync($"/api/v1/build/projects/{x}/portal",
            new { slug = slugX, access = "Password", visibility = new { postConstruction = false } }), HttpStatusCode.OK);
        (await Code(await sessionX.GetAsync($"{portalX}/warranty"), HttpStatusCode.NotFound)).Should().Be("portal_section_disabled");
        (await Code(await sessionX.GetAsync($"{portalX}/handover"), HttpStatusCode.NotFound)).Should().Be("portal_section_disabled");
        (await Code(await Anonymous().GetAsync($"{portalX}/warranty"), HttpStatusCode.Unauthorized)).Should().Be("portal_password_required");
        (await Code(await Anonymous().PostAsJsonAsync($"{portalX}/assistance",
            new { category = "Outros", title = "Teste", description = "Teste" }), HttpStatusCode.Unauthorized)).Should().Be("portal_password_required");
        (await Code(await Anonymous().PostAsJsonAsync($"{portalX}/handover/accept", new { signerName = "Maria" }), HttpStatusCode.Unauthorized))
            .Should().Be("portal_password_required");
        var hidden = await Json(await sessionX.GetAsync(portalX), HttpStatusCode.OK);
        hidden.GetProperty("phase").GetString().Should().Be("delivered");
        hidden.TryGetProperty("property", out _).Should().BeFalse();
        hidden.GetProperty("sections").EnumerateArray().Select(s => s.GetString()).Should().NotContain("postConstruction");
    }

    [Fact]
    public async Task Parallel_portal_writes_never_exceed_the_limits_nor_lose_a_photo()
    {
        var admin = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var projectId = await ProjectAsync(admin, "Obra corrida W3");
        await Json(await admin.PutAsJsonAsync($"/api/v1/build/projects/{projectId}/handover", new { scheduledFor = Today }), HttpStatusCode.OK);
        await Json(await admin.PostAsync($"/api/v1/build/projects/{projectId}/handover/deliver", null), HttpStatusCode.OK);
        var company = await CompanyAsync(admin);
        var password = PortalPassword();
        var portal = await PortalAsync(admin, company, projectId, password);
        var token = await TokenAsync(portal, password);
        var client = WithSession(Anonymous(), token);

        // 18 open requests, then 6 at once: the limit (20) is counted and enforced atomically → exactly 2 more
        Guid first = default;
        for (var i = 0; i < 18; i++)
        {
            var created = await Json(await client.PostAsJsonAsync($"{portal}/assistance",
                new { category = "Outros", title = $"Chamado {i}", description = "Descrição" }), HttpStatusCode.Created);
            if (i == 0) first = created.GetProperty("id").GetGuid();
        }
        var burst = await Task.WhenAll(Enumerable.Range(0, 6).Select(i => WithSession(Anonymous(), token)
            .PostAsJsonAsync($"{portal}/assistance", new { category = "Outros", title = $"Paralelo {i}", description = "Descrição" })));
        burst.Count(r => r.StatusCode == HttpStatusCode.Created).Should().Be(2);
        burst.Where(r => r.StatusCode != HttpStatusCode.Created).Should().OnlyContain(r => r.StatusCode == HttpStatusCode.UnprocessableEntity);
        (await Json(await admin.GetAsync($"/api/v1/build/projects/{projectId}/assistance"), HttpStatusCode.OK))
            .GetArrayLength().Should().Be(BuildPublicPortalService.MaxOpenPortalRequests);

        // 9 photos at once on one request: exactly 6 attached, none lost, the refused ones never stay in the store
        var storage = new InMemoryStorageProvider();
        await using var host = StorageHost(storage);
        var uploads = await Task.WhenAll(Enumerable.Range(0, 9).Select(_ => WithSession(host.CreateClient(), token)
            .PostAsync($"{portal}/assistance/{first}/photos", Upload(Png(), "image/png", "foto.png"))));
        uploads.Count(r => r.StatusCode == HttpStatusCode.OK).Should().Be(BuildAssistanceRequest.MaxPhotos);
        foreach (var refused in uploads.Where(r => r.StatusCode != HttpStatusCode.OK))
            (await Code(refused, HttpStatusCode.UnprocessableEntity)).Should().Be("assistance_photo_limit");
        var stored = (await Json(await admin.GetAsync($"/api/v1/build/projects/{projectId}/assistance"), HttpStatusCode.OK))
            .EnumerateArray().Single(a => a.GetProperty("number").GetInt32() == 1);
        stored.GetProperty("photoKeys").GetArrayLength().Should().Be(BuildAssistanceRequest.MaxPhotos);
        storage.Objects.Should().HaveCount(BuildAssistanceRequest.MaxPhotos);
    }

    [Fact]
    public async Task Another_tenant_gets_404_for_every_wave3_resource()
    {
        var admin = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var projectId = await ProjectAsync(admin, "Obra isolamento W3");
        var template = (await Json(await admin.PostAsJsonAsync("/api/v1/build/checklist-templates",
            new { name = "Checklist A", items = new[] { new { text = "Item" } } }), HttpStatusCode.Created)).GetProperty("id").GetGuid();
        var inspection = await Json(await admin.PostAsJsonAsync($"/api/v1/build/projects/{projectId}/inspections",
            new { title = "Vistoria A", templateId = template }), HttpStatusCode.Created);
        var inspectionId = inspection.GetProperty("id").GetGuid();
        var itemId = inspection.GetProperty("items")[0].GetProperty("id").GetGuid();
        var nc = (await Json(await admin.PostAsJsonAsync($"/api/v1/build/projects/{projectId}/non-conformities",
            new { title = "NC A", severity = "High" }), HttpStatusCode.Created)).GetProperty("id").GetGuid();
        await Json(await admin.PutAsJsonAsync($"/api/v1/build/projects/{projectId}/handover", new { scheduledFor = Today }), HttpStatusCode.OK);
        await Json(await admin.PostAsync($"/api/v1/build/projects/{projectId}/handover/deliver", null), HttpStatusCode.OK);
        var term = (await Json(await admin.GetAsync($"/api/v1/build/projects/{projectId}/warranty"), HttpStatusCode.OK))[0].GetProperty("id").GetGuid();
        var assistance = (await Json(await admin.PostAsJsonAsync($"/api/v1/build/projects/{projectId}/assistance",
            new { category = "Outros", title = "Chamado A", description = "Desc" }), HttpStatusCode.Created)).GetProperty("id").GetGuid();

        var other = await OtherTenantWithBuildAsync();
        var notFound = new List<HttpResponseMessage>
        {
            await other.GetAsync($"/api/v1/build/checklist-templates/{template}"),
            await other.PutAsJsonAsync($"/api/v1/build/checklist-templates/{template}", new { name = "x" }),
            await other.DeleteAsync($"/api/v1/build/checklist-templates/{template}"),
            await other.GetAsync($"/api/v1/build/projects/{projectId}/inspections"),
            await other.GetAsync($"/api/v1/build/inspections/{inspectionId}"),
            await other.PutAsJsonAsync($"/api/v1/build/inspections/{inspectionId}", new { title = "x" }),
            await other.PutAsJsonAsync($"/api/v1/build/inspections/{inspectionId}/items/{itemId}", new { result = "Ok" }),
            await other.PostAsync($"/api/v1/build/inspections/{inspectionId}/complete", null),
            await other.GetAsync($"/api/v1/build/projects/{projectId}/non-conformities"),
            await other.GetAsync($"/api/v1/build/non-conformities/{nc}"),
            await other.PutAsJsonAsync($"/api/v1/build/non-conformities/{nc}", new { title = "x" }),
            await other.PostAsync($"/api/v1/build/non-conformities/{nc}/start", null),
            await other.GetAsync($"/api/v1/build/projects/{projectId}/quality/summary"),
            await other.GetAsync($"/api/v1/build/projects/{projectId}/handover"),
            await other.PutAsJsonAsync($"/api/v1/build/projects/{projectId}/handover", new { notes = "x" }),
            await other.PostAsJsonAsync($"/api/v1/build/projects/{projectId}/handover/accept", new { signerName = "x" }),
            await other.GetAsync($"/api/v1/build/projects/{projectId}/warranty"),
            await other.PutAsJsonAsync($"/api/v1/build/warranty-terms/{term}", new { system = "x", months = 1 }),
            await other.GetAsync($"/api/v1/build/projects/{projectId}/assistance"),
            await other.GetAsync($"/api/v1/build/assistance/{assistance}"),
            await other.PostAsync($"/api/v1/build/assistance/{assistance}/triage", null),
            await other.PostAsJsonAsync($"/api/v1/build/assistance/{assistance}/reject", new { reason = "x" }),
        };
        foreach (var r in notFound)
            r.StatusCode.Should().Be(HttpStatusCode.NotFound, $"{r.RequestMessage!.Method} {r.RequestMessage.RequestUri}");
        (await Json(await other.GetAsync("/api/v1/build/assistance"), HttpStatusCode.OK)).EnumerateArray()
            .Should().NotContain(a => a.GetProperty("id").GetGuid() == assistance);
        (await Json(await other.GetAsync("/api/v1/build/checklist-templates"), HttpStatusCode.OK)).EnumerateArray()
            .Should().NotContain(t => t.GetProperty("id").GetGuid() == template);

        (await Json(await admin.GetAsync($"/api/v1/build/non-conformities/{nc}"), HttpStatusCode.OK))
            .GetProperty("status").GetString().Should().Be("Open");
        (await Json(await admin.GetAsync($"/api/v1/build/assistance/{assistance}"), HttpStatusCode.OK))
            .GetProperty("status").GetString().Should().Be("Open");
    }
}
