using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
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
using Nexo.IntegrationTests.Security;

namespace Nexo.IntegrationTests.Build;

/// <summary>
/// Orken Build Wave 4 "Digital Twin", end to end: critical path 4 (3D on → hand-written IFC4 upload → queue processed →
/// elements and zones → walls of the ground floor bound to "Alvenaria" → stage progress 50% then 100% on different days → state
/// on each date → explicit override → snapshot untouched → portal twin without any source file → browser-converted GLB attached
/// → signed viewer URL), RVT refusal without storage, Build3D off (403), platform flags (PlatformAdmin only), reality capture
/// magic bytes and tenant isolation of every new resource.
/// </summary>
[Collection("Integration")]
public class BuildTwinTests
{
    private readonly TestWebApplicationFactory _factory;

    /// <summary>One in-memory store for every storage-enabled host of this class: a job queued by one test can be read by another.</summary>
    private static readonly InMemoryStorageProvider Storage = new();

    public BuildTwinTests(TestWebApplicationFactory factory) => _factory = factory;

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string Gid(string prefix) => prefix.PadRight(22, '0');
    private static DateOnly LocalToday => DateOnly.FromDateTime(DateTime.UtcNow.AddHours(-3));
    private static string Day(int offset) => LocalToday.AddDays(offset).ToString("yyyy-MM-dd");
    private static string Slug(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..(prefix.Length + 9)];

    private static async Task<JsonElement> Json(HttpResponseMessage r, HttpStatusCode expected)
    {
        var body = await r.Content.ReadAsStringAsync();
        r.StatusCode.Should().Be(expected, body);
        return string.IsNullOrWhiteSpace(body) ? default : JsonDocument.Parse(body).RootElement.Clone();
    }

    private static async Task<string> Code(HttpResponseMessage r, HttpStatusCode expected)
        => (await Json(r, expected)).GetProperty("code").GetString()!;

    private WebApplicationFactory<Program> StorageHost()
        => _factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("Integrations:Features:StorageEnabled", "true");
            b.ConfigureTestServices(s =>
            {
                s.RemoveAll<IStorageProvider>();
                s.AddSingleton<IStorageProvider>(Storage);
            });
        });

    /// <summary>A client of <paramref name="host"/> carrying the same bearer token as <paramref name="authed"/>.</summary>
    private static HttpClient As(WebApplicationFactory<Program> host, HttpClient authed)
    {
        var c = host.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        c.DefaultRequestHeaders.Authorization = authed.DefaultRequestHeaders.Authorization;
        return c;
    }

    /// <summary>Runs the model queue to the end (deterministic: no background worker in Testing).</summary>
    private static async Task DrainAsync(WebApplicationFactory<Program> host)
    {
        for (var i = 0; i < 50; i++)
        {
            using var scope = host.Services.CreateScope();
            if (!await scope.ServiceProvider.GetRequiredService<IBuildModelProcessor>().ProcessNextAsync(CancellationToken.None)) return;
        }
    }

    private static MultipartFormDataContent File(byte[] bytes, string fileName, params (string Name, string Value)[] fields)
    {
        var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        content.Add(file, "file", fileName);
        foreach (var (name, value) in fields) content.Add(new StringContent(value), name);
        return content;
    }

    private static byte[] Ifc()
    {
        using var s = typeof(BuildTwinTests).Assembly.GetManifestResourceStream("casa-teste.ifc")!;
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    }

    /// <summary>A minimal valid GLB: a root group with one mesh node per name.</summary>
    private static byte[] Glb(IEnumerable<string> nodeNames)
    {
        var names = nodeNames.ToList();
        var nodes = new List<object> { new { name = "Raiz", children = Enumerable.Range(1, names.Count).ToArray() } };
        nodes.AddRange(names.Select(n => (object)new { name = n, mesh = 0 }));
        var json = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            asset = new { version = "2.0" },
            scene = 0,
            scenes = new[] { new { nodes = new[] { 0 } } },
            nodes,
            meshes = new[] { new { primitives = new[] { new { attributes = new { POSITION = 0 } } } } },
            accessors = new[] { new { bufferView = 0, componentType = 5126, count = 3, type = "VEC3" } },
            bufferViews = new[] { new { buffer = 0, byteLength = 36 } },
            buffers = new[] { new { byteLength = 36 } },
        }));
        var jsonPadded = new byte[(json.Length + 3) / 4 * 4];
        Array.Fill(jsonPadded, (byte)0x20);
        json.CopyTo(jsonPadded, 0);
        var bin = new byte[36];
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write(0x46546C67u); w.Write(2u); w.Write((uint)(12 + 8 + jsonPadded.Length + 8 + bin.Length));
        w.Write((uint)jsonPadded.Length); w.Write(0x4E4F534Au); w.Write(jsonPadded);
        w.Write((uint)bin.Length); w.Write(0x004E4942u); w.Write(bin);
        w.Flush();
        return ms.ToArray();
    }

    private static byte[] Mp4()
    {
        var bytes = new byte[256];
        new byte[] { 0, 0, 0, 0x18, (byte)'f', (byte)'t', (byte)'y', (byte)'p', (byte)'i', (byte)'s', (byte)'o', (byte)'m' }.CopyTo(bytes, 0);
        return bytes;
    }

    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0, 0, 0, 0, 0];
    private static readonly byte[] Rvt = [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];

    private static async Task<Guid> ProjectAsync(HttpClient c, string name)
        => (await Json(await c.PostAsJsonAsync("/api/v1/build/projects", new
        {
            name, clientName = "Cliente Gêmeo", type = "House", startDate = "2026-01-05", expectedEndDate = "2026-12-20",
        }), HttpStatusCode.Created)).GetProperty("id").GetGuid();

    private static async Task<Guid> StageAsync(HttpClient c, Guid projectId, string name)
        => (await Json(await c.PostAsJsonAsync($"/api/v1/build/projects/{projectId}/stages", new
        {
            name, plannedStart = "2026-01-05", plannedEnd = "2026-12-20", weight = 1m,
        }), HttpStatusCode.Created)).GetProperty("id").GetGuid();

    private static async Task<string> PortalAsync(HttpClient c, Guid projectId, object visibility, string? password = null)
    {
        var company = Slug("gemeo");
        await Json(await c.PutAsJsonAsync("/api/v1/build/portal/company", new
        {
            slug = company, displayName = "Construtora Gêmeo", contactEmail = "contato@" + TestCredentials.TestDomain,
        }), HttpStatusCode.OK);
        var slug = Slug("obra");
        await Json(await c.PutAsJsonAsync($"/api/v1/build/projects/{projectId}/portal", new { slug, access = "Public", title = "Minha obra", visibility }),
            HttpStatusCode.OK);
        if (password is not null)
        {
            await Json(await c.PutAsJsonAsync($"/api/v1/build/projects/{projectId}/portal/password", new { password }), HttpStatusCode.OK);
            await Json(await c.PutAsJsonAsync($"/api/v1/build/projects/{projectId}/portal",
                new { slug, access = "Password", title = "Minha obra", visibility }), HttpStatusCode.OK);
        }
        await Json(await c.PostAsync($"/api/v1/build/projects/{projectId}/portal/publish", null), HttpStatusCode.OK);
        return $"/api/public/build/{company}/{slug}";
    }

    private async Task<Guid> TenantOfAsync(Guid projectId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        return await db.BldProjects.IgnoreQueryFilters().Where(p => p.Id == projectId).Select(p => p.TenantId).SingleAsync();
    }

    private async Task<(HttpClient Client, Guid TenantId)> OtherTenantWithBuildAsync()
    {
        var email = $"bld4-{Guid.NewGuid():N}"[..17] + "@" + TestCredentials.TestDomain;
        var reg = await _factory.CreateApiClient().PostAsJsonAsync("/api/auth/register",
            new { name = "Outra Construtora W4", email, password = TestCredentials.AdminPassword });
        reg.StatusCode.Should().Be(HttpStatusCode.OK, await reg.Content.ReadAsStringAsync());
        Guid tenantId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
            var user = await db.Users.IgnoreQueryFilters().SingleAsync(u => u.Login == email);
            user.Activate();
            tenantId = user.TenantId;
            db.ModuleSubscriptions.Add(ModuleSubscription.CreateAdminGrant(user.TenantId, "build"));
            await db.SaveChangesAsync();
        }
        return (await AuthClientFactory.LoginAsync(_factory, email, TestCredentials.AdminPassword), tenantId);
    }

    private static IEnumerable<string> Ids(JsonElement state, string name)
        => state.GetProperty("states").TryGetProperty(name, out var list) ? list.EnumerateArray().Select(x => x.GetString()!) : [];

    // ── Critical path 4 ───────────────────────────────────────────────────────

    [Fact]
    public async Task Critical_path_4_ifc_to_4d_state_and_the_portal_twin_without_ever_exposing_the_source()
    {
        var admin = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var platform = await PlatformAuthorizationTests.PlatformClientAsync(_factory);
        var projectId = await ProjectAsync(admin, "Obra CP4 gêmeo");
        var alvenaria = await StageAsync(admin, projectId, "Alvenaria");
        var tenant = await TenantOfAsync(projectId);

        // ── 3D on (platform, audited) and visible to the company ──
        (await Json(await platform.PutAsJsonAsync($"/api/platform/tenants/{tenant}/build-settings", new { build3DEnabled = true }),
            HttpStatusCode.OK)).GetProperty("build3DEnabled").GetBoolean().Should().BeTrue();
        (await Json(await admin.GetAsync("/api/v1/build/settings"), HttpStatusCode.OK)).GetProperty("build3DEnabled").GetBoolean().Should().BeTrue();

        await using var host = StorageHost();
        var api = As(host, admin);

        // ── upload: stored privately under a key that never uses the client's name, queued ──
        var upload = await Json(await api.PostAsync($"/api/v1/build/projects/{projectId}/models",
            File(Ifc(), "../../etc/Projeto Casa.ifc", ("name", "Arquitetura casa"), ("discipline", "Arquitetura"))), HttpStatusCode.Created);
        var modelId = upload.GetProperty("model").GetProperty("id").GetGuid();
        var version = upload.GetProperty("version");
        var versionId = version.GetProperty("id").GetGuid();
        version.GetProperty("status").GetString().Should().Be("Queued");
        version.GetProperty("sourceFormat").GetString().Should().Be("Ifc");
        version.GetProperty("sourceFileName").GetString().Should().Be("Projeto Casa.ifc");
        version.GetProperty("sha256").GetString().Should().HaveLength(64);
        version.ToString().Should().NotContain("private/", "storage keys never leave the API");
        var sourceKey = $"private/tenants/{tenant}/build/models/{modelId}/{versionId}/source.ifc";
        Storage.Objects.Should().ContainKey(sourceKey);

        // ── processing (driven by the test) ──
        await DrainAsync(host);
        var ready = await Json(await api.GetAsync($"/api/v1/build/models/versions/{versionId}"), HttpStatusCode.OK);
        ready.GetProperty("status").GetString().Should().Be("Ready", ready.ToString());
        ready.GetProperty("elementCount").GetInt32().Should().Be(7);
        ready.GetProperty("storeyCount").GetInt32().Should().Be(2);
        ready.GetProperty("schema").GetString().Should().Be("IFC4");
        ready.GetProperty("geometrySource").GetString().Should().Be("NativeBim");
        ready.GetProperty("hasViewer").GetBoolean().Should().BeFalse();
        ready.GetProperty("statusMessage").GetString().Should().Contain("navegador");
        (await Json(await api.GetAsync($"/api/v1/build/models/{modelId}"), HttpStatusCode.OK))
            .GetProperty("currentVersionId").GetGuid().Should().Be(versionId);

        var zones = await Json(await api.GetAsync($"/api/v1/build/models/versions/{versionId}/zones"), HttpStatusCode.OK);
        zones.GetArrayLength().Should().Be(5);
        var terreo = zones.EnumerateArray().Single(z => z.GetProperty("externalId").GetString() == Gid("StoreyT"));
        terreo.GetProperty("name").GetString().Should().Be("Térreo");
        terreo.GetProperty("elementCount").GetInt32().Should().Be(4);
        var terreoId = terreo.GetProperty("id").GetGuid();

        var walls = await Json(await api.GetAsync($"/api/v1/build/models/versions/{versionId}/elements?category=IfcWall&zoneId={terreoId}"),
            HttpStatusCode.OK);
        walls.GetProperty("total").GetInt32().Should().Be(3);
        var wall = walls.GetProperty("items").EnumerateArray().First();
        wall.GetProperty("material").GetString().Should().Be("Bloco cerâmico");
        wall.GetProperty("properties").GetProperty("netVolume").GetDouble().Should().Be(2.4);
        walls.GetProperty("categories").EnumerateArray().Select(c => c.GetString()).Should().BeEquivalentTo(["IfcSlab", "IfcWall"]);

        // ── bind the ground-floor walls to Alvenaria ──
        (await Json(await api.PostAsJsonAsync($"/api/v1/build/projects/{projectId}/twin/bindings/bulk", new
        {
            stageId = alvenaria, modelVersionId = versionId, category = "IfcWall", zoneId = terreoId,
        }), HttpStatusCode.OK)).GetProperty("matched").GetInt32().Should().Be(3);
        (await Code(await api.PostAsJsonAsync($"/api/v1/build/projects/{projectId}/twin/bindings/bulk", new
        {
            stageId = alvenaria, modelVersionId = versionId, externalIds = new[] { "nao-existe" },
        }), HttpStatusCode.UnprocessableEntity)).Should().Be("element_not_found");

        // ── stage progress on two different days ──
        await Json(await api.PutAsJsonAsync($"/api/v1/build/stages/{alvenaria}/progress", new { progressPercent = 50, effectiveDate = Day(-20) }),
            HttpStatusCode.OK);
        await Json(await api.PutAsJsonAsync($"/api/v1/build/stages/{alvenaria}/progress", new { progressPercent = 100, effectiveDate = Day(-10) }),
            HttpStatusCode.OK);

        var groundWalls = new[] { Gid("WallT1"), Gid("WallT2"), Gid("WallT3") };
        async Task<JsonElement> StateAt(string day) => await Json(await api.GetAsync($"/api/v1/build/models/versions/{versionId}/state?date={day}"), HttpStatusCode.OK);
        var before = await StateAt(Day(-25));
        Ids(before, "NotStarted").Should().BeEquivalentTo(groundWalls);
        before.GetProperty("unbound").GetInt32().Should().Be(4);
        var during = await StateAt(Day(-15));
        Ids(during, "InProgress").Should().BeEquivalentTo(groundWalls);
        during.GetProperty("counts").GetProperty("InProgress").GetInt32().Should().Be(3);
        var after = await StateAt(Day(-5));
        Ids(after, "Completed").Should().BeEquivalentTo(groundWalls);

        // ETag: unchanged state → 304
        var first = await api.GetAsync($"/api/v1/build/models/versions/{versionId}/state?date={Day(-5)}");
        var etag = first.Headers.ETag!.Tag;
        using (var conditional = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/build/models/versions/{versionId}/state?date={Day(-5)}"))
        {
            conditional.Headers.IfNoneMatch.Add(new EntityTagHeaderValue(etag));
            (await api.SendAsync(conditional)).StatusCode.Should().Be(HttpStatusCode.NotModified);
        }

        // ── explicit override; the stage progress and the daily snapshot are untouched ──
        var progressBefore = (await Json(await api.GetAsync($"/api/v1/build/projects/{projectId}/progress"), HttpStatusCode.OK))
            .GetProperty("actualPercent").GetDecimal();
        progressBefore.Should().Be(100m);
        var recorded = await Json(await api.PostAsJsonAsync($"/api/v1/build/projects/{projectId}/twin/element-events", new
        {
            modelVersionId = versionId, externalIds = new[] { Gid("WallT2") }, state = "NeedsReview", note = "Fissura a verificar", effectiveDate = Day(-3),
        }), HttpStatusCode.Created);
        recorded.GetArrayLength().Should().Be(1);
        (await Code(await api.PostAsJsonAsync($"/api/v1/build/projects/{projectId}/twin/element-events", new
        {
            modelVersionId = versionId, externalIds = new[] { Gid("WallT2") }, state = "Completed", effectiveDate = Day(5),
        }), HttpStatusCode.UnprocessableEntity)).Should().Be("invalid_date");
        var now = await StateAt(Day(0));
        Ids(now, "NeedsReview").Should().Equal(Gid("WallT2"));
        Ids(now, "Completed").Should().BeEquivalentTo([Gid("WallT1"), Gid("WallT3")]);
        Ids(await StateAt(Day(-5)), "Completed").Should().Contain(Gid("WallT2"), "the override starts on its own date");
        (await Json(await api.GetAsync($"/api/v1/build/projects/{projectId}/progress"), HttpStatusCode.OK))
            .GetProperty("actualPercent").GetDecimal().Should().Be(progressBefore);
        (await Json(await api.GetAsync($"/api/v1/build/projects/{projectId}/progress/at?date={Day(0)}"), HttpStatusCode.OK))
            .GetProperty("actualPercent").GetDecimal().Should().Be(progressBefore);
        (await Json(await api.GetAsync($"/api/v1/build/models/versions/{versionId}/elements/{Gid("WallT2")}/events"), HttpStatusCode.OK))
            .EnumerateArray().Single().GetProperty("state").GetString().Should().Be("NeedsReview");

        // ── overview and 5D prep ──
        var overview = await Json(await api.GetAsync($"/api/v1/build/projects/{projectId}/twin"), HttpStatusCode.OK);
        overview.GetProperty("totalElements").GetInt32().Should().Be(7);
        overview.GetProperty("boundElements").GetInt32().Should().Be(3);
        overview.GetProperty("bindingCoveragePercent").GetDecimal().Should().Be(42.9m);
        overview.GetProperty("geometrySource").GetString().Should().Be("NativeBim");
        overview.GetProperty("stages").EnumerateArray().Single().GetProperty("boundElements").GetInt32().Should().Be(3);
        var cost = await Json(await api.GetAsync($"/api/v1/build/projects/{projectId}/twin/cost?date={Day(0)}"), HttpStatusCode.OK);
        var costStage = cost.GetProperty("stages").EnumerateArray().Single();
        costStage.GetProperty("boundElements").GetInt32().Should().Be(3);
        costStage.GetProperty("volume").GetDecimal().Should().Be(7.2m);
        costStage.GetProperty("area").GetDecimal().Should().Be(36m);
        cost.GetProperty("methodology").GetString().Should().Contain("Rateio por quantidade");

        // ── a manual zone with a simplified box (typed by the user) ──
        await Json(await api.PostAsJsonAsync($"/api/v1/build/projects/{projectId}/zones", new
        {
            kind = "Area", name = "Garagem (externa)", simplifiedBox = new { width = 3, depth = 5, height = 2.6 },
        }), HttpStatusCode.Created);

        // ── portal (model3d on): states, zones, no viewer yet, never the source ──
        // (v1.5: site captures are personal data, served only by a password portal → the client here has a session)
        var portalPassword = "Tw!" + Guid.NewGuid().ToString("N")[..12];
        var portal = await PortalAsync(admin, projectId, new { model3d = true, realityCapture = true }, portalPassword);
        var anonymous = host.CreateClient();
        anonymous.DefaultRequestHeaders.Add("X-Build-Portal-Session", (await Json(await host.CreateClient()
            .PostAsJsonAsync($"{portal}/session", new { password = portalPassword }), HttpStatusCode.OK)).GetProperty("token").GetString());
        (await Json(await anonymous.GetAsync(portal), HttpStatusCode.OK)).GetProperty("sections").EnumerateArray()
            .Select(s => s.GetString()).Should().Contain(["model3d", "realityCapture"]);
        var twinResponse = await anonymous.GetAsync($"{portal}/twin");
        var twinRaw = await twinResponse.Content.ReadAsStringAsync();
        twinResponse.StatusCode.Should().Be(HttpStatusCode.OK, twinRaw);
        twinRaw.Should().NotContain("/source.").And.NotContain(".ifc").And.NotContain("private/").And.NotContain(modelId.ToString());
        var twin = JsonDocument.Parse(twinRaw).RootElement;
        twin.GetProperty("geometrySource").GetString().Should().Be("NativeBim");
        twin.TryGetProperty("viewerUrl", out _).Should().BeFalse("no viewer derivative yet");
        twin.GetProperty("zones").GetArrayLength().Should().Be(6);
        twin.GetProperty("zones").EnumerateArray().Single(z => z.GetProperty("name").GetString() == "Garagem (externa)")
            .GetProperty("simplifiedBox").GetProperty("width").GetDecimal().Should().Be(3m);
        Ids(twin.GetProperty("state"), "NeedsReview").Should().Equal(Gid("WallT2"));
        twin.GetProperty("disclaimer").GetString().Should().NotBeNullOrWhiteSpace();
        twin.GetProperty("maxDate").GetString().Should().Be(Day(0));

        // v1.6 — the element index: stage from the real bindings, the IFC class, the zone; never from node names
        static string[] Group(JsonElement groups, string name) => groups.EnumerateArray()
            .Single(g => g.GetProperty("name").GetString() == name).GetProperty("elementIds").EnumerateArray().Select(i => i.GetString()!).ToArray();
        var index = twin.GetProperty("elementIndex");
        index.GetProperty("stages").GetArrayLength().Should().Be(1);
        Group(index.GetProperty("stages"), "Alvenaria").Should().BeEquivalentTo(groundWalls);
        index.GetProperty("stages")[0].GetProperty("order").GetInt32().Should().BeGreaterThanOrEqualTo(0);
        index.GetProperty("categories").EnumerateArray().Select(g => g.GetProperty("name").GetString()).Should().Equal("IfcSlab", "IfcWall");
        Group(index.GetProperty("categories"), "IfcWall").Should().HaveCount(5);
        index.GetProperty("zones").GetProperty(Gid("StoreyT")).EnumerateArray().Select(i => i.GetString())
            .Should().HaveCount(4).And.Contain(groundWalls);
        // stage names only where the portal already shows stages
        var portalSlug = portal.Split('/')[^1];
        await Json(await admin.PutAsJsonAsync($"/api/v1/build/projects/{projectId}/portal", new
        {
            slug = portalSlug, access = "Password", title = "Minha obra",
            visibility = new { model3d = true, realityCapture = true, stageProgress = false, schedule = false },
        }), HttpStatusCode.OK);
        var hidden = await Json(await anonymous.GetAsync($"{portal}/twin"), HttpStatusCode.OK);
        hidden.GetProperty("elementIndex").GetProperty("stages").GetArrayLength().Should().Be(0);
        hidden.GetProperty("elementIndex").GetProperty("categories").GetArrayLength().Should().Be(2);
        await Json(await admin.PutAsJsonAsync($"/api/v1/build/projects/{projectId}/portal", new
        {
            slug = portalSlug, access = "Password", title = "Minha obra", visibility = new { model3d = true, realityCapture = true },
        }), HttpStatusCode.OK);

        Ids(await Json(await anonymous.GetAsync($"{portal}/twin/state?date={Day(-15)}"), HttpStatusCode.OK), "InProgress")
            .Should().BeEquivalentTo(groundWalls);
        (await Code(await anonymous.GetAsync($"{portal}/twin/state?date={Day(1)}"), HttpStatusCode.BadRequest)).Should().Be("invalid_date");
        (await Code(await anonymous.GetAsync($"{portal}/twin/state?date=2025-12-31"), HttpStatusCode.BadRequest)).Should().Be("invalid_date");

        // ── viewer derivative converted in the browser: a wrong file is refused, the right one attached ──
        (await Code(await api.PutAsync($"/api/v1/build/models/versions/{versionId}/viewer", File(Glb(["X", "Y", "Z", Gid("WallT1")]), "outro.glb")),
            HttpStatusCode.UnprocessableEntity)).Should().Be("viewer_mismatch");
        (await Code(await api.PutAsync($"/api/v1/build/models/versions/{versionId}/viewer", File(Ifc(), "nao-e-glb.glb")),
            HttpStatusCode.UnprocessableEntity)).Should().Be("invalid_file_content");
        var allIds = new[] { "WallT1", "WallT2", "WallT3", "Wall11", "Wall12", "SlabT", "Slab1" }.Select(Gid);
        var withViewer = await Json(await api.PutAsync($"/api/v1/build/models/versions/{versionId}/viewer", File(Glb(allIds), "casa.glb")),
            HttpStatusCode.OK);
        withViewer.GetProperty("viewerSource").GetString().Should().Be("ConvertedBrowser");
        withViewer.GetProperty("hasViewer").GetBoolean().Should().BeTrue();
        (await Json(await api.GetAsync($"/api/v1/build/models/versions/{versionId}/viewer"), HttpStatusCode.OK))
            .GetProperty("viewerUrl").GetString().Should().Contain("/viewer.glb");

        twinRaw = await (await anonymous.GetAsync($"{portal}/twin")).Content.ReadAsStringAsync();
        twinRaw.Should().NotContain("/source.").And.NotContain(".ifc");
        twin = JsonDocument.Parse(twinRaw).RootElement;
        twin.GetProperty("viewerUrl").GetString().Should()
            .StartWith(InMemoryStorageProvider.Host + $"private/tenants/{tenant}/build/models/{modelId}/{versionId}/viewer.glb")
            .And.Contain("signature=").And.Contain("ttl=900");
        twin.GetProperty("viewerExpiresAt").GetDateTimeOffset().Should().BeCloseTo(DateTimeOffset.UtcNow.AddMinutes(15), TimeSpan.FromMinutes(1));

        // the platform switching 3D off hides the twin from the portal too, whatever the portal's own flag says
        await Json(await platform.PutAsJsonAsync($"/api/platform/tenants/{tenant}/build-settings", new { build3DEnabled = false }), HttpStatusCode.OK);
        (await Json(await anonymous.GetAsync(portal), HttpStatusCode.OK)).GetProperty("sections").EnumerateArray()
            .Select(s => s.GetString()).Should().NotContain("model3d").And.Contain("realityCapture");
        (await anonymous.GetAsync($"{portal}/twin")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        await Json(await platform.PutAsJsonAsync($"/api/platform/tenants/{tenant}/build-settings", new { build3DEnabled = true }), HttpStatusCode.OK);

        // ── reality capture: magic bytes decide; hidden from the client until marked visible ──
        (await Code(await api.PostAsync($"/api/v1/build/projects/{projectId}/reality-captures",
            File(Png, "video.mp4", ("type", "Video"), ("title", "Falso vídeo"))), HttpStatusCode.UnprocessableEntity)).Should().Be("invalid_file_content");
        var storedBefore = Storage.Objects.Keys.Count(k => k.Contains("/build/reality/", StringComparison.Ordinal));
        var capture = await Json(await api.PostAsync($"/api/v1/build/projects/{projectId}/reality-captures",
            File(Mp4(), "voo.mp4", ("type", "Video"), ("title", "Voo semanal"), ("stageId", alvenaria.ToString()), ("notes", "nota interna"))),
            HttpStatusCode.Created);
        capture.GetProperty("contentType").GetString().Should().Be("video/mp4");
        capture.GetProperty("url").GetString().Should().Contain($"private/tenants/{tenant}/build/reality/{projectId}/").And.Contain("signature=");
        capture.GetProperty("clientVisible").GetBoolean().Should().BeFalse();
        Storage.Objects.Keys.Count(k => k.Contains("/build/reality/", StringComparison.Ordinal)).Should().Be(storedBefore + 1);
        var captureId = capture.GetProperty("id").GetGuid();
        (await Json(await anonymous.GetAsync($"{portal}/reality-captures"), HttpStatusCode.OK)).GetProperty("total").GetInt32().Should().Be(0);
        await Json(await api.PutAsJsonAsync($"/api/v1/build/reality-captures/{captureId}/client-visibility", new { clientVisible = true }), HttpStatusCode.OK);
        var publicCaptures = await anonymous.GetAsync($"{portal}/reality-captures");
        var publicRaw = await publicCaptures.Content.ReadAsStringAsync();
        publicCaptures.StatusCode.Should().Be(HttpStatusCode.OK, publicRaw);
        publicRaw.Should().NotContain("nota interna");
        var item = JsonDocument.Parse(publicRaw).RootElement.GetProperty("items").EnumerateArray().Single();
        item.GetProperty("stageName").GetString().Should().Be("Alvenaria");
        item.GetProperty("url").GetString().Should().Contain("signature=");
        (await Json(await api.GetAsync($"/api/v1/build/projects/{projectId}/reality-captures?type=Video"), HttpStatusCode.OK)).GetArrayLength().Should().Be(1);

        // ── tenant isolation: another company sees none of it ──
        var (other, _) = await OtherTenantWithBuildAsync();
        var otherApi = As(host, other);
        foreach (var url in new[]
                 {
                     $"/api/v1/build/models/{modelId}", $"/api/v1/build/models/versions/{versionId}",
                     $"/api/v1/build/models/versions/{versionId}/elements", $"/api/v1/build/models/versions/{versionId}/zones",
                     $"/api/v1/build/models/versions/{versionId}/state?date={Day(0)}", $"/api/v1/build/models/versions/{versionId}/viewer",
                     $"/api/v1/build/reality-captures/{captureId}", $"/api/v1/build/projects/{projectId}/models",
                     $"/api/v1/build/projects/{projectId}/twin", $"/api/v1/build/projects/{projectId}/twin/bindings",
                     $"/api/v1/build/projects/{projectId}/reality-captures",
                 })
            (await otherApi.GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.NotFound, url);
        (await otherApi.PutAsync($"/api/v1/build/models/versions/{versionId}/viewer", File(Glb(allIds), "casa.glb")))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await otherApi.PostAsync($"/api/v1/build/models/{modelId}/versions", File(Ifc(), "x.ifc"))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await otherApi.PostAsJsonAsync($"/api/v1/build/projects/{projectId}/twin/element-events", new
        {
            modelVersionId = versionId, externalIds = new[] { Gid("WallT1") }, state = "Blocked",
        })).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await otherApi.PutAsJsonAsync($"/api/v1/build/reality-captures/{captureId}/client-visibility", new { clientVisible = false }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── Formats refused ───────────────────────────────────────────────────────

    [Fact]
    public async Task Rvt_and_dwg_are_rejected_without_storing_and_unknown_or_external_files_are_refused()
    {
        var admin = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var projectId = await ProjectAsync(admin, "Obra formatos");
        await using var host = StorageHost();
        var api = As(host, admin);
        var storedBefore = Storage.Objects.Count;

        var rvt = await Json(await api.PostAsync($"/api/v1/build/projects/{projectId}/models",
            File(Rvt, "projeto.rvt", ("name", "Revit"), ("discipline", "Estrutura"))), HttpStatusCode.UnprocessableEntity);
        rvt.GetProperty("code").GetString().Should().Be("model_format_rejected");
        rvt.GetProperty("error").GetString().Should().Contain("Exportar → IFC");
        rvt.GetProperty("version").GetProperty("status").GetString().Should().Be("Rejected");
        rvt.GetProperty("version").GetProperty("sourceFormat").GetString().Should().Be("Rvt");
        var modelId = rvt.GetProperty("model").GetProperty("id").GetGuid();

        (await Json(await api.PostAsync($"/api/v1/build/models/{modelId}/versions", File(Encoding.ASCII.GetBytes("AC1032\0\0\0\0\0\0"), "planta.dwg")),
            HttpStatusCode.UnprocessableEntity)).GetProperty("version").GetProperty("number").GetInt32().Should().Be(2);
        Storage.Objects.Count.Should().Be(storedBefore, "a rejected file is never stored");

        (await Code(await api.PostAsync($"/api/v1/build/projects/{projectId}/models", File(Encoding.ASCII.GetBytes("isto não é um modelo"), "x.ifc")),
            HttpStatusCode.UnprocessableEntity)).Should().Be("model_format_unknown");
        (await Code(await api.PostAsync($"/api/v1/build/projects/{projectId}/models", File([0x50, 0x4B, 0x03, 0x04, 0, 0, 0, 0], "casa.ifczip")),
            HttpStatusCode.UnprocessableEntity)).Should().Be("model_format_compressed");
        var externalGltf = Encoding.UTF8.GetBytes("""{"asset":{"version":"2.0"},"buffers":[{"uri":"https://example.test/geo.bin","byteLength":4}]}""");
        (await Code(await api.PostAsync($"/api/v1/build/projects/{projectId}/models", File(externalGltf, "casa.gltf")),
            HttpStatusCode.UnprocessableEntity)).Should().Be("gltf_external_uri");
        (await Code(await api.PostAsync($"/api/v1/build/projects/{projectId}/models", File(Ifc(), "casa.ifc", ("discipline", "Paisagismo"))),
            HttpStatusCode.UnprocessableEntity)).Should().Be("invalid_value");
        Storage.Objects.Count.Should().Be(storedBefore);

        var models = await Json(await api.GetAsync($"/api/v1/build/projects/{projectId}/models"), HttpStatusCode.OK);
        models.GetArrayLength().Should().Be(1, "only the RVT/DWG attempts are recorded");
        models[0].GetProperty("currentVersionId").ValueKind.Should().Be(JsonValueKind.Null);

        // a GLB is its own viewer: processed into elements, the portal may sign it
        var glb = await Json(await api.PostAsync($"/api/v1/build/projects/{projectId}/models", File(Glb(["IfcWall_a", "IfcSlab_b"]), "casa.glb")),
            HttpStatusCode.Created);
        var glbVersion = glb.GetProperty("version").GetProperty("id").GetGuid();
        glb.GetProperty("version").GetProperty("viewerSource").GetString().Should().Be("Uploaded");
        await DrainAsync(host);
        var v = await Json(await api.GetAsync($"/api/v1/build/models/versions/{glbVersion}"), HttpStatusCode.OK);
        v.GetProperty("status").GetString().Should().Be("Ready", v.ToString());
        v.GetProperty("geometrySource").GetString().Should().Be("Converted3D");
        v.GetProperty("elementCount").GetInt32().Should().Be(2);
    }

    // ── Build3D flag ──────────────────────────────────────────────────────────

    [Fact]
    public async Task With_build3d_off_uploads_are_refused_with_403_and_the_company_sees_the_flag()
    {
        var (company, tenantId) = await OtherTenantWithBuildAsync();
        var platform = await PlatformAuthorizationTests.PlatformClientAsync(_factory);
        var projectId = await ProjectAsync(company, "Obra sem 3D");

        var defaults = await Json(await platform.GetAsync($"/api/platform/tenants/{tenantId}/build-settings"), HttpStatusCode.OK);
        defaults.GetProperty("build3DEnabled").GetBoolean().Should().BeTrue();
        defaults.GetProperty("buildAiEnabled").GetBoolean().Should().BeFalse();
        defaults.GetProperty("buildVisionEnabled").GetBoolean().Should().BeFalse();
        defaults.GetProperty("buildPredictEnabled").GetBoolean().Should().BeTrue();
        defaults.GetProperty("customized").GetBoolean().Should().BeFalse();

        var off = await Json(await platform.PutAsJsonAsync($"/api/platform/tenants/{tenantId}/build-settings", new { build3DEnabled = false }),
            HttpStatusCode.OK);
        off.GetProperty("build3DEnabled").GetBoolean().Should().BeFalse();
        off.GetProperty("buildPredictEnabled").GetBoolean().Should().BeTrue("flags not sent keep their value");
        (await Json(await company.GetAsync("/api/v1/build/settings"), HttpStatusCode.OK)).GetProperty("build3DEnabled").GetBoolean().Should().BeFalse();

        await using var host = StorageHost();
        var api = As(host, company);
        (await Code(await api.PostAsync($"/api/v1/build/projects/{projectId}/models", File(Ifc(), "casa.ifc")), HttpStatusCode.Forbidden))
            .Should().Be(BuildTenantSettingsService.Code3DDisabled);
        // reads stay available
        (await Json(await api.GetAsync($"/api/v1/build/projects/{projectId}/twin"), HttpStatusCode.OK))
            .GetProperty("build3DEnabled").GetBoolean().Should().BeFalse();

        await Json(await platform.PutAsJsonAsync($"/api/platform/tenants/{tenantId}/build-settings", new { build3DEnabled = true }), HttpStatusCode.OK);
        await Json(await api.PostAsync($"/api/v1/build/projects/{projectId}/models", File(Ifc(), "casa.ifc")), HttpStatusCode.Created);
        await DrainAsync(host);

        using var scope = _factory.Services.CreateScope();
        var audits = await scope.ServiceProvider.GetRequiredService<NexoDbContext>().AuditRecords.IgnoreQueryFilters().AsNoTracking()
            .Where(a => a.ActionType == "build_tenant_settings_changed" && a.EntityId == tenantId.ToString()).ToListAsync();
        audits.Should().HaveCount(2);
        audits.Should().OnlyContain(a => a.ActorType == "platform");
    }

    [Fact]
    public async Task Platform_build_settings_require_a_platform_admin()
    {
        var admin = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var me = await Json(await admin.GetAsync("/api/auth/me"), HttpStatusCode.OK);
        var tenantId = Guid.Parse(me.GetProperty("tenantId").GetString()!);
        var url = $"/api/platform/tenants/{tenantId}/build-settings";

        (await admin.GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await admin.PutAsJsonAsync(url, new { build3DEnabled = false })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await _factory.CreateApiClient().GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var platform = await PlatformAuthorizationTests.PlatformClientAsync(_factory);
        (await platform.GetAsync($"/api/platform/tenants/{Guid.NewGuid()}/build-settings")).StatusCode.Should().Be(HttpStatusCode.OK, "an unknown company reads as defaults");
        (await platform.PutAsJsonAsync($"/api/platform/tenants/{Guid.NewGuid()}/build-settings", new { build3DEnabled = false }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await Json(await admin.GetAsync("/api/v1/build/settings"), HttpStatusCode.OK)).GetProperty("build3DEnabled").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Reality_capture_checks_the_magic_and_portal_sections_stay_off_by_default()
    {
        var admin = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var projectId = await ProjectAsync(admin, "Obra capturas");
        await using var host = StorageHost();
        var api = As(host, admin);

        (await Code(await api.PostAsync($"/api/v1/build/projects/{projectId}/reality-captures",
            File(Mp4(), "foto.jpg", ("type", "Photo"), ("title", "Foto"))), HttpStatusCode.UnprocessableEntity)).Should().Be("invalid_file_content");
        (await Code(await api.PostAsync($"/api/v1/build/projects/{projectId}/reality-captures",
            File(Mp4(), "voo.mp4", ("type", "Satelite"), ("title", "x"))), HttpStatusCode.UnprocessableEntity)).Should().Be("invalid_value");
        var cloud = await Json(await api.PostAsync($"/api/v1/build/projects/{projectId}/reality-captures",
            File(Encoding.ASCII.GetBytes("LASF" + new string('\0', 60)), "nuvem.las", ("type", "PointCloud"), ("title", "Nuvem"))), HttpStatusCode.Created);
        cloud.GetProperty("processed").GetBoolean().Should().BeFalse("point clouds are stored, not processed");
        (await Json(await api.PostAsync($"/api/v1/build/projects/{projectId}/reality-captures",
            File(Png, "drone.png", ("type", "Drone"), ("title", "Drone foto"))), HttpStatusCode.Created))
            .GetProperty("contentType").GetString().Should().Be("image/png");

        // storage off → 503 storage_unavailable
        (await Code(await admin.PostAsync($"/api/v1/build/projects/{projectId}/reality-captures",
            File(Mp4(), "voo.mp4", ("type", "Video"), ("title", "x"))), HttpStatusCode.ServiceUnavailable)).Should().Be("storage_unavailable");

        // portal sections model3d / realityCapture are off unless enabled
        var portal = await PortalAsync(admin, projectId, new { photos = true });
        var anonymous = host.CreateClient();
        (await Code(await anonymous.GetAsync($"{portal}/twin"), HttpStatusCode.NotFound)).Should().Be("portal_section_disabled");
        (await Code(await anonymous.GetAsync($"{portal}/twin/state"), HttpStatusCode.NotFound)).Should().Be("portal_section_disabled");
        (await Code(await anonymous.GetAsync($"{portal}/reality-captures"), HttpStatusCode.NotFound)).Should().Be("portal_section_disabled");
    }

    [Fact]
    public async Task Without_private_storage_private_uploads_fail_closed_and_public_branding_still_works()
    {
        var admin = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var platform = await PlatformAuthorizationTests.PlatformClientAsync(_factory);
        var projectId = await ProjectAsync(admin, "Obra sem bucket privado");
        var tenant = await TenantOfAsync(projectId);
        await Json(await platform.PutAsJsonAsync($"/api/platform/tenants/{tenant}/build-settings", new { build3DEnabled = true }),
            HttpStatusCode.OK);

        // the object store with no private bucket: "private/…" is refused, never put in the public bucket
        var storage = new InMemoryStorageProvider { RefusePrivate = true };
        await using var host = _factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("Integrations:Features:StorageEnabled", "true");
            b.ConfigureTestServices(s =>
            {
                s.RemoveAll<IStorageProvider>();
                s.AddSingleton<IStorageProvider>(storage);
            });
        });
        var api = As(host, admin);

        // original BIM, reality capture and site photo: 503 storage_unavailable
        (await Code(await api.PostAsync($"/api/v1/build/projects/{projectId}/models",
            File(Ifc(), "casa.ifc", ("name", "Arquitetura"), ("discipline", "Arquitetura"))), HttpStatusCode.ServiceUnavailable))
            .Should().Be("storage_unavailable");
        (await Code(await api.PostAsync($"/api/v1/build/projects/{projectId}/reality-captures",
            File(Png, "drone.png", ("type", "Drone"), ("title", "Drone"))), HttpStatusCode.ServiceUnavailable))
            .Should().Be("storage_unavailable");
        static MultipartFormDataContent Image(string context)
        {
            var form = new MultipartFormDataContent();
            var file = new ByteArrayContent(Png);
            file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            form.Add(file, "file", "foto.png");
            form.Add(new StringContent(context), "context");
            return form;
        }
        (await Code(await api.PostAsync("/api/integrations/storage/upload", Image("build-daily-log")), HttpStatusCode.ServiceUnavailable))
            .Should().Be("storage_unavailable");
        storage.Objects.Should().BeEmpty("nothing private was written anywhere");

        // public branding keeps its public URL
        var logo = await Json(await api.PostAsync("/api/integrations/storage/upload", Image("build-portal-logo")), HttpStatusCode.OK);
        logo.GetProperty("publicUrl").GetString().Should().StartWith(InMemoryStorageProvider.Host + $"tenants/{tenant}/build/portal/logo/");
        storage.Objects.Keys.Should().ContainSingle().Which.Should().NotStartWith(StorageKeys.PrivatePrefix);
    }

    // ── Model removal ─────────────────────────────────────────────────────────

    private async Task<(Guid ProjectId, Guid TenantId, WebApplicationFactory<Program> Host, HttpClient Api, HttpClient Admin)> ModelHostAsync(
        InMemoryStorageProvider storage, string projectName)
    {
        var admin = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var platform = await PlatformAuthorizationTests.PlatformClientAsync(_factory);
        var projectId = await ProjectAsync(admin, projectName);
        var tenant = await TenantOfAsync(projectId);
        await Json(await platform.PutAsJsonAsync($"/api/platform/tenants/{tenant}/build-settings", new { build3DEnabled = true }),
            HttpStatusCode.OK);
        var host = _factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("Integrations:Features:StorageEnabled", "true");
            b.ConfigureTestServices(s =>
            {
                s.RemoveAll<IStorageProvider>();
                s.AddSingleton<IStorageProvider>(storage);
            });
        });
        return (projectId, tenant, host, As(host, admin), admin);
    }

    private static async Task<(Guid Model, Guid Version)> UploadModelAsync(HttpClient api, Guid projectId, string name)
    {
        var upload = await Json(await api.PostAsync($"/api/v1/build/projects/{projectId}/models",
            File(Ifc(), "casa.ifc", ("name", name), ("discipline", "Arquitetura"))), HttpStatusCode.Created);
        return (upload.GetProperty("model").GetProperty("id").GetGuid(), upload.GetProperty("version").GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task A_model_is_removed_with_its_private_files_only_by_a_manager_of_its_own_company()
    {
        var storage = new InMemoryStorageProvider();
        var (projectId, tenant, host, api, admin) = await ModelHostAsync(storage, "Obra exclusão de modelo");
        await using var _h = host;

        var (modelId, versionId) = await UploadModelAsync(api, projectId, "Modelo descartável");
        var (keptId, keptVersionId) = await UploadModelAsync(api, projectId, "Modelo mantido");
        await DrainAsync(host);
        var ids = (await Json(await api.GetAsync($"/api/v1/build/models/versions/{versionId}/elements?take=50"), HttpStatusCode.OK))
            .GetProperty("items").EnumerateArray().Select(e => e.GetProperty("externalId").GetString()!).ToList();
        await Json(await api.PutAsync($"/api/v1/build/models/versions/{versionId}/viewer", File(Glb(ids), "casa.glb")), HttpStatusCode.OK);
        var stageId = await StageAsync(admin, projectId, "Alvenaria");
        await Json(await api.PostAsJsonAsync($"/api/v1/build/projects/{projectId}/twin/bindings/bulk", new
        {
            stageId, modelVersionId = versionId, externalIds = ids.Take(2).ToArray(),
        }), HttpStatusCode.OK);

        var source = BuildModelService.SourceKey(tenant, modelId, versionId, ".ifc");
        var viewer = BuildModelService.ViewerKey(tenant, modelId, versionId);
        var keptSource = BuildModelService.SourceKey(tenant, keptId, keptVersionId, ".ifc");
        storage.Objects.Keys.Should().Contain([source, viewer, keptSource]);

        // a seller is not a manager
        var tag = Guid.NewGuid().ToString("N")[..8];
        var password = "t" + Guid.NewGuid().ToString("N")[..12];
        await Json(await admin.PostAsJsonAsync("/api/users", new
        {
            fullName = "Vendedor Obra", email = $"md{tag}@teste.dev", login = $"md{tag}", password, role = "vendedor",
            phone = (string?)null, notes = (string?)null,
        }), HttpStatusCode.Created);
        var seller = As(host, await AuthClientFactory.LoginAsync(_factory, $"md{tag}", password));
        (await seller.DeleteAsync($"/api/v1/build/models/{modelId}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // another company never reaches it
        var (other, _) = await OtherTenantWithBuildAsync();
        (await As(host, other).DeleteAsync($"/api/v1/build/models/{modelId}")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        // the private bucket does not answer: 503, nothing removed — the call can be repeated
        storage.PrivateDown = true;
        (await Code(await api.DeleteAsync($"/api/v1/build/models/{modelId}"), HttpStatusCode.ServiceUnavailable)).Should().Be("storage_unavailable");
        storage.PrivateDown = false;
        await Json(await api.GetAsync($"/api/v1/build/models/{modelId}"), HttpStatusCode.OK);
        storage.Objects.Keys.Should().Contain([source, viewer, keptSource], "nothing was touched by the refused attempt");

        // a file already gone from the store still counts as removed
        storage.Objects.TryRemove(viewer, out _);
        (await api.DeleteAsync($"/api/v1/build/models/{modelId}")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        storage.Objects.Keys.Should().NotContain(source).And.NotContain(viewer).And.Contain(keptSource, "only this model's own files");
        (await api.GetAsync($"/api/v1/build/models/{modelId}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await api.GetAsync($"/api/v1/build/models/versions/{versionId}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await api.DeleteAsync($"/api/v1/build/models/{modelId}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        await Json(await api.GetAsync($"/api/v1/build/models/{keptId}"), HttpStatusCode.OK);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        (await db.BldModelVersions.IgnoreQueryFilters().AnyAsync(v => v.ModelId == modelId)).Should().BeFalse();
        (await db.BldModelElements.IgnoreQueryFilters().AnyAsync(e => e.ModelVersionId == versionId)).Should().BeFalse();
        (await db.BldModelZones.IgnoreQueryFilters().AnyAsync(z => z.ModelVersionId == versionId)).Should().BeFalse();
        (await db.BldModelJobs.IgnoreQueryFilters().AnyAsync(j => j.VersionId == versionId)).Should().BeFalse();
        (await db.BldModelBindings.IgnoreQueryFilters().AnyAsync(b => b.ModelVersionId == versionId)).Should().BeFalse();
        (await db.BldModelElements.IgnoreQueryFilters().AnyAsync(e => e.ModelVersionId == keptVersionId)).Should().BeTrue();
    }

    [Fact]
    public async Task A_model_still_processing_or_with_recorded_progress_is_not_removed()
    {
        var storage = new InMemoryStorageProvider();
        var (projectId, tenant, host, api, _) = await ModelHostAsync(storage, "Obra modelo com histórico");
        await using var _h = host;

        var (queuedId, queuedVersionId) = await UploadModelAsync(api, projectId, "Na fila");
        (await api.DeleteAsync($"/api/v1/build/models/{queuedId}")).StatusCode.Should().Be(HttpStatusCode.Conflict);
        storage.Objects.Keys.Should().Contain(BuildModelService.SourceKey(tenant, queuedId, queuedVersionId, ".ifc"));

        await DrainAsync(host);
        await Json(await api.PostAsJsonAsync($"/api/v1/build/projects/{projectId}/twin/element-events", new
        {
            modelVersionId = queuedVersionId, externalIds = new[] { Gid("WallT2") }, state = "NeedsReview", note = "Fissura", effectiveDate = Day(-1),
        }), HttpStatusCode.Created);
        (await api.DeleteAsync($"/api/v1/build/models/{queuedId}")).StatusCode.Should().Be(HttpStatusCode.Conflict);
        storage.Objects.Keys.Should().Contain(BuildModelService.SourceKey(tenant, queuedId, queuedVersionId, ".ifc"),
            "the execution history and its model stay");
    }

    [Fact]
    public async Task Abandoned_jobs_are_failed_oldest_first_in_a_stable_batch()
    {
        var storage = new InMemoryStorageProvider();
        var (projectId, tenant, host, api, _) = await ModelHostAsync(storage, "Obra jobs abandonados");
        await using var _h = host;
        var (_, versionId) = await UploadModelAsync(api, projectId, "Modelo");
        await DrainAsync(host);

        // 22 jobs whose worker died on the last attempt, each abandoned a minute apart (the newest two outside the batch of 20)
        var jobIds = new List<Guid>();
        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
            for (var i = 0; i < 22; i++)
            {
                var job = BuildModelProcessingJob.Enqueue(tenant, versionId, BuildModelJobKind.Convert);
                db.BldModelJobs.Add(job);
                jobIds.Add(job.Id);
            }
            await db.SaveChangesAsync();
            for (var i = 0; i < 22; i++)
                await db.Database.ExecuteSqlRawAsync(
                    "UPDATE nexo.bld_model_processing_jobs SET status = 2, attempts = {0}, locked_until = now() - make_interval(mins => {1}) WHERE id = {2}",
                    BuildModelProcessingJob.MaxAttempts, 100 - i, jobIds[i]);
        }

        using (var scope = host.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<IBuildModelProcessor>().ProcessNextAsync(CancellationToken.None);

        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
            var statuses = await db.BldModelJobs.IgnoreQueryFilters().Where(j => jobIds.Contains(j.Id))
                .ToDictionaryAsync(j => j.Id, j => j.Status);
            jobIds.Take(20).Select(id => statuses[id]).Should().AllBeEquivalentTo(BuildModelJobStatus.Failed, "the 20 longest-dead");
            jobIds.Skip(20).Select(id => statuses[id]).Should().AllBeEquivalentTo(BuildModelJobStatus.Running, "the newest wait for the next batch");
        }
    }
}
