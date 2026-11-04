using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Nexo.Application.Integrations.Contracts;
using Nexo.Application.Integrations.DTOs;
using Nexo.Application.Modules.Build;
using Nexo.Domain.Modules.Build;
using Nexo.Infrastructure.Persistence;

namespace Nexo.Infrastructure.Modules.Build;

/// <summary>Processes the model queue one job at a time. Integration tests drive it directly; production runs it in
/// <see cref="BuildModelProcessingJobRunner"/>.</summary>
public interface IBuildModelProcessor
{
    /// <summary>Claims and processes the oldest pending job. False when the queue is empty.</summary>
    Task<bool> ProcessNextAsync(CancellationToken ct);
}

/// <summary>
/// The model processing worker (system-wide, no current tenant: every query names the job's tenant).
///   - claim: one atomic UPDATE … WHERE id = (SELECT … FOR UPDATE SKIP LOCKED) RETURNING id — two workers never take the same
///     job; the claim sets a lock that expires (a crashed worker's job is taken again), at most 3 attempts;
///   - Parse: reads the private source file into a temporary file and extracts zones/elements (IFC via xBIM, GLB/glTF JSON,
///     DXF layer names) in one transaction (elements in batches of 1000) — a failure leaves nothing half-written;
///   - Convert (only when Build:Models:IfcConvertPath points to an existing IfcConvert binary): IFC → GLB with element
///     GlobalIds as node names, fixed arguments, timeout, temporary folder always removed.
/// File problems (unreadable, unsupported schema…) fail at once with a readable message; other errors are retried.
/// </summary>
public sealed class BuildModelProcessor : IBuildModelProcessor
{
    private const int Batch = 1000;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly NexoDbContext _db;
    private readonly IStorageProvider _storage;
    private readonly BuildTwinOptions _options;
    private readonly ILogger<BuildModelProcessor> _logger;

    public BuildModelProcessor(NexoDbContext db, IStorageProvider storage, BuildTwinOptions options, ILogger<BuildModelProcessor> logger)
    {
        _db = db;
        _storage = storage;
        _options = options;
        _logger = logger;
    }

    public async Task<bool> ProcessNextAsync(CancellationToken ct)
    {
        await FailAbandonedAsync(ct);
        var jobId = await ClaimAsync(ct);
        if (jobId is null) return false;

        var job = await _db.BldModelJobs.IgnoreQueryFilters().FirstAsync(j => j.Id == jobId, ct);
        var tenantId = job.TenantId;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(_options.JobTimeout);
        try
        {
            if (job.Kind == BuildModelJobKind.Parse) await ParseAsync(tenantId, job.VersionId, timeout.Token);
            else await ConvertAsync(tenantId, job.VersionId, timeout.Token);
            await FinishAsync(jobId.Value, null, permanent: false, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;   // shutting down: the lock expires and the job is taken again
        }
        catch (Exception ex)
        {
            var permanent = ex is BuildModelFileException;
            var message = ex switch
            {
                BuildModelFileException f => f.Message,
                OperationCanceledException => "O processamento passou do tempo limite.",
                _ => "Falha inesperada ao processar o arquivo.",
            };
            if (!permanent) _logger.LogWarning(ex, "[Build] Model job {JobId} failed", jobId);
            await FinishAsync(jobId.Value, message, permanent, ct);
        }
        return true;
    }

    // ── Queue ─────────────────────────────────────────────────────────────────

    private async Task<Guid?> ClaimAsync(CancellationToken ct)
    {
        var lockSeconds = (int)(_options.JobTimeout + TimeSpan.FromMinutes(5)).TotalSeconds;
        var ids = await _db.Database.SqlQueryRaw<Guid>(
            """
            UPDATE nexo.bld_model_processing_jobs
               SET status = 2, attempts = attempts + 1, started_at = now(), updated_at = now(),
                   locked_until = now() + make_interval(secs => {0})
             WHERE id = (SELECT id FROM nexo.bld_model_processing_jobs
                          WHERE (status = 1 OR (status = 2 AND locked_until < now())) AND attempts < {1}
                          ORDER BY created_at
                          FOR UPDATE SKIP LOCKED
                          LIMIT 1)
            RETURNING id AS "Value"
            """, lockSeconds, BuildModelProcessingJob.MaxAttempts).ToListAsync(ct);
        return ids.Count == 0 ? null : ids[0];
    }

    /// <summary>Jobs whose worker died on the last attempt: failed for good (and their version, for a parse).</summary>
    private async Task FailAbandonedAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var dead = await _db.BldModelJobs.IgnoreQueryFilters()
            .Where(j => j.Status == BuildModelJobStatus.Running && j.LockedUntil < now && j.Attempts >= BuildModelProcessingJob.MaxAttempts)
            // the longest-dead first, id as the tie-break: a stable batch when more than 20 are waiting
            .OrderBy(j => j.LockedUntil).ThenBy(j => j.Id)
            .Select(j => j.Id).Take(20).ToListAsync(ct);
        foreach (var id in dead)
            await FinishAsync(id, "O processamento foi interrompido várias vezes.", permanent: true, ct);
    }

    private async Task FinishAsync(Guid jobId, string? error, bool permanent, CancellationToken ct)
    {
        _db.ChangeTracker.Clear();
        var job = await _db.BldModelJobs.IgnoreQueryFilters().FirstAsync(j => j.Id == jobId, ct);
        if (error is null)
        {
            job.Complete();
            await _db.SaveChangesAsync(ct);
            return;
        }
        var failed = job.Fail(error, permanent);
        var version = await _db.BldModelVersions.IgnoreQueryFilters()
            .FirstOrDefaultAsync(v => v.TenantId == job.TenantId && v.Id == job.VersionId, ct);
        if (version is not null)
        {
            if (job.Kind == BuildModelJobKind.Parse)
            {
                if (failed) version.MarkFailed(error);
                else version.MarkQueued($"Nova tentativa na fila ({job.Attempts}/{BuildModelProcessingJob.MaxAttempts}).");
            }
            else if (failed)
            {
                version.AppendMessage("A conversão para o visualizador no servidor falhou; o arquivo do visualizador pode ser gerado no navegador.");
            }
        }
        await _db.SaveChangesAsync(ct);
    }

    // ── Parse ─────────────────────────────────────────────────────────────────

    private async Task ParseAsync(Guid tenantId, Guid versionId, CancellationToken ct)
    {
        var version = await Version(tenantId, versionId).FirstOrDefaultAsync(ct);
        if (version is null || version.SourceFileKey is null) return;   // deleted meanwhile, or nothing stored
        version.MarkProcessing();
        await _db.SaveChangesAsync(ct);

        await using var file = await DownloadAsync(version.SourceFileKey, ct);
        var parsed = version.SourceFormat switch
        {
            BuildModelSourceFormat.Ifc => BuildIfcParser.Parse(file, _options.MaxElements),
            BuildModelSourceFormat.Glb => BuildGltfReader.Extract(BuildGltfReader.ReadGlb(file)),
            BuildModelSourceFormat.Gltf => BuildGltfReader.Extract(BuildGltfReader.ReadJson(await ReadAllAsync(file, ct))),
            BuildModelSourceFormat.Dxf => Dxf(file),
            _ => new BuildParsedModel(null, BuildGeometrySource.AssistedMapping, [], [],
                "Folha 2D pronta para o mapeamento assistido: desenhe as zonas sobre ela."),
        };
        if (parsed.Elements.Count > _options.MaxElements)
            throw new BuildModelFileException($"O modelo tem mais de {_options.MaxElements} elementos — divida-o por disciplina ou pavimento.",
                "model_too_many_elements");

        var projectId = version.ProjectId;
        var modelId = version.ModelId;
        var isIfc = version.SourceFormat == BuildModelSourceFormat.Ifc;

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        await _db.BldModelElements.IgnoreQueryFilters().Where(e => e.TenantId == tenantId && e.ModelVersionId == versionId).ExecuteDeleteAsync(ct);
        await _db.BldModelZones.IgnoreQueryFilters().Where(z => z.TenantId == tenantId && z.ModelVersionId == versionId).ExecuteDeleteAsync(ct);
        _db.ChangeTracker.Clear();

        var zoneIds = parsed.Zones.ToDictionary(z => z.Key, z => ZoneId(versionId, z.Key), StringComparer.Ordinal);
        _db.BldModelZones.AddRange(parsed.Zones.Select(z => BuildModelZone.FromModel(tenantId, projectId, versionId, zoneIds[z.Key], z.Kind,
            z.Name, z.ExternalId, z.ParentKey is { } pk && zoneIds.TryGetValue(pk, out var pid) ? pid : null, z.Elevation, z.Order)));
        await _db.SaveChangesAsync(ct);
        _db.ChangeTracker.Clear();

        foreach (var chunk in parsed.Elements.Chunk(Batch))
        {
            _db.BldModelElements.AddRange(chunk.Select(e => BuildModelElement.Create(tenantId, projectId, versionId, e.ExternalId, e.Category,
                e.Name, e.TypeName, e.Material, e.ZoneKey is { } zk && zoneIds.TryGetValue(zk, out var zid) ? zid : null, e.PropertiesJson)));
            await _db.SaveChangesAsync(ct);
            _db.ChangeTracker.Clear();
        }

        version = await Version(tenantId, versionId).FirstAsync(ct);
        var message = parsed.Message;
        var convert = isIfc && parsed.Geometry == BuildGeometrySource.NativeBim && _options.IfcConverterAvailable;
        if (isIfc && !convert)
            message += parsed.Geometry == BuildGeometrySource.NativeBim
                ? " O arquivo do visualizador (GLB) pode ser gerado no navegador ao abrir o modelo."
                : " Sem geometria, não há o que mostrar em 3D: as zonas e etapas continuam utilizáveis.";
        if (convert) message += " Conversão para o visualizador na fila.";
        version.MarkReady(parsed.Elements.Count, parsed.StoreyCount, parsed.Schema, parsed.Geometry, message, parsed.LayersJson);
        var model = await _db.BldModels.IgnoreQueryFilters().FirstOrDefaultAsync(m => m.TenantId == tenantId && m.Id == modelId, ct);
        model?.SetCurrentVersion(versionId);
        if (convert) _db.BldModelJobs.Add(BuildModelProcessingJob.Enqueue(tenantId, versionId, BuildModelJobKind.Convert));
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        _logger.LogInformation("[Build] Model version {VersionId} parsed: {Elements} elements, {Zones} zones", versionId,
            parsed.Elements.Count, parsed.Zones.Count);
    }

    private static BuildParsedModel Dxf(Stream file)
    {
        using var reader = new StreamReader(file, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        var layers = BuildDxfReader.Layers(reader);
        return new BuildParsedModel("DXF", BuildGeometrySource.AssistedMapping, [], [],
            $"Planta DXF lida: {layers.Count} camada(s). Desenhe as zonas sobre ela (mapeamento assistido, não é BIM).",
            JsonSerializer.Serialize(layers, Json));
    }

    /// <summary>Deterministic zone id per (version, external key): a retried parse produces the same ids.</summary>
    private static Guid ZoneId(Guid versionId, string key)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{versionId:N}|{key}"));
        var bytes = hash[..16];
        bytes[7] = (byte)((bytes[7] & 0x0F) | 0x50);   // version-5-like, RFC 4122 variant
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return new Guid(bytes);
    }

    // ── Convert (optional IfcConvert) ─────────────────────────────────────────

    private async Task ConvertAsync(Guid tenantId, Guid versionId, CancellationToken ct)
    {
        var version = await Version(tenantId, versionId).FirstOrDefaultAsync(ct);
        if (version is null || version.SourceFileKey is null || version.SourceFormat != BuildModelSourceFormat.Ifc) return;
        if (!_options.IfcConverterAvailable)
        {
            version.AppendMessage("Conversão no servidor indisponível; o arquivo do visualizador pode ser gerado no navegador.");
            await _db.SaveChangesAsync(ct);
            return;
        }

        var dir = Path.Combine(Path.GetTempPath(), "orken-ifcconvert-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var input = Path.Combine(dir, "input.ifc");
            var output = Path.Combine(dir, "output.glb");
            await using (var source = await _storage.OpenReadAsync(version.SourceFileKey, ct))
            await using (var target = File.Create(input))
                await source.CopyToAsync(target, ct);

            await RunIfcConvertAsync(_options.IfcConvertPath!, input, output, _options.IfcConvertTimeout, ct);
            if (!File.Exists(output) || new FileInfo(output).Length == 0)
                throw new InvalidOperationException("IfcConvert produced no output.");

            var key = BuildModelService.ViewerKey(tenantId, version.ModelId, version.Id);
            await using (var glb = File.OpenRead(output))
                await _storage.UploadAsync(new StorageUploadRequest(glb, "viewer.glb", "model/gltf-binary", key, glb.Length), ct);
            version.AttachViewer(key, BuildViewerSource.ConvertedServer);
            version.AppendMessage("Arquivo do visualizador gerado no servidor.");
            await _db.SaveChangesAsync(ct);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); }
            catch (Exception ex) { _logger.LogWarning(ex, "[Build] Could not remove the conversion folder {Dir}", dir); }
        }
    }

    /// <summary>Runs IfcConvert with fixed arguments (no user input reaches the command line) and a hard timeout.</summary>
    internal static async Task RunIfcConvertAsync(string exe, string input, string output, TimeSpan timeout, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(input)!,
        };
        foreach (var arg in new[] { "--use-element-guids", "-y", input, output }) psi.ArgumentList.Add(arg);
        using var process = Process.Start(psi) ?? throw new InvalidOperationException("IfcConvert did not start.");
        var stdout = process.StandardOutput.ReadToEndAsync(ct);
        var stderr = process.StandardError.ReadToEndAsync(ct);
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(timeout);
        try
        {
            await process.WaitForExitAsync(limit.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { /* already gone */ }
            throw;
        }
        await Task.WhenAll(stdout, stderr);
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"IfcConvert exited with {process.ExitCode}.");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private IQueryable<BuildModelVersion> Version(Guid tenantId, Guid versionId)
        => _db.BldModelVersions.IgnoreQueryFilters().Where(v => v.TenantId == tenantId && v.Id == versionId);

    /// <summary>The stored file copied to a temporary file (deleted on dispose) — xBIM and the readers need a seekable stream.</summary>
    private async Task<FileStream> DownloadAsync(string key, CancellationToken ct)
    {
        var path = Path.Combine(Path.GetTempPath(), "orken-model-" + Guid.NewGuid().ToString("N"));
        var file = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 81920,
            FileOptions.DeleteOnClose | FileOptions.Asynchronous);
        try
        {
            await using (var source = await _storage.OpenReadAsync(key, ct))
                await source.CopyToAsync(file, ct);
            file.Position = 0;
            return file;
        }
        catch
        {
            await file.DisposeAsync();
            throw;
        }
    }

    private static async Task<byte[]> ReadAllAsync(Stream s, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        await s.CopyToAsync(ms, ct);
        return ms.ToArray();
    }
}

/// <summary>Polls the model queue (not registered in the Testing environment — tests call the processor directly).</summary>
public sealed class BuildModelProcessingJobRunner : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<BuildModelProcessingJobRunner> _logger;

    public BuildModelProcessingJobRunner(IServiceScopeFactory scopes, ILogger<BuildModelProcessingJobRunner> logger)
    {
        _scopes = scopes;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            TimeSpan wait;
            try
            {
                using var scope = _scopes.CreateScope();
                var processor = scope.ServiceProvider.GetRequiredService<IBuildModelProcessor>();
                wait = scope.ServiceProvider.GetRequiredService<BuildTwinOptions>().PollInterval;
                for (var i = 0; i < 20 && await processor.ProcessNextAsync(stoppingToken); i++) { /* drain a little */ }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Build model processing failed; retrying");
                wait = TimeSpan.FromSeconds(30);
            }
            try { await Task.Delay(wait, stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }
}
