using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nexo.Application.Integrations.Contracts;
using Nexo.Application.Integrations.DTOs;
using Nexo.Domain.Entities;
using Nexo.Infrastructure.Modules.Build;
using Nexo.Infrastructure.Persistence;
using Nexo.IntegrationTests.Common;
using Nexo.IntegrationTests.Helpers;

// Development/test only. The real API (controllers, services, CORS policy, migrations) on a disposable PostgreSQL
// (Testcontainers), behind a thin pass-through proxy because the test server does not listen on a port. Test data only:
// passwords are generated per run and never leave this machine.
public static class PortalReviewHost
{
    const string FrontendOrigin = "http://127.0.0.1:5195";
    static readonly ReviewStorage Storage = new();

    public static async Task Main(string[] args)
    {
        var root = Environment.GetEnvironmentVariable("BUILD_BACKEND_ROOT") ?? throw new InvalidOperationException("BUILD_BACKEND_ROOT required");
        Environment.SetEnvironmentVariable("ASPNETCORE_TEST_CONTENTROOT_NEXO_API", Path.Combine(root, "src", "Nexo.Api"));
        await using var factory = new ReviewFactory(root);
        await factory.InitializeAsync();
        using var admin = await AuthClientFactory.LoginAsAdminAsync(factory);
        using var api = factory.CreateApiClient();

        var seed = new Seed(factory, admin);
        var setup = await seed.RunAsync();

        var builder = WebApplication.CreateBuilder(args);
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:5194");
        var app = builder.Build();

        // signed media: a revoked signature answers 403, like an expired S3 link
        app.MapGet("/__test/media/{**key}", (string key, string? signature) =>
            signature is null || Storage.IsRevoked(signature) ? Results.StatusCode(403)
            : Storage.Inner.Objects.TryGetValue(key, out var value) ? Results.File(value.Bytes, value.ContentType)
            : Results.NotFound());
        app.MapPost("/__test/media/expire", () => { Storage.RevokeIssued(); return Results.Ok(); });
        app.MapGet("/__test/setup", () => setup);
        app.MapPost("/__test/sessions/revoke", async () =>
        {
            await WithDb(factory, db => db.BldPortalSessions.IgnoreQueryFilters()
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, DateTime.UtcNow)));
            return Results.Ok();
        });
        app.MapPost("/__test/sessions/expire", async () =>
        {
            await WithDb(factory, db => db.BldPortalSessions.IgnoreQueryFilters()
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.ExpiresAt, DateTime.UtcNow.AddMinutes(-1))));
            return Results.Ok();
        });
        app.MapPost("/__test/assistance/resolve", seed.ResolveAssistanceAsync);
        app.MapPost("/__test/selection/confirm", seed.ConfirmSelectionAsync);

        // pass-through to the real API: method, headers (Origin, preflight headers, session, If-None-Match) and body as
        // sent by the browser; the response — CORS headers included — exactly as the API answered
        app.Map("/api/public/build/{**path}", async ctx =>
        {
            using var request = new HttpRequestMessage(new HttpMethod(ctx.Request.Method), ctx.Request.Path + ctx.Request.QueryString);
            if (ctx.Request.ContentLength > 0 || ctx.Request.Headers.TransferEncoding.Count > 0)
                request.Content = new StreamContent(ctx.Request.Body);
            foreach (var (name, value) in ctx.Request.Headers)
            {
                if (name is "Host" or "Connection" or "Content-Length" or "Transfer-Encoding") continue;
                if (!request.Headers.TryAddWithoutValidation(name, value.ToArray()))
                    request.Content?.Headers.TryAddWithoutValidation(name, value.ToArray());
            }
            using var response = await api.SendAsync(request, ctx.RequestAborted);
            ctx.Response.StatusCode = (int)response.StatusCode;
            foreach (var h in response.Headers.Concat(response.Content.Headers))
                ctx.Response.Headers[h.Key] = h.Value.ToArray();
            ctx.Response.Headers.Remove("transfer-encoding");
            await response.Content.CopyToAsync(ctx.Response.Body);
        });

        Console.WriteLine("Portal integration host ready on 127.0.0.1:5194 (test data only)");
        await app.RunAsync();
    }

    static async Task WithDb(ReviewFactory factory, Func<NexoDbContext, Task> work)
    {
        using var scope = factory.Services.CreateScope();
        await work(scope.ServiceProvider.GetRequiredService<NexoDbContext>());
    }

    internal static async Task<JsonElement> Read(HttpResponseMessage response)
    {
        var content = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"{response.RequestMessage?.Method} {response.RequestMessage?.RequestUri}: {(int)response.StatusCode} {content}");
        return string.IsNullOrWhiteSpace(content) ? default : JsonDocument.Parse(content).RootElement.Clone();
    }

    /// <summary>Everything the portal suite exercises, created through the company's own API.</summary>
    sealed class Seed(ReviewFactory factory, HttpClient admin)
    {
        const string Company = "review-construtora";
        const string OtherCompany = "review-outra";
        readonly string _password = "Rv-" + Guid.NewGuid().ToString("N");
        readonly string _otherPassword = "Rv-" + Guid.NewGuid().ToString("N");
        readonly Dictionary<string, Guid> _ids = [];
        Guid _selection;

        static readonly object Visibility = new
        {
            overallProgress = true, stageProgress = true, schedule = true, forecastDelivery = true, clientUpdates = true,
            dailyLogs = true, photos = true, financialSummary = false, postConstruction = true, changeOrders = true,
            selections = true, documents = true, model3d = true, realityCapture = true,
        };

        public async Task<object> RunAsync()
        {
            await Read(await admin.PutAsJsonAsync("/api/v1/build/portal/company",
                new { slug = Company, displayName = "Construtora · ambiente de teste" }));
            var tenant = await TenantOfAdminAsync();

            // public: progress, timeline, a published photo; finance hidden
            var publica = await ProjectAsync("obra-publica", "Residência · teste local");
            await DiaryPhotoAsync(tenant, publica.Id, "Evidência sintética de teste");
            await PortalAsync(admin, publica.Id, "obra-publica", null);

            // private: decisions, documents, photos
            var privada = await ProjectAsync("obra-privada", "Residência · teste local");
            await DiaryPhotoAsync(tenant, privada.Id, "Evidência sintética de teste");
            await ChangeOrderAsync(privada.Id, privada.Stage, "Ajuste de revestimento", 200m, 2);
            await ChangeOrderAsync(privada.Id, privada.Stage, "Troca de louças", 350m, 0);
            var selection = await Read(await admin.PostAsJsonAsync($"/api/v1/build/projects/{privada.Id}/selections",
                new { title = "Piso da sala", clientVisible = true }));
            _selection = selection.GetProperty("id").GetGuid();
            await Read(await admin.PostAsJsonAsync($"/api/v1/build/selections/{_selection}/options",
                new { name = "Cerâmica padrão", priceDelta = 0m, isStandard = true }));
            await Read(await admin.PostAsJsonAsync($"/api/v1/build/selections/{_selection}/options",
                new { name = "Porcelanato 90x90", priceDelta = 2500m, leadTimeDays = 15, isStandard = false }));
            using (var files = Uploads())
                await Read(await files.PostAsync($"/api/v1/build/projects/{privada.Id}/documents",
                    File(Pdf(), "manual.pdf", ("category", "Manual"), ("clientVisible", "true"))));
            await PortalAsync(admin, privada.Id, "obra-privada", _password);

            // delivered property: handover, warranty, assistance
            var entregue = await ProjectAsync("imovel-entregue", "Meu imóvel · teste local");
            await Read(await admin.PutAsJsonAsync($"/api/v1/build/projects/{entregue.Id}/handover",
                new { scheduledFor = DateTime.UtcNow.ToString("yyyy-MM-dd") }));
            await Read(await admin.PostAsync($"/api/v1/build/projects/{entregue.Id}/handover/deliver", null));
            await PortalAsync(admin, entregue.Id, "imovel-entregue", _password);

            // digital twin: a real GLB, stages bound by IFC class, progress on different dates, a site capture
            var twin = await ProjectAsync("obra-3d", "Edifício · teste local", stageName: "Fundação");
            var walls = await StageAsync(twin.Id, "Alvenaria", "2026-12-15");
            await Read(await admin.PutAsJsonAsync($"/api/v1/build/stages/{twin.Stage}/progress",
                new { progressPercent = 100, effectiveDate = "2026-02-10" }));
            await Read(await admin.PutAsJsonAsync($"/api/v1/build/stages/{walls}/progress",
                new { progressPercent = 60, effectiveDate = "2026-05-04" }));
            Guid versionId;
            using (var files = Uploads())
            {
                var upload = await Read(await files.PostAsync($"/api/v1/build/projects/{twin.Id}/models",
                    File(Glb.House(), "edificio.glb", ("name", "Arquitetura"), ("discipline", "Arquitetura"))));
                versionId = upload.GetProperty("version").GetProperty("id").GetGuid();
            }
            for (var i = 0; i < 20; i++)
            {
                using var scope = factory.Services.CreateScope();
                if (!await scope.ServiceProvider.GetRequiredService<IBuildModelProcessor>().ProcessNextAsync(CancellationToken.None)) break;
            }
            await Read(await admin.PostAsJsonAsync($"/api/v1/build/projects/{twin.Id}/twin/bindings/bulk",
                new { stageId = twin.Stage, modelVersionId = versionId, category = "IfcSlab" }));
            await Read(await admin.PostAsJsonAsync($"/api/v1/build/projects/{twin.Id}/twin/bindings/bulk",
                new { stageId = walls, modelVersionId = versionId, category = "IfcWall" }));
            using (var files = Uploads())
            {
                var capture = await Read(await files.PostAsync($"/api/v1/build/projects/{twin.Id}/reality-captures",
                    File(Png(), "fachada.png", ("type", "Photo"), ("title", "Fachada registrada"), ("stageId", walls.ToString()))));
                await Read(await admin.PutAsJsonAsync($"/api/v1/build/reality-captures/{capture.GetProperty("id").GetGuid()}/client-visibility",
                    new { clientVisible = true }));
            }
            await PortalAsync(admin, twin.Id, "obra-3d", _password);

            // dedicated to the password lockout, so the lock never blocks the other scenarios
            var bloqueio = await ProjectAsync("obra-bloqueio", "Residência · teste local");
            await PortalAsync(admin, bloqueio.Id, "obra-bloqueio", _password);

            // another company (tenant) with the same project slug, its own password and a change order
            using var other = await OtherCompanyAsync();
            await Read(await other.PutAsJsonAsync("/api/v1/build/portal/company", new { slug = OtherCompany, displayName = "Outra construtora" }));
            var otherProject = await Read(await other.PostAsJsonAsync("/api/v1/build/projects", new
            {
                name = "Obra de outra empresa", clientName = "Outro cliente", type = "House", startDate = "2026-01-05", expectedEndDate = "2026-12-20",
            }));
            var otherId = otherProject.GetProperty("id").GetGuid();
            var otherOrder = await Read(await other.PostAsJsonAsync($"/api/v1/build/projects/{otherId}/change-orders",
                new { title = "Aditivo de outra empresa", costImpact = 10m, scheduleImpactDays = 0 }));
            await Read(await other.PostAsync($"/api/v1/build/change-orders/{otherOrder.GetProperty("id").GetGuid()}/send", null));
            await PortalAsync(other, otherId, "obra-privada", _otherPassword);

            return new
            {
                company = Company, otherCompany = OtherCompany, password = _password, otherPassword = _otherPassword,
                otherChangeOrder = otherOrder.GetProperty("id").GetGuid(),
            };
        }

        async Task<Guid> TenantOfAdminAsync()
        {
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
            return await db.Users.IgnoreQueryFilters().Where(u => u.Login == TestCredentials.AdminLogin).Select(u => u.TenantId).SingleAsync();
        }

        async Task<HttpClient> OtherCompanyAsync()
        {
            var email = $"review-{Guid.NewGuid():N}"[..19] + "@" + TestCredentials.TestDomain;
            await Read(await factory.CreateApiClient().PostAsJsonAsync("/api/auth/register",
                new { name = "Outra construtora " + Guid.NewGuid().ToString("N")[..6], email, password = TestCredentials.AdminPassword }));
            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
                var user = await db.Users.IgnoreQueryFilters().SingleAsync(u => u.Login == email);
                user.Activate();
                db.ModuleSubscriptions.Add(ModuleSubscription.CreateAdminGrant(user.TenantId, "build"));
                await db.SaveChangesAsync();
            }
            return await AuthClientFactory.LoginAsync(factory, email, TestCredentials.AdminPassword);
        }

        async Task<(Guid Id, Guid Stage)> ProjectAsync(string slug, string name, string stageName = "Fundação")
        {
            var project = await Read(await admin.PostAsJsonAsync("/api/v1/build/projects", new
            {
                name, clientName = "Cliente de teste", type = "House", startDate = "2026-01-05", expectedEndDate = "2026-12-20",
            }));
            var id = project.GetProperty("id").GetGuid();
            _ids[slug] = id;
            // the twin project gets its own dated history (seeded by the caller); the others are at 40% today
            var stage = await StageAsync(id, stageName, slug == "obra-3d" ? "2026-12-15" : "2026-09-30");
            if (slug != "obra-3d")
                await Read(await admin.PutAsJsonAsync($"/api/v1/build/stages/{stage}/progress", new { progressPercent = 40m }));
            var update = await Read(await admin.PostAsJsonAsync($"/api/v1/build/projects/{id}/client-updates", new
            {
                title = "Fundação em andamento", body = "Registro publicado para a revisão local. Dados de teste.", kind = "WeeklyDigest",
            }));
            await Read(await admin.PostAsync($"/api/v1/build/client-updates/{update.GetProperty("id").GetGuid()}/publish", null));
            if (slug != "obra-3d")
                await Read(await admin.PostAsJsonAsync($"/api/v1/build/projects/{id}/zones", new
                {
                    kind = "Area", name = "Sala · medidas de teste", simplifiedBox = new { width = 3, depth = 4, height = 2.8, elevation = 0 },
                }));
            return (id, stage);
        }

        async Task<Guid> StageAsync(Guid projectId, string name, string plannedEnd = "2026-09-30")
            => (await Read(await admin.PostAsJsonAsync($"/api/v1/build/projects/{projectId}/stages",
                new { name, plannedStart = "2026-01-05", plannedEnd, weight = 1m }))).GetProperty("id").GetGuid();

        async Task ChangeOrderAsync(Guid projectId, Guid stageId, string title, decimal cost, int days)
        {
            var order = await Read(await admin.PostAsJsonAsync($"/api/v1/build/projects/{projectId}/change-orders",
                new { title, costImpact = cost, scheduleImpactDays = days, stageId }));
            await Read(await admin.PostAsync($"/api/v1/build/change-orders/{order.GetProperty("id").GetGuid()}/send", null));
        }

        async Task DiaryPhotoAsync(Guid tenant, Guid projectId, string caption)
        {
            var log = await Read(await admin.PostAsJsonAsync($"/api/v1/build/projects/{projectId}/daily-logs",
                new { date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)), notes = "Registro interno de teste." }));
            var logId = log.GetProperty("id").GetGuid();
            await Read(await admin.PutAsJsonAsync($"/api/v1/build/daily-logs/{logId}/client-visibility",
                new { clientVisible = true, clientSummary = "Concretagem registrada no ambiente de teste." }));
            var key = $"private/tenants/{tenant}/build/daily-logs/{Guid.NewGuid():N}.png";
            var png = Png();
            await Storage.Inner.UploadAsync(new StorageUploadRequest(new MemoryStream(png), "foto.png", "image/png", key, png.Length));
            var withPhoto = await Read(await admin.PostAsJsonAsync($"/api/v1/build/daily-logs/{logId}/photos", new { storageKey = key, caption }));
            var photoId = withPhoto.GetProperty("photos").EnumerateArray().Single(p => p.GetProperty("caption").GetString() == caption)
                .GetProperty("id").GetGuid();
            await Read(await admin.PutAsJsonAsync($"/api/v1/build/daily-log-photos/{photoId}/client-visibility", new { clientVisible = true }));
        }

        static async Task PortalAsync(HttpClient c, Guid projectId, string slug, string? password)
        {
            await Read(await c.PutAsJsonAsync($"/api/v1/build/projects/{projectId}/portal", new
            {
                slug, access = "Public", title = "Acompanhe sua obra", welcomeMessage = "Registros de teste, servidos pelo backend local.",
                visibility = Visibility,
            }));
            if (password is not null)
            {
                await Read(await c.PutAsJsonAsync($"/api/v1/build/projects/{projectId}/portal/password", new { password }));
                await Read(await c.PutAsJsonAsync($"/api/v1/build/projects/{projectId}/portal",
                    new { slug, access = "Password", title = "Acompanhe sua obra", visibility = Visibility }));
            }
            await Read(await c.PostAsync($"/api/v1/build/projects/{projectId}/portal/publish", null));
        }

        HttpClient Uploads()
        {
            var c = factory.CreateClient();
            c.DefaultRequestHeaders.Authorization = admin.DefaultRequestHeaders.Authorization;
            return c;
        }

        /// <summary>The company works the delivered property's tickets through to Resolved.</summary>
        public async Task<IResult> ResolveAssistanceAsync()
        {
            var list = await Read(await admin.GetAsync($"/api/v1/build/projects/{_ids["imovel-entregue"]}/assistance"));
            foreach (var item in list.EnumerateArray().Where(t => t.GetProperty("status").GetString() is not ("Resolved" or "Closed")))
            {
                var ticket = item.GetProperty("id").GetGuid();
                await Read(await admin.PostAsync($"/api/v1/build/assistance/{ticket}/triage", null));
                await Read(await admin.PostAsJsonAsync($"/api/v1/build/assistance/{ticket}/schedule", new { scheduledFor = DateTimeOffset.UtcNow.AddDays(1) }));
                await Read(await admin.PostAsync($"/api/v1/build/assistance/{ticket}/start", null));
                await Read(await admin.PostAsJsonAsync($"/api/v1/build/assistance/{ticket}/resolve", new { resolutionNotes = "Ajuste concluído no ambiente de teste." }));
            }
            return Results.Ok();
        }

        /// <summary>The company confirms the client's choice; a price or lead-time change becomes a change order it sends.</summary>
        public async Task<IResult> ConfirmSelectionAsync()
        {
            var confirmed = await Read(await admin.PostAsync($"/api/v1/build/selections/{_selection}/confirm", null));
            if (confirmed.TryGetProperty("changeOrderId", out var co) && co.ValueKind == JsonValueKind.String)
                await Read(await admin.PostAsync($"/api/v1/build/change-orders/{co.GetGuid()}/send", null));
            return Results.Ok(new { changeOrder = co.ValueKind == JsonValueKind.String });
        }
    }

    static MultipartFormDataContent File(byte[] bytes, string fileName, params (string Name, string Value)[] fields)
    {
        var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        content.Add(file, "file", fileName);
        foreach (var (name, value) in fields) content.Add(new StringContent(value), name);
        return content;
    }

    static byte[] Pdf() => Encoding.ASCII.GetBytes("%PDF-1.4\n% manual de teste\n1 0 obj << >> endobj\ntrailer << >>\n%%EOF");

    /// <summary>A real 1×1 PNG (so the browser decodes it).</summary>
    static byte[] Png() => Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aN1sAAAAASUVORK5CYII=");

    /// <summary>A small building with real geometry: storeys as groups, slabs and walls as meshes named by id.</summary>
    static class Glb
    {
        record Part(string Id, string Category, float[] Scale, float[] At);

        public static byte[] House()
        {
            var groups = new (string Name, float Y, Part[] Parts)[]
            {
                ("Pavimento térreo", 0, [
                    new("LAJE-T01", "IfcSlab", [6, 0.2f, 4], [0, 0, 0]),
                    new("PAREDE-T01", "IfcWall", [6, 2.8f, 0.2f], [0, 1.5f, -1.9f]),
                    new("PAREDE-T02", "IfcWall", [0.2f, 2.8f, 4], [-2.9f, 1.5f, 0]),
                    new("PAREDE-T03", "IfcWall", [0.2f, 2.8f, 4], [2.9f, 1.5f, 0]),
                ]),
                ("Pavimento superior", 3, [
                    new("LAJE-S01", "IfcSlab", [6, 0.2f, 4], [0, 0, 0]),
                    new("PAREDE-S01", "IfcWall", [6, 2.8f, 0.2f], [0, 1.5f, -1.9f]),
                ]),
            };
            var nodes = new List<object>();
            var childIndex = groups.Length;
            foreach (var g in groups)
            {
                nodes.Add(new { name = g.Name, translation = new[] { 0f, g.Y, 0f }, children = Enumerable.Range(childIndex, g.Parts.Length).ToArray() });
                childIndex += g.Parts.Length;
            }
            foreach (var p in groups.SelectMany(g => g.Parts))
                nodes.Add(new { name = p.Id, mesh = 0, scale = p.Scale, translation = p.At, extras = new { category = p.Category } });

            var (positions, normals, indices) = Cube();
            var bin = new MemoryStream();
            var w = new BinaryWriter(bin);
            foreach (var f in positions) w.Write(f);
            foreach (var f in normals) w.Write(f);
            foreach (var i in indices) w.Write(i);
            while (bin.Length % 4 != 0) w.Write((byte)0);
            w.Flush();
            var json = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
            {
                asset = new { version = "2.0" },
                scene = 0,
                scenes = new[] { new { nodes = Enumerable.Range(0, groups.Length).ToArray() } },
                nodes,
                materials = new[] { new { pbrMetallicRoughness = new { baseColorFactor = new[] { 0.82f, 0.79f, 0.74f, 1f }, metallicFactor = 0f, roughnessFactor = 0.9f } } },
                meshes = new[] { new { primitives = new[] { new { attributes = new { POSITION = 0, NORMAL = 1 }, indices = 2, material = 0 } } } },
                accessors = new object[]
                {
                    new { bufferView = 0, componentType = 5126, count = 24, type = "VEC3", min = new[] { -0.5f, -0.5f, -0.5f }, max = new[] { 0.5f, 0.5f, 0.5f } },
                    new { bufferView = 1, componentType = 5126, count = 24, type = "VEC3" },
                    new { bufferView = 2, componentType = 5123, count = 36, type = "SCALAR" },
                },
                bufferViews = new object[]
                {
                    new { buffer = 0, byteOffset = 0, byteLength = 288 },
                    new { buffer = 0, byteOffset = 288, byteLength = 288 },
                    new { buffer = 0, byteOffset = 576, byteLength = 72 },
                },
                buffers = new[] { new { byteLength = (int)bin.Length } },
            }));
            var padded = new byte[(json.Length + 3) / 4 * 4];
            Array.Fill(padded, (byte)0x20);
            json.CopyTo(padded, 0);
            var body = bin.ToArray();
            using var ms = new MemoryStream();
            using var o = new BinaryWriter(ms);
            o.Write(0x46546C67u); o.Write(2u); o.Write((uint)(12 + 8 + padded.Length + 8 + body.Length));
            o.Write((uint)padded.Length); o.Write(0x4E4F534Au); o.Write(padded);
            o.Write((uint)body.Length); o.Write(0x004E4942u); o.Write(body);
            o.Flush();
            return ms.ToArray();
        }

        static (float[] Positions, float[] Normals, ushort[] Indices) Cube()
        {
            var faces = new (float[] N, float[][] V)[]
            {
                ([1, 0, 0], [[.5f, -.5f, -.5f], [.5f, .5f, -.5f], [.5f, .5f, .5f], [.5f, -.5f, .5f]]),
                ([-1, 0, 0], [[-.5f, -.5f, .5f], [-.5f, .5f, .5f], [-.5f, .5f, -.5f], [-.5f, -.5f, -.5f]]),
                ([0, 1, 0], [[-.5f, .5f, -.5f], [-.5f, .5f, .5f], [.5f, .5f, .5f], [.5f, .5f, -.5f]]),
                ([0, -1, 0], [[-.5f, -.5f, .5f], [-.5f, -.5f, -.5f], [.5f, -.5f, -.5f], [.5f, -.5f, .5f]]),
                ([0, 0, 1], [[-.5f, -.5f, .5f], [.5f, -.5f, .5f], [.5f, .5f, .5f], [-.5f, .5f, .5f]]),
                ([0, 0, -1], [[.5f, -.5f, -.5f], [-.5f, -.5f, -.5f], [-.5f, .5f, -.5f], [.5f, .5f, -.5f]]),
            };
            var positions = faces.SelectMany(f => f.V.SelectMany(v => v)).ToArray();
            var normals = faces.SelectMany(f => Enumerable.Repeat(f.N, 4).SelectMany(n => n)).ToArray();
            var indices = Enumerable.Range(0, 6).SelectMany(f => new[] { 0, 1, 2, 0, 2, 3 }.Select(i => (ushort)(f * 4 + i))).ToArray();
            return (positions, normals, indices);
        }
    }

    sealed class ReviewStorage : IStorageProvider
    {
        long _sequence;
        long _revokedUpTo;
        public InMemoryStorageProvider Inner { get; } = new();
        public Task<StorageUploadResult> UploadAsync(StorageUploadRequest r, CancellationToken ct = default) => Inner.UploadAsync(r, ct);
        public Task DeleteAsync(string key, CancellationToken ct = default) => Inner.DeleteAsync(key, ct);
        public Task PingAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task<Stream> OpenReadAsync(string key, CancellationToken ct = default) => Inner.OpenReadAsync(key, ct);
        public string GetSignedUrl(string key, TimeSpan ttl)
            => $"https://storage.test/{key}?signature=review-{Interlocked.Increment(ref _sequence)}&ttl={(int)ttl.TotalSeconds}";
        /// <summary>Every signature issued so far stops working (as if their 15 minutes had passed).</summary>
        public void RevokeIssued() => Interlocked.Exchange(ref _revokedUpTo, Interlocked.Read(ref _sequence));
        public bool IsRevoked(string signature)
            => !signature.StartsWith("review-", StringComparison.Ordinal)
               || !long.TryParse(signature["review-".Length..], out var n)
               || n <= Interlocked.Read(ref _revokedUpTo);
    }

    sealed class ReviewFactory(string root) : TestWebApplicationFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseContentRoot(Path.Combine(root, "src", "Nexo.Api"));
            builder.UseSetting("Logging:LogLevel:Default", "Warning");
            builder.UseSetting("Integrations:Features:StorageEnabled", "true");
            // the frontend under test is a separate origin: the API's own CORS policy must let it read the portal
            builder.UseSetting("Cors:AllowedOrigins:0", FrontendOrigin);
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IStorageProvider>();
                services.AddSingleton<IStorageProvider>(Storage);
            });
        }
    }
}
