using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nexo.Application.Integrations.Contracts;
using Nexo.Infrastructure.Modules.Build;
using Nexo.Infrastructure.Persistence;
using Nexo.IntegrationTests.Common;
using Nexo.IntegrationTests.Helpers;
using Nexo.IntegrationTests.Security;

namespace Nexo.IntegrationTests.Build;

/// <summary>
/// Regras do gêmeo digital do lado da empresa (BuildTwinService) sobre o IFC de teste processado (7 elementos, Térreo com 3
/// paredes e 1 laje): zonas manuais (pai, ciclos, folha, exclusão), filtros de elementos por estado e por ids, vínculos de
/// etapa (cada escopo e cada recusa), vínculo em massa e eventos explícitos de elemento.
/// </summary>
[Collection("Integration")]
public class BuildTwinRulesTests
{
    private readonly TestWebApplicationFactory _factory;

    public BuildTwinRulesTests(TestWebApplicationFactory factory) => _factory = factory;

    private static string Gid(string prefix) => prefix.PadRight(22, '0');
    private static readonly string[] GroundWalls = [Gid("WallT1"), Gid("WallT2"), Gid("WallT3")];

    private sealed record Twin(HttpClient Api, Guid ProjectId, Guid VersionId, Guid StageId, WebApplicationFactory<Program> Host);

    private static async Task<JsonElement> Json(HttpResponseMessage r, HttpStatusCode expected)
    {
        var body = await r.Content.ReadAsStringAsync();
        r.StatusCode.Should().Be(expected, body);
        return string.IsNullOrWhiteSpace(body) ? default : JsonDocument.Parse(body).RootElement.Clone();
    }

    private static async Task<string> Code(HttpResponseMessage r, HttpStatusCode expected)
        => (await Json(r, expected)).GetProperty("code").GetString()!;

    private static async Task<Guid> StageAsync(HttpClient c, Guid projectId, string name)
        => (await Json(await c.PostAsJsonAsync($"/api/v1/build/projects/{projectId}/stages", new
        {
            name, plannedStart = "2026-01-05", plannedEnd = "2026-12-20", weight = 1m,
        }), HttpStatusCode.Created)).GetProperty("id").GetGuid();

    private static async Task<Guid> ProjectAsync(HttpClient c, string name)
        => (await Json(await c.PostAsJsonAsync("/api/v1/build/projects", new
        {
            name, clientName = "Cliente Regras", type = "House", startDate = "2026-01-05", expectedEndDate = "2026-12-20",
        }), HttpStatusCode.Created)).GetProperty("id").GetGuid();

    private static byte[] Ifc()
    {
        using var s = typeof(BuildTwinRulesTests).Assembly.GetManifestResourceStream("casa-teste.ifc")!;
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    }

    /// <summary>A project with one stage and the test IFC uploaded and processed (3D on, storage in memory).</summary>
    private async Task<Twin> TwinAsync(string projectName)
    {
        var admin = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var platform = await PlatformAuthorizationTests.PlatformClientAsync(_factory);
        var projectId = await ProjectAsync(admin, projectName);
        var stageId = await StageAsync(admin, projectId, "Alvenaria");
        Guid tenant;
        using (var scope = _factory.Services.CreateScope())
            tenant = await scope.ServiceProvider.GetRequiredService<NexoDbContext>().BldProjects.IgnoreQueryFilters()
                .Where(p => p.Id == projectId).Select(p => p.TenantId).SingleAsync();
        await Json(await platform.PutAsJsonAsync($"/api/platform/tenants/{tenant}/build-settings", new { build3DEnabled = true }),
            HttpStatusCode.OK);

        var storage = new InMemoryStorageProvider();
        var host = _factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("Integrations:Features:StorageEnabled", "true");
            b.ConfigureTestServices(s =>
            {
                s.RemoveAll<IStorageProvider>();
                s.AddSingleton<IStorageProvider>(storage);
            });
        });
        var api = host.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        api.DefaultRequestHeaders.Authorization = admin.DefaultRequestHeaders.Authorization;

        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(Ifc());
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(file, "file", "casa.ifc");
        form.Add(new StringContent("Arquitetura"), "name");
        form.Add(new StringContent("Arquitetura"), "discipline");
        var versionId = (await Json(await api.PostAsync($"/api/v1/build/projects/{projectId}/models", form), HttpStatusCode.Created))
            .GetProperty("version").GetProperty("id").GetGuid();
        for (var i = 0; i < 50; i++)
        {
            using var scope = host.Services.CreateScope();
            if (!await scope.ServiceProvider.GetRequiredService<IBuildModelProcessor>().ProcessNextAsync(CancellationToken.None)) break;
        }
        (await Json(await api.GetAsync($"/api/v1/build/models/versions/{versionId}"), HttpStatusCode.OK))
            .GetProperty("status").GetString().Should().Be("Ready");
        return new Twin(api, projectId, versionId, stageId, host);
    }

    private static async Task<Guid> ModelZoneAsync(Twin t, string externalIdPrefix)
        => (await Json(await t.Api.GetAsync($"/api/v1/build/models/versions/{t.VersionId}/zones"), HttpStatusCode.OK)).EnumerateArray()
            .Single(z => z.GetProperty("externalId").GetString() == Gid(externalIdPrefix)).GetProperty("id").GetGuid();

    private static IEnumerable<string> ExternalIds(JsonElement page)
        => page.GetProperty("items").EnumerateArray().Select(e => e.GetProperty("externalId").GetString()!);

    // ── Zonas manuais ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Manual_zones_form_a_tree_without_cycles_and_only_they_can_be_changed_or_removed()
    {
        var t = await TwinAsync("Obra regras zonas");
        await using var _h = t.Host;
        var zones = $"/api/v1/build/projects/{t.ProjectId}/zones";

        var bloco = (await Json(await t.Api.PostAsJsonAsync(zones, new { kind = "Floor", name = "Bloco A", order = 1 }), HttpStatusCode.Created))
            .GetProperty("id").GetGuid();
        var sala = (await Json(await t.Api.PostAsJsonAsync(zones, new { kind = "Room", name = "Sala", order = 0, parentZoneId = bloco }),
            HttpStatusCode.Created)).GetProperty("id").GetGuid();
        var canto = (await Json(await t.Api.PostAsJsonAsync(zones, new { kind = "Area", name = "Canto", order = 2, parentZoneId = sala }),
            HttpStatusCode.Created)).GetProperty("id").GetGuid();

        // the project's manual zones only (not the model's), by order
        var listed = await Json(await t.Api.GetAsync(zones), HttpStatusCode.OK);
        listed.EnumerateArray().Select(z => z.GetProperty("name").GetString()).Should().Equal("Sala", "Bloco A", "Canto");
        listed.EnumerateArray().Should().OnlyContain(z => z.GetProperty("modelVersionId").ValueKind == JsonValueKind.Null);

        // parent rules: itself, one of its own sub-zones, a zone of another project
        (await Code(await t.Api.PutAsJsonAsync($"/api/v1/build/zones/{sala}", new { kind = "Room", name = "Sala", parentZoneId = sala }),
            HttpStatusCode.UnprocessableEntity)).Should().Be("invalid_parent_zone");
        (await Code(await t.Api.PutAsJsonAsync($"/api/v1/build/zones/{bloco}", new { kind = "Floor", name = "Bloco A", parentZoneId = canto }),
            HttpStatusCode.UnprocessableEntity)).Should().Be("invalid_parent_zone");
        (await Code(await t.Api.PostAsJsonAsync(zones, new { kind = "Area", name = "Solta", parentZoneId = Guid.NewGuid() }),
            HttpStatusCode.UnprocessableEntity)).Should().Be("invalid_parent_zone");
        // a sheet must be a PDF/image/DXF of this project: the IFC is not one
        (await Code(await t.Api.PostAsJsonAsync(zones, new { kind = "Area", name = "Na folha", sheetVersionId = t.VersionId }),
            HttpStatusCode.UnprocessableEntity)).Should().Be("invalid_sheet");

        // a valid move: Canto goes straight under Bloco A, renamed
        var moved = await Json(await t.Api.PutAsJsonAsync($"/api/v1/build/zones/{canto}",
            new { kind = "Area", name = "Canto da varanda", order = 3, parentZoneId = bloco }), HttpStatusCode.OK);
        moved.GetProperty("name").GetString().Should().Be("Canto da varanda");
        moved.GetProperty("parentZoneId").GetGuid().Should().Be(bloco);
        moved.GetProperty("order").GetInt32().Should().Be(3);

        // zones read from the model cannot be removed; manual ones can, once
        var terreo = await ModelZoneAsync(t, "StoreyT");
        (await Code(await t.Api.DeleteAsync($"/api/v1/build/zones/{terreo}"), HttpStatusCode.UnprocessableEntity)).Should().Be("zone_from_model");
        (await t.Api.DeleteAsync($"/api/v1/build/zones/{canto}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await t.Api.DeleteAsync($"/api/v1/build/zones/{canto}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await t.Api.PutAsJsonAsync($"/api/v1/build/zones/{canto}", new { kind = "Area", name = "X" })).StatusCode
            .Should().Be(HttpStatusCode.NotFound);
        (await Json(await t.Api.GetAsync(zones), HttpStatusCode.OK)).GetArrayLength().Should().Be(2);
    }

    // ── Elementos e estado ────────────────────────────────────────────────────

    [Fact]
    public async Task Elements_can_be_filtered_by_state_by_unbound_and_by_exact_external_ids()
    {
        var t = await TwinAsync("Obra regras elementos");
        await using var _h = t.Host;
        var terreo = await ModelZoneAsync(t, "StoreyT");
        (await Json(await t.Api.PostAsJsonAsync($"/api/v1/build/projects/{t.ProjectId}/twin/bindings/bulk", new
        {
            stageId = t.StageId, modelVersionId = t.VersionId, category = "IfcWall", zoneId = terreo,
        }), HttpStatusCode.OK)).GetProperty("matched").GetInt32().Should().Be(3);
        var elements = $"/api/v1/build/models/versions/{t.VersionId}/elements";

        // a stage without progress: its elements are NotStarted; the other 4 have no stage
        var notStarted = await Json(await t.Api.GetAsync($"{elements}?state=NotStarted"), HttpStatusCode.OK);
        notStarted.GetProperty("total").GetInt32().Should().Be(3);
        ExternalIds(notStarted).Should().BeEquivalentTo(GroundWalls);
        notStarted.GetProperty("items").EnumerateArray().Should().OnlyContain(e =>
            e.GetProperty("state").GetString() == "NotStarted" && e.GetProperty("stageId").GetGuid() == t.StageId);

        var unbound = await Json(await t.Api.GetAsync($"{elements}?state=unbound&take=2"), HttpStatusCode.OK);
        unbound.GetProperty("total").GetInt32().Should().Be(4);
        unbound.GetProperty("items").GetArrayLength().Should().Be(2, "the page size applies after the state filter");
        unbound.GetProperty("items").EnumerateArray().Should().OnlyContain(e => e.GetProperty("state").ValueKind == JsonValueKind.Null);
        (await Json(await t.Api.GetAsync($"{elements}?state=Unbound&skip=2&take=2"), HttpStatusCode.OK))
            .GetProperty("items").GetArrayLength().Should().Be(2);

        (await Json(await t.Api.GetAsync($"{elements}?state=Completed"), HttpStatusCode.OK)).GetProperty("total").GetInt32().Should().Be(0);
        (await Code(await t.Api.GetAsync($"{elements}?state=Pronto"), HttpStatusCode.UnprocessableEntity)).Should().Be("invalid_value");

        // exact ids, trimmed and deduplicated; unknown ids simply match nothing
        var byIds = await Json(await t.Api.GetAsync($"{elements}?externalId={Gid("WallT1")},%20{Gid("Slab1")},{Gid("WallT1")},nao-existe"),
            HttpStatusCode.OK);
        ExternalIds(byIds).Should().BeEquivalentTo(Gid("WallT1"), Gid("Slab1"));
        var tooMany = string.Join(",", Enumerable.Range(0, BuildTwinService.MaxExternalIdFilter + 1).Select(i => $"id-{i}"));
        (await Code(await t.Api.GetAsync($"{elements}?externalId={tooMany}"), HttpStatusCode.UnprocessableEntity)).Should().Be("too_many_ids");

        // a zone that is neither of this version nor a manual zone of the project
        (await Code(await t.Api.GetAsync($"{elements}?zoneId={Guid.NewGuid()}"), HttpStatusCode.UnprocessableEntity)).Should().Be("invalid_zone");
    }

    // ── Vínculos ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Each_binding_scope_checks_what_it_points_to_and_bindings_are_listed_and_removed()
    {
        var t = await TwinAsync("Obra regras vínculos");
        await using var _h = t.Host;
        var bindings = $"/api/v1/build/projects/{t.ProjectId}/twin/bindings";
        var terreo = await ModelZoneAsync(t, "StoreyT");
        var other = await ProjectAsync(t.Api, "Outra obra regras");
        var otherStage = await StageAsync(t.Api, other, "Outra etapa");
        var manual = (await Json(await t.Api.PostAsJsonAsync($"/api/v1/build/projects/{other}/zones", new { kind = "Area", name = "Zona alheia" }),
            HttpStatusCode.Created)).GetProperty("id").GetGuid();

        // refusals
        (await Code(await t.Api.PostAsJsonAsync(bindings, new { stageId = otherStage, scope = "Category", category = "IfcWall" }),
            HttpStatusCode.UnprocessableEntity)).Should().Be("stage_not_of_project");
        (await Code(await t.Api.PostAsJsonAsync($"/api/v1/build/projects/{other}/twin/bindings",
            new { stageId = otherStage, scope = "Element", modelVersionId = t.VersionId, elementExternalId = Gid("WallT1") }),
            HttpStatusCode.UnprocessableEntity)).Should().Be("invalid_model_version");
        (await Code(await t.Api.PostAsJsonAsync(bindings, new { stageId = t.StageId, scope = "Element", elementExternalId = Gid("WallT1") }),
            HttpStatusCode.UnprocessableEntity)).Should().Be("invalid_binding");
        (await Code(await t.Api.PostAsJsonAsync(bindings, new { stageId = t.StageId, scope = "Element", modelVersionId = t.VersionId, elementExternalId = "  " }),
            HttpStatusCode.UnprocessableEntity)).Should().Be("invalid_binding");
        (await Code(await t.Api.PostAsJsonAsync(bindings, new { stageId = t.StageId, scope = "Element", modelVersionId = t.VersionId, elementExternalId = "nao-existe" }),
            HttpStatusCode.UnprocessableEntity)).Should().Be("element_not_found");
        (await Code(await t.Api.PostAsJsonAsync(bindings, new { stageId = t.StageId, scope = "Zone" }),
            HttpStatusCode.UnprocessableEntity)).Should().Be("invalid_zone");
        (await Code(await t.Api.PostAsJsonAsync(bindings, new { stageId = t.StageId, scope = "Zone", zoneId = manual }),
            HttpStatusCode.UnprocessableEntity)).Should().Be("invalid_zone");
        (await Code(await t.Api.PostAsJsonAsync(bindings, new { stageId = t.StageId, scope = "Category" }),
            HttpStatusCode.UnprocessableEntity)).Should().Be("invalid_binding");
        (await Code(await t.Api.PostAsJsonAsync(bindings, new { stageId = t.StageId, scope = "Planta" }),
            HttpStatusCode.UnprocessableEntity)).Should().Be("invalid_value");

        // one of each scope; a zone binding takes the version of its zone
        var element = await Json(await t.Api.PostAsJsonAsync(bindings,
            new { stageId = t.StageId, scope = "Element", modelVersionId = t.VersionId, elementExternalId = $" {Gid("WallT1")} " }),
            HttpStatusCode.Created);
        element.GetProperty("stageName").GetString().Should().Be("Alvenaria");
        element.GetProperty("source").GetString().Should().Be("Manual");
        var zone = await Json(await t.Api.PostAsJsonAsync(bindings, new { stageId = t.StageId, scope = "Zone", zoneId = terreo }),
            HttpStatusCode.Created);
        zone.GetProperty("modelVersionId").GetGuid().Should().Be(t.VersionId);
        var category = await Json(await t.Api.PostAsJsonAsync(bindings, new { stageId = t.StageId, scope = "Category", category = "IfcSlab" }),
            HttpStatusCode.Created);
        category.GetProperty("scope").GetString().Should().Be("Category");

        var listed = await Json(await t.Api.GetAsync(bindings), HttpStatusCode.OK);
        listed.EnumerateArray().Select(b => b.GetProperty("scope").GetString()).Should().BeEquivalentTo("Element", "Zone", "Category");
        listed.EnumerateArray().Should().OnlyContain(b => b.GetProperty("stageName").GetString() == "Alvenaria");

        var zoneBinding = zone.GetProperty("id").GetGuid();
        (await t.Api.DeleteAsync($"/api/v1/build/twin/bindings/{zoneBinding}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await t.Api.DeleteAsync($"/api/v1/build/twin/bindings/{zoneBinding}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await Json(await t.Api.GetAsync(bindings), HttpStatusCode.OK)).GetArrayLength().Should().Be(2);
    }

    [Fact]
    public async Task Bulk_binding_needs_a_filter_and_replaces_or_keeps_the_existing_bindings()
    {
        var t = await TwinAsync("Obra regras massa");
        await using var _h = t.Host;
        var bulk = $"/api/v1/build/projects/{t.ProjectId}/twin/bindings/bulk";
        var reboco = await StageAsync(t.Api, t.ProjectId, "Reboco");

        (await Code(await t.Api.PostAsJsonAsync(bulk, new { stageId = t.StageId, modelVersionId = t.VersionId, category = " ", externalIds = Array.Empty<string>() }),
            HttpStatusCode.UnprocessableEntity)).Should().Be("invalid_binding");
        (await Code(await t.Api.PostAsJsonAsync(bulk, new
        {
            stageId = t.StageId, modelVersionId = t.VersionId,
            externalIds = Enumerable.Range(0, BuildTwinService.MaxBulkIds + 1).Select(i => $"id-{i}").ToArray(),
        }), HttpStatusCode.UnprocessableEntity)).Should().Be("invalid_binding");
        var manual = (await Json(await t.Api.PostAsJsonAsync($"/api/v1/build/projects/{t.ProjectId}/zones", new { kind = "Area", name = "Manual" }),
            HttpStatusCode.Created)).GetProperty("id").GetGuid();
        (await Code(await t.Api.PostAsJsonAsync(bulk, new { stageId = t.StageId, modelVersionId = t.VersionId, zoneId = manual }),
            HttpStatusCode.UnprocessableEntity)).Should().Be("invalid_zone", "bulk binding works on the zones of the version only");

        // the two ground-floor walls by id go to Alvenaria
        var byIds = await Json(await t.Api.PostAsJsonAsync(bulk, new
        {
            stageId = t.StageId, modelVersionId = t.VersionId, externalIds = new[] { Gid("WallT1"), $" {Gid("WallT2")}", "" },
        }), HttpStatusCode.OK);
        (byIds.GetProperty("matched").GetInt32(), byIds.GetProperty("created").GetInt32(), byIds.GetProperty("replaced").GetInt32())
            .Should().Be((2, 2, 0));

        // every wall to Reboco, replacing: the two existing element bindings go away
        var replaced = await Json(await t.Api.PostAsJsonAsync(bulk, new { stageId = reboco, modelVersionId = t.VersionId, category = "IfcWall" }),
            HttpStatusCode.OK);
        replaced.GetProperty("matched").GetInt32().Should().Be(5);
        replaced.GetProperty("replaced").GetInt32().Should().Be(2);

        // the slabs kept alongside (replaceExisting = false)
        var kept = await Json(await t.Api.PostAsJsonAsync(bulk, new
        {
            stageId = t.StageId, modelVersionId = t.VersionId, category = "IfcSlab", replaceExisting = false,
        }), HttpStatusCode.OK);
        kept.GetProperty("matched").GetInt32().Should().Be(2);
        kept.GetProperty("replaced").GetInt32().Should().Be(0);

        var all = await Json(await t.Api.GetAsync($"/api/v1/build/projects/{t.ProjectId}/twin/bindings"), HttpStatusCode.OK);
        all.EnumerateArray().Count(b => b.GetProperty("stageName").GetString() == "Reboco").Should().Be(5);
        all.EnumerateArray().Count(b => b.GetProperty("stageName").GetString() == "Alvenaria").Should().Be(2);
    }

    // ── Eventos explícitos ────────────────────────────────────────────────────

    [Fact]
    public async Task Element_events_need_known_elements_and_unknown_elements_have_no_history()
    {
        var t = await TwinAsync("Obra regras eventos");
        await using var _h = t.Host;
        var events = $"/api/v1/build/projects/{t.ProjectId}/twin/element-events";

        (await Code(await t.Api.PostAsJsonAsync(events, new { modelVersionId = t.VersionId, externalIds = new[] { " ", "" }, state = "Completed" }),
            HttpStatusCode.UnprocessableEntity)).Should().Be("invalid_elements");
        (await Code(await t.Api.PostAsJsonAsync(events, new
        {
            modelVersionId = t.VersionId, externalIds = new[] { Gid("WallT1"), "nao-existe" }, state = "Completed",
        }), HttpStatusCode.UnprocessableEntity)).Should().Be("element_not_found");
        (await Code(await t.Api.PostAsJsonAsync(events, new
        {
            modelVersionId = t.VersionId, externalIds = new[] { Gid("WallT1") }, state = "Completed", source = "Satélite",
        }), HttpStatusCode.UnprocessableEntity)).Should().Be("invalid_value");

        var recorded = await Json(await t.Api.PostAsJsonAsync(events, new
        {
            modelVersionId = t.VersionId, externalIds = new[] { Gid("WallT1"), Gid("WallT1") }, state = "Blocked", source = "DailyLog",
            note = "Falta material",
        }), HttpStatusCode.Created);
        recorded.GetArrayLength().Should().Be(1, "repeated ids are recorded once");

        var history = await Json(await t.Api.GetAsync($"/api/v1/build/models/versions/{t.VersionId}/elements/{Gid("WallT1")}/events"),
            HttpStatusCode.OK);
        var only = history.EnumerateArray().Single();
        only.GetProperty("state").GetString().Should().Be("Blocked");
        only.GetProperty("source").GetString().Should().Be("DailyLog");
        (await t.Api.GetAsync($"/api/v1/build/models/versions/{t.VersionId}/elements/nao-existe/events")).StatusCode
            .Should().Be(HttpStatusCode.NotFound);
        (await t.Api.GetAsync($"/api/v1/build/models/versions/{Guid.NewGuid()}/elements/{Gid("WallT1")}/events")).StatusCode
            .Should().Be(HttpStatusCode.NotFound);
    }
}
