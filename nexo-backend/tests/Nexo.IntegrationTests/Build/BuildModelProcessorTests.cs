using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nexo.Application.Integrations.Contracts;
using Nexo.Domain.Modules.Build;
using Nexo.Infrastructure.Modules.Build;
using Nexo.Infrastructure.Persistence;
using Nexo.IntegrationTests.Common;
using Nexo.IntegrationTests.Helpers;

namespace Nexo.IntegrationTests.Build;

/// <summary>
/// A fila de processamento de modelos, pelo próprio processador (sem worker em Testing): leitura de GLB/glTF/DXF, arquivos
/// recusados de imediato com mensagem legível, falhas inesperadas repetidas até 3 tentativas, limite de elementos e a
/// conversão IFC → GLB no servidor por um IfcConvert de mentira (script que copia, falha ou não gera nada).
/// </summary>
[Collection("Integration")]
public class BuildModelProcessorTests
{
    private readonly TestWebApplicationFactory _factory;

    public BuildModelProcessorTests(TestWebApplicationFactory factory) => _factory = factory;

    // ── Helpers ───────────────────────────────────────────────────────────────

    private sealed record Seeded(Guid ModelId, Guid VersionId, Guid JobId, string SourceKey);

    private async Task<(Guid ProjectId, Guid TenantId)> ProjectAsync(string name)
    {
        var admin = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var r = await admin.PostAsJsonAsync("/api/v1/build/projects", new
        {
            name, clientName = "Cliente Fila", type = "House", startDate = "2026-01-05", expectedEndDate = "2026-12-20",
        });
        var body = await r.Content.ReadAsStringAsync();
        r.StatusCode.Should().Be(HttpStatusCode.Created, body);
        var projectId = JsonDocument.Parse(body).RootElement.GetProperty("id").GetGuid();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        var tenant = await db.BldProjects.IgnoreQueryFilters().Where(p => p.Id == projectId).Select(p => p.TenantId).SingleAsync();
        return (projectId, tenant);
    }

    private WebApplicationFactory<Program> Host(InMemoryStorageProvider storage, params (string Key, string Value)[] settings)
        => _factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("Integrations:Features:StorageEnabled", "true");
            foreach (var (key, value) in settings) b.UseSetting(key, value);
            b.ConfigureTestServices(s =>
            {
                s.RemoveAll<IStorageProvider>();
                s.AddSingleton<IStorageProvider>(storage);
            });
        });

    /// <summary>
    /// A stored source file and its version, as the upload leaves them, with a job of the given kind in the queue. The queue is
    /// drained first, so the next job claimed is this one (the queue is shared by every test of the collection).
    /// </summary>
    private static async Task<Seeded> SeedAsync(WebApplicationFactory<Program> host, InMemoryStorageProvider storage, Guid tenant,
        Guid projectId, BuildModelSourceFormat format, byte[] bytes, string extension, BuildModelJobKind kind = BuildModelJobKind.Parse)
    {
        await DrainAsync(host);
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        var model = BuildModel.Create(tenant, projectId, "Modelo " + extension, BuildModelDiscipline.Arquitetura, Guid.NewGuid());
        var versionId = Guid.NewGuid();
        var key = BuildModelService.SourceKey(tenant, model.Id, versionId, extension);
        storage.Objects[key] = (bytes, "application/octet-stream");
        var version = BuildModelVersion.Stored(tenant, projectId, model.Id, versionId, 1, format, key, "arquivo" + extension,
            bytes.Length, new string('a', 64), Guid.NewGuid());
        var job = BuildModelProcessingJob.Enqueue(tenant, versionId, kind);
        db.BldModels.Add(model);
        db.BldModelVersions.Add(version);
        await db.SaveChangesAsync();
        db.BldModelJobs.Add(job);
        await db.SaveChangesAsync();
        return new Seeded(model.Id, versionId, job.Id, key);
    }

    private static async Task<bool> ProcessOnceAsync(WebApplicationFactory<Program> host)
    {
        using var scope = host.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IBuildModelProcessor>().ProcessNextAsync(CancellationToken.None);
    }

    private static async Task DrainAsync(WebApplicationFactory<Program> host)
    {
        for (var i = 0; i < 50 && await ProcessOnceAsync(host); i++) { }
    }

    private static async Task<T> ReadAsync<T>(WebApplicationFactory<Program> host, Func<NexoDbContext, Task<T>> read)
    {
        using var scope = host.Services.CreateScope();
        return await read(scope.ServiceProvider.GetRequiredService<NexoDbContext>());
    }

    private static Task<BuildModelVersion> VersionAsync(WebApplicationFactory<Program> host, Guid id)
        => ReadAsync(host, db => db.BldModelVersions.IgnoreQueryFilters().AsNoTracking().SingleAsync(v => v.Id == id));

    private static Task<BuildModelProcessingJob> JobAsync(WebApplicationFactory<Program> host, Guid id)
        => ReadAsync(host, db => db.BldModelJobs.IgnoreQueryFilters().AsNoTracking().SingleAsync(j => j.Id == id));

    private static Task<List<BuildModelElement>> ElementsAsync(WebApplicationFactory<Program> host, Guid versionId)
        => ReadAsync(host, db => db.BldModelElements.IgnoreQueryFilters().AsNoTracking().Where(e => e.ModelVersionId == versionId)
            .OrderBy(e => e.ExternalId).ToListAsync());

    private static Task<List<BuildModelZone>> ZonesAsync(WebApplicationFactory<Program> host, Guid versionId)
        => ReadAsync(host, db => db.BldModelZones.IgnoreQueryFilters().AsNoTracking().Where(z => z.ModelVersionId == versionId)
            .OrderBy(z => z.Order).ToListAsync());

    /// <summary>glTF JSON: a top-level group ("Pavimento") with one mesh node per name, plus a loose mesh outside any group.</summary>
    private static string GltfJson(IEnumerable<string> names, string bufferUri)
    {
        var list = names.ToList();
        var nodes = new List<object> { new { name = "Pavimento", children = Enumerable.Range(1, list.Count).ToArray() } };
        nodes.AddRange(list.Select(n => (object)new { name = n, mesh = 0 }));
        nodes.Add(new { name = "Solto", mesh = 0, extras = new { category = "IfcRailing" } });
        return JsonSerializer.Serialize(new
        {
            asset = new { version = "2.0" },
            scene = 0,
            scenes = new[] { new { nodes = new[] { 0, list.Count + 1 } } },
            nodes,
            meshes = new[] { new { primitives = new[] { new { attributes = new { POSITION = 0 } } } } },
            accessors = new[] { new { bufferView = 0, componentType = 5126, count = 3, type = "VEC3" } },
            bufferViews = new[] { new { buffer = 0, byteLength = 36 } },
            buffers = new[] { new { byteLength = 36, uri = bufferUri } },
        });
    }

    private static byte[] Glb(IEnumerable<string> names)
    {
        var doc = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(GltfJson(names, "data:,"))!;
        doc["buffers"] = JsonSerializer.SerializeToElement(new[] { new { byteLength = 36 } });
        var json = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(doc));
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

    private static byte[] Ifc()
    {
        using var s = typeof(BuildModelProcessorTests).Assembly.GetManifestResourceStream("casa-teste.ifc")!;
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    }

    /// <summary>
    /// A stand-in for IfcConvert (fixed arguments: --use-element-guids -y input output): "copy" writes the input as the output,
    /// "fail" exits with 3, "empty" exits with 0 without writing anything.
    /// </summary>
    private static string FakeIfcConvert(string behaviour)
    {
        var dir = Path.Combine(Path.GetTempPath(), "orken-fake-ifcconvert-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        if (OperatingSystem.IsWindows())
        {
            var cmd = Path.Combine(dir, "ifcconvert.cmd");
            File.WriteAllText(cmd, behaviour switch
            {
                "copy" => "@copy /b \"%~3\" \"%~4\" >nul\r\n@exit /b 0\r\n",
                "fail" => "@exit /b 3\r\n",
                _ => "@exit /b 0\r\n",
            });
            return cmd;
        }
        var sh = Path.Combine(dir, "ifcconvert.sh");
        File.WriteAllText(sh, behaviour switch
        {
            "copy" => "#!/bin/sh\ncp \"$3\" \"$4\"\n",
            "fail" => "#!/bin/sh\nexit 3\n",
            _ => "#!/bin/sh\nexit 0\n",
        });
        File.SetUnixFileMode(sh, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return sh;
    }

    // ── Leitura dos formatos ──────────────────────────────────────────────────

    [Fact]
    public async Task A_glb_becomes_mesh_elements_inside_its_group_zone_and_the_current_version()
    {
        var storage = new InMemoryStorageProvider();
        var (projectId, tenant) = await ProjectAsync("Obra fila GLB");
        await using var host = Host(storage);
        var seeded = await SeedAsync(host, storage, tenant, projectId, BuildModelSourceFormat.Glb,
            Glb(["IfcWall_parede-01", "IfcSlab_laje-01", "Porta"]), ".glb");

        await DrainAsync(host);

        var version = await VersionAsync(host, seeded.VersionId);
        version.Status.Should().Be(BuildModelVersionStatus.Ready);
        version.GeometrySource.Should().Be(BuildGeometrySource.Converted3D);
        version.Schema.Should().Be("glTF 2.0");
        version.ElementCount.Should().Be(4);
        version.StatusMessage.Should().Be("Arquivo 3D lido: 4 elemento(s). Os ids vêm dos nomes dos nós do arquivo.");
        version.ViewerSource.Should().Be(BuildViewerSource.Uploaded, "a GLB is already a viewer file: nothing to convert");
        (await JobAsync(host, seeded.JobId)).Status.Should().Be(BuildModelJobStatus.Done);
        (await ReadAsync(host, db => db.BldModels.IgnoreQueryFilters().AsNoTracking().SingleAsync(m => m.Id == seeded.ModelId)))
            .CurrentVersionId.Should().Be(seeded.VersionId);

        var zones = await ZonesAsync(host, seeded.VersionId);
        var zone = zones.Should().ContainSingle().Subject;
        zone.Name.Should().Be("Pavimento");
        zone.Kind.Should().Be(BuildZoneKind.Area);

        var elements = await ElementsAsync(host, seeded.VersionId);
        elements.Select(e => (e.ExternalId, e.Category, e.ZoneId)).Should().BeEquivalentTo(new[]
        {
            ("IfcSlab_laje-01", "IfcSlab", (Guid?)zone.Id),
            ("IfcWall_parede-01", "IfcWall", zone.Id),
            ("Porta", "Mesh", zone.Id),
            ("Solto", "IfcRailing", (Guid?)null),
        });
        elements.Should().OnlyContain(e => e.TenantId == tenant && e.ProjectId == projectId);
    }

    [Fact]
    public async Task Reprocessing_a_version_replaces_its_elements_and_keeps_the_same_zone_ids()
    {
        var storage = new InMemoryStorageProvider();
        var (projectId, tenant) = await ProjectAsync("Obra fila reprocesso");
        await using var host = Host(storage);
        var seeded = await SeedAsync(host, storage, tenant, projectId, BuildModelSourceFormat.Glb, Glb(["A", "B"]), ".glb");
        await DrainAsync(host);
        var firstZone = (await ZonesAsync(host, seeded.VersionId)).Single().Id;

        storage.Objects[seeded.SourceKey] = (Glb(["A", "C", "D"]), "model/gltf-binary");
        await ReadAsync(host, async db =>
        {
            db.BldModelJobs.Add(BuildModelProcessingJob.Enqueue(tenant, seeded.VersionId, BuildModelJobKind.Parse));
            return await db.SaveChangesAsync();
        });
        await DrainAsync(host);

        (await ElementsAsync(host, seeded.VersionId)).Select(e => e.ExternalId).Should().Equal("A", "C", "D", "Solto");
        (await ZonesAsync(host, seeded.VersionId)).Single().Id.Should().Be(firstZone, "zone ids are derived from the version and key");
    }

    [Fact]
    public async Task A_gltf_with_embedded_buffers_is_read_like_a_glb()
    {
        var storage = new InMemoryStorageProvider();
        var (projectId, tenant) = await ProjectAsync("Obra fila glTF");
        await using var host = Host(storage);
        var json = GltfJson(["Viga-01", "Viga-01", "Pilar-01"], "data:application/octet-stream;base64," + Convert.ToBase64String(new byte[36]));
        var seeded = await SeedAsync(host, storage, tenant, projectId, BuildModelSourceFormat.Gltf, Encoding.UTF8.GetBytes(json), ".gltf");

        await DrainAsync(host);

        var version = await VersionAsync(host, seeded.VersionId);
        version.Status.Should().Be(BuildModelVersionStatus.Ready);
        version.ElementCount.Should().Be(4);
        // repeated names cannot identify an element: those fall back to the node index
        (await ElementsAsync(host, seeded.VersionId)).Select(e => e.ExternalId).Should().BeEquivalentTo("node:1", "node:2", "Pilar-01", "Solto");
    }

    [Fact]
    public async Task A_dxf_sheet_lists_its_layers_for_assisted_mapping()
    {
        var storage = new InMemoryStorageProvider();
        var (projectId, tenant) = await ProjectAsync("Obra fila DXF");
        await using var host = Host(storage);
        var dxf = string.Join("\n",
            "0", "SECTION", "2", "TABLES", "0", "TABLE", "2", "LAYER",
            "0", "LAYER", "2", "PAREDES", "70", "0",
            "0", "LAYER", "2", "COTAS", "70", "0",
            "0", "ENDTAB", "0", "ENDSEC",
            "0", "SECTION", "2", "ENTITIES", "0", "LINE", "8", "PAREDES", "0", "LINE", "8", "MOBILIARIO",
            "0", "ENDSEC", "0", "EOF");
        var seeded = await SeedAsync(host, storage, tenant, projectId, BuildModelSourceFormat.Dxf, Encoding.UTF8.GetBytes(dxf), ".dxf");

        await DrainAsync(host);

        var version = await VersionAsync(host, seeded.VersionId);
        version.Status.Should().Be(BuildModelVersionStatus.Ready);
        version.GeometrySource.Should().Be(BuildGeometrySource.AssistedMapping);
        version.Schema.Should().Be("DXF");
        version.ElementCount.Should().Be(0);
        version.StatusMessage.Should().Be("Planta DXF lida: 3 camada(s). Desenhe as zonas sobre ela (mapeamento assistido, não é BIM).");
        JsonSerializer.Deserialize<string[]>(version.LayersJson!).Should().Equal("PAREDES", "COTAS", "MOBILIARIO");
        (await ElementsAsync(host, seeded.VersionId)).Should().BeEmpty();
    }

    // ── Falhas ────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("externo")]
    [InlineData("corrompido")]
    [InlineData("versao1")]
    public async Task A_file_problem_fails_at_once_with_a_readable_message_and_writes_nothing(string problem)
    {
        var storage = new InMemoryStorageProvider();
        var (projectId, tenant) = await ProjectAsync("Obra fila arquivo ruim " + problem);
        await using var host = Host(storage);
        var (format, bytes, extension, message) = problem switch
        {
            "externo" => (BuildModelSourceFormat.Gltf, Encoding.UTF8.GetBytes(GltfJson(["A"], "casa.bin")), ".gltf",
                "O glTF aponta para arquivos externos (.bin/texturas): exporte como GLB ou com os dados embutidos. O servidor não busca arquivos externos."),
            "corrompido" => (BuildModelSourceFormat.Glb, Encoding.ASCII.GetBytes("glTF\u0002\0\0\0curto"), ".glb", "GLB incompleto."),
            _ => (BuildModelSourceFormat.Gltf, Encoding.UTF8.GetBytes("""{ "asset": { "version": "1.0" } }"""), ".gltf",
                "Arquivo glTF inválido: só glTF 2.0 é aceito."),
        };
        var seeded = await SeedAsync(host, storage, tenant, projectId, format, bytes, extension);

        (await ProcessOnceAsync(host)).Should().BeTrue();

        var job = await JobAsync(host, seeded.JobId);
        job.Status.Should().Be(BuildModelJobStatus.Failed, "a file problem is not retried");
        job.Attempts.Should().Be(1);
        job.Error.Should().Be(message);
        var version = await VersionAsync(host, seeded.VersionId);
        version.Status.Should().Be(BuildModelVersionStatus.Failed);
        version.StatusMessage.Should().Be(message);
        (await ElementsAsync(host, seeded.VersionId)).Should().BeEmpty();
        (await ZonesAsync(host, seeded.VersionId)).Should().BeEmpty();
    }

    [Fact]
    public async Task A_model_above_the_element_limit_is_refused_without_writing_any_element()
    {
        var storage = new InMemoryStorageProvider();
        var (projectId, tenant) = await ProjectAsync("Obra fila limite");
        await using var host = Host(storage, ("Build:Models:MaxElements", "100"));
        var seeded = await SeedAsync(host, storage, tenant, projectId, BuildModelSourceFormat.Glb,
            Glb(Enumerable.Range(1, 100).Select(i => $"Elemento-{i:000}")), ".glb");   // 100 + the loose one

        await DrainAsync(host);

        var version = await VersionAsync(host, seeded.VersionId);
        version.Status.Should().Be(BuildModelVersionStatus.Failed);
        version.StatusMessage.Should().Be("O modelo tem mais de 100 elementos — divida-o por disciplina ou pavimento.");
        (await JobAsync(host, seeded.JobId)).Attempts.Should().Be(1);
        (await ElementsAsync(host, seeded.VersionId)).Should().BeEmpty();
    }

    [Fact]
    public async Task An_unexpected_failure_is_retried_until_the_third_attempt_and_then_fails_the_version()
    {
        var storage = new InMemoryStorageProvider();
        var (projectId, tenant) = await ProjectAsync("Obra fila armazenamento fora");
        await using var host = Host(storage);
        var seeded = await SeedAsync(host, storage, tenant, projectId, BuildModelSourceFormat.Glb, Glb(["A"]), ".glb");
        storage.PrivateDown = true;

        (await ProcessOnceAsync(host)).Should().BeTrue();

        var job = await JobAsync(host, seeded.JobId);
        job.Status.Should().Be(BuildModelJobStatus.Pending, "back to the queue while attempts remain");
        job.Attempts.Should().Be(1);
        job.Error.Should().Be("Falha inesperada ao processar o arquivo.", "the exception detail stays in the log");
        var version = await VersionAsync(host, seeded.VersionId);
        version.Status.Should().Be(BuildModelVersionStatus.Queued);
        version.StatusMessage.Should().Be("Nova tentativa na fila (1/3).");

        await DrainAsync(host);

        job = await JobAsync(host, seeded.JobId);
        job.Status.Should().Be(BuildModelJobStatus.Failed);
        job.Attempts.Should().Be(BuildModelProcessingJob.MaxAttempts);
        version = await VersionAsync(host, seeded.VersionId);
        version.Status.Should().Be(BuildModelVersionStatus.Failed);
        version.StatusMessage.Should().Be("Falha inesperada ao processar o arquivo.");
    }

    [Fact]
    public async Task An_empty_queue_does_nothing()
    {
        await using var host = Host(new InMemoryStorageProvider());
        await DrainAsync(host);
        (await ProcessOnceAsync(host)).Should().BeFalse();
    }

    // ── Conversão IFC → GLB ───────────────────────────────────────────────────

    [Fact]
    public async Task Without_a_converter_a_conversion_job_only_explains_the_browser_alternative()
    {
        var storage = new InMemoryStorageProvider();
        var (projectId, tenant) = await ProjectAsync("Obra fila sem conversor");
        await using var host = Host(storage, ("Build:Models:IfcConvertPath", Path.Combine(Path.GetTempPath(), "nao-existe", "IfcConvert")));
        var seeded = await SeedAsync(host, storage, tenant, projectId, BuildModelSourceFormat.Ifc, Ifc(), ".ifc", BuildModelJobKind.Convert);

        await DrainAsync(host);

        (await JobAsync(host, seeded.JobId)).Status.Should().Be(BuildModelJobStatus.Done);
        var version = await VersionAsync(host, seeded.VersionId);
        version.StatusMessage.Should().EndWith("Conversão no servidor indisponível; o arquivo do visualizador pode ser gerado no navegador.");
        version.ViewerFileKey.Should().BeNull();
    }

    [Fact]
    public async Task A_conversion_job_for_a_file_that_is_not_ifc_changes_nothing()
    {
        var storage = new InMemoryStorageProvider();
        var (projectId, tenant) = await ProjectAsync("Obra fila conversão GLB");
        await using var host = Host(storage, ("Build:Models:IfcConvertPath", FakeIfcConvert("copy")));
        var seeded = await SeedAsync(host, storage, tenant, projectId, BuildModelSourceFormat.Glb, Glb(["A"]), ".glb", BuildModelJobKind.Convert);

        await DrainAsync(host);

        (await JobAsync(host, seeded.JobId)).Status.Should().Be(BuildModelJobStatus.Done);
        var version = await VersionAsync(host, seeded.VersionId);
        version.ViewerSource.Should().Be(BuildViewerSource.Uploaded);
        version.StatusMessage.Should().Be("Na fila de processamento.");
    }

    [Fact]
    public async Task With_a_converter_an_ifc_is_parsed_then_converted_on_the_server_into_the_viewer_file()
    {
        var storage = new InMemoryStorageProvider();
        var (projectId, tenant) = await ProjectAsync("Obra fila conversão");
        await using var host = Host(storage, ("Build:Models:IfcConvertPath", FakeIfcConvert("copy")));
        var ifc = Ifc();
        var seeded = await SeedAsync(host, storage, tenant, projectId, BuildModelSourceFormat.Ifc, ifc, ".ifc");

        (await ProcessOnceAsync(host)).Should().BeTrue();   // parse

        var parsed = await VersionAsync(host, seeded.VersionId);
        parsed.Status.Should().Be(BuildModelVersionStatus.Ready);
        parsed.GeometrySource.Should().Be(BuildGeometrySource.NativeBim);
        parsed.StatusMessage.Should().EndWith(" Conversão para o visualizador na fila.");
        parsed.ElementCount.Should().BeGreaterThan(0);
        var convert = await ReadAsync(host, db => db.BldModelJobs.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(j => j.VersionId == seeded.VersionId && j.Kind == BuildModelJobKind.Convert));
        convert.Status.Should().Be(BuildModelJobStatus.Pending);

        await DrainAsync(host);   // convert

        (await JobAsync(host, convert.Id)).Status.Should().Be(BuildModelJobStatus.Done);
        var converted = await VersionAsync(host, seeded.VersionId);
        var viewerKey = BuildModelService.ViewerKey(tenant, seeded.ModelId, seeded.VersionId);
        converted.ViewerFileKey.Should().Be(viewerKey);
        converted.ViewerSource.Should().Be(BuildViewerSource.ConvertedServer);
        converted.StatusMessage.Should().EndWith("Arquivo do visualizador gerado no servidor.");
        storage.Objects[viewerKey].Bytes.Should().Equal(ifc, "the stand-in converter copies its input");
        storage.Objects[viewerKey].ContentType.Should().Be("model/gltf-binary");
    }

    [Theory]
    [InlineData("fail")]
    [InlineData("empty")]
    public async Task A_converter_that_fails_is_retried_and_the_version_keeps_its_parsed_model(string behaviour)
    {
        var storage = new InMemoryStorageProvider();
        var (projectId, tenant) = await ProjectAsync("Obra fila conversor " + behaviour);
        await using var host = Host(storage, ("Build:Models:IfcConvertPath", FakeIfcConvert(behaviour)));
        var seeded = await SeedAsync(host, storage, tenant, projectId, BuildModelSourceFormat.Ifc, Ifc(), ".ifc", BuildModelJobKind.Convert);

        (await ProcessOnceAsync(host)).Should().BeTrue();
        var job = await JobAsync(host, seeded.JobId);
        job.Status.Should().Be(BuildModelJobStatus.Pending);
        job.Error.Should().Be("Falha inesperada ao processar o arquivo.");
        (await VersionAsync(host, seeded.VersionId)).StatusMessage.Should().Be("Na fila de processamento.",
            "a conversion retry does not touch the version");

        await DrainAsync(host);

        job = await JobAsync(host, seeded.JobId);
        job.Status.Should().Be(BuildModelJobStatus.Failed);
        job.Attempts.Should().Be(BuildModelProcessingJob.MaxAttempts);
        var version = await VersionAsync(host, seeded.VersionId);
        version.StatusMessage.Should().EndWith(
            "A conversão para o visualizador no servidor falhou; o arquivo do visualizador pode ser gerado no navegador.");
        version.ViewerFileKey.Should().BeNull();
        storage.Objects.Keys.Should().NotContain(BuildModelService.ViewerKey(tenant, seeded.ModelId, seeded.VersionId));
    }
}
