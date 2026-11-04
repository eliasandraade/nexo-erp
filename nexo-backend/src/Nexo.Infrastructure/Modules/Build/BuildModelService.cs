using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nexo.Application.Common.Interfaces;
using Nexo.Application.Integrations.Contracts;
using Nexo.Application.Integrations.DTOs;
using Nexo.Application.Integrations.Options;
using Nexo.Application.Modules.Build;
using Nexo.Domain.Exceptions;
using Nexo.Domain.Modules.Build;
using Nexo.Infrastructure.Integrations.Storage;
using Nexo.Infrastructure.Persistence;

namespace Nexo.Infrastructure.Modules.Build;

/// <summary>
/// Models and their versions — company side (tenant-scoped, Management). Uploads are identified by their first bytes (never the
/// name), stored privately under <c>private/tenants/{t}/build/models/{model}/{version}/source.&lt;ext&gt;</c> (the client's
/// file name never enters a key) with their SHA-256, and queued for processing. RVT/DWG are recorded as Rejected without
/// storing anything; unknown/compressed/external-reference files are refused (422) and nothing is recorded.
/// All of it requires the company's Build3D flag (403 build_3d_disabled).
/// </summary>
public sealed class BuildModelService
{
    public const string Folder = "build/models";
    public const string CodeViewerMismatch = "viewer_mismatch";

    private readonly NexoDbContext _db;
    private readonly ICurrentTenant _tenant;
    private readonly BuildAuditTrail _audit;
    private readonly BuildTenantSettingsService _settings;
    private readonly IStorageProvider _storage;
    private readonly IIntegrationFeatureFlags _flags;
    private readonly BuildTwinOptions _options;
    private readonly ILogger<BuildModelService> _logger;

    public BuildModelService(NexoDbContext db, ICurrentTenant tenant, BuildAuditTrail audit, BuildTenantSettingsService settings,
        IStorageProvider storage, IIntegrationFeatureFlags flags, BuildTwinOptions options, ILogger<BuildModelService> logger)
    {
        _db = db;
        _tenant = tenant;
        _audit = audit;
        _settings = settings;
        _storage = storage;
        _flags = flags;
        _options = options;
        _logger = logger;
    }

    private Guid Tid => _tenant.Id;

    public static string SourceKey(Guid tenantId, Guid modelId, Guid versionId, string extension)
        => StorageKeys.Build(tenantId, $"{Folder}/{modelId}/{versionId}", "source" + extension, isPrivate: true);

    public static string ViewerKey(Guid tenantId, Guid modelId, Guid versionId)
        => StorageKeys.Build(tenantId, $"{Folder}/{modelId}/{versionId}", "viewer.glb", isPrivate: true);

    // ── Reads ─────────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<BuildModelDto>> ListAsync(Guid projectId, CancellationToken ct)
    {
        await BuildScope.EnsureProjectAsync(_db, Tid, projectId, ct);
        var models = await _db.BldModels.AsNoTracking().Where(m => m.TenantId == Tid && m.ProjectId == projectId)
            .OrderBy(m => m.CreatedAt).ToListAsync(ct);
        var versions = await _db.BldModelVersions.AsNoTracking().Where(v => v.TenantId == Tid && v.ProjectId == projectId).ToListAsync(ct);
        return models.Select(m => BuildTwinMap.Model(m, versions)).ToList();
    }

    public async Task<BuildModelDto> GetAsync(Guid modelId, CancellationToken ct)
    {
        var m = await _db.BldModels.AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == Tid && x.Id == modelId, ct)
            ?? throw new NotFoundException("BuildModel", modelId);
        var versions = await _db.BldModelVersions.AsNoTracking().Where(v => v.TenantId == Tid && v.ModelId == modelId).ToListAsync(ct);
        return BuildTwinMap.Model(m, versions);
    }

    public async Task<BuildModelVersionDto> GetVersionAsync(Guid versionId, CancellationToken ct)
        => BuildTwinMap.Version(await LoadVersionAsync(versionId, tracking: false, ct));

    /// <summary>Signed URL (15 min) of the version's viewer derivative — never the source file.</summary>
    public async Task<BuildModelViewerDto> ViewerAsync(Guid versionId, CancellationToken ct)
    {
        var v = await LoadVersionAsync(versionId, tracking: false, ct);
        string? url = null;
        if (v.ViewerFileKey is { } key && _flags.StorageEnabled && StorageKeys.IsOfTenant(key, Tid))
        {
            try { url = _storage.GetSignedUrl(key, PrivateFileUrls.Ttl); }
            catch (Exception ex) { _logger.LogWarning(ex, "Could not sign a model viewer URL."); }
        }
        return new BuildModelViewerDto(v.Id, url, url is null ? null : DateTimeOffset.UtcNow.Add(PrivateFileUrls.Ttl),
            v.GeometrySource.ToString(), v.ViewerSource.ToString());
    }

    /// <summary>
    /// Signed URL (15 min) of the version's private source file (IFC, GLB, PDF…) for the company's own users — never offered
    /// to the portal. Every read is audited. 422 source_not_stored when the file was refused (RVT/DWG); 503 when storage is off.
    /// </summary>
    public async Task<BuildModelSourceDto> SourceAsync(Guid versionId, CancellationToken ct)
    {
        var v = await LoadVersionAsync(versionId, tracking: false, ct);
        if (v.SourceFileKey is not { } key || !StorageKeys.IsOfTenant(key, Tid))
            throw new DomainException("O arquivo desta versão não foi armazenado (formato recusado).", "source_not_stored");
        if (!_flags.StorageEnabled) throw new BuildStorageUnavailableException("O armazenamento de arquivos está indisponível.");
        string url;
        try { url = _storage.GetSignedUrl(key, PrivateFileUrls.Ttl); }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not sign a model source URL.");
            throw new BuildStorageUnavailableException("Não foi possível gerar o link do arquivo agora. Tente de novo.", ex);
        }
        _audit.Record(AuditActions.BuildModelSourceRead, "ModelVersion", v.Id,
            $"Link do arquivo original da versão {v.Number} gerado.", new { projectId = v.ProjectId, modelId = v.ModelId, format = v.SourceFormat.ToString() });
        await _db.SaveChangesAsync(ct);
        return new BuildModelSourceDto(v.Id, v.SourceFileName, v.SourceFormat.ToString(), v.SizeBytes, v.Sha256, url,
            DateTimeOffset.UtcNow.Add(PrivateFileUrls.Ttl));
    }

    // ── Uploads ───────────────────────────────────────────────────────────────

    public async Task<BuildModelUploadResultDto> CreateModelAsync(Guid projectId, string? name, string? discipline,
        BuildUploadBuffer file, CancellationToken ct)
    {
        await _settings.EnsureBuild3DAsync(Tid, ct);
        await BuildScope.EnsureProjectAsync(_db, Tid, projectId, ct);
        var parsedDiscipline = string.IsNullOrWhiteSpace(discipline)
            ? BuildModelDiscipline.Arquitetura
            : BuildTwinParse.Enum<BuildModelDiscipline>(discipline, "Disciplina");
        var decision = await DecideAsync(file, ct);
        var model = BuildModel.Create(Tid, projectId, string.IsNullOrWhiteSpace(name) ? Path.GetFileNameWithoutExtension(file.FileName) : name,
            parsedDiscipline, _audit.UserId);
        _db.BldModels.Add(model);
        return await StoreVersionAsync(model, 1, decision, file, ct);
    }

    public async Task<BuildModelUploadResultDto> AddVersionAsync(Guid modelId, BuildUploadBuffer file, CancellationToken ct)
    {
        await _settings.EnsureBuild3DAsync(Tid, ct);
        var model = await _db.BldModels.FirstOrDefaultAsync(m => m.TenantId == Tid && m.Id == modelId, ct)
            ?? throw new NotFoundException("BuildModel", modelId);
        var decision = await DecideAsync(file, ct);
        var number = (await _db.BldModelVersions.Where(v => v.TenantId == Tid && v.ModelId == modelId)
            .MaxAsync(v => (int?)v.Number, ct) ?? 0) + 1;
        return await StoreVersionAsync(model, number, decision, file, ct);
    }

    /// <summary>Format from the first bytes, plus a full check of what can be checked cheaply (glTF JSON/URIs, GLB header).</summary>
    private static async Task<BuildModelFormatDecision> DecideAsync(BuildUploadBuffer file, CancellationToken ct)
    {
        if (file.Length == 0) throw new BuildModelFileException("Arquivo vazio.", "model_file_invalid");
        var decision = BuildModelFormats.Decide(BuildFileSignatures.Sniff(file.Head));
        if (!decision.Accepted && !decision.Rejected) throw new BuildModelFileException(decision.Message!, decision.Code);
        if (decision.Format == BuildModelSourceFormat.Glb)
        {
            await using var s = file.Open();
            BuildGltfReader.ReadGlb(s);
        }
        else if (decision.Format == BuildModelSourceFormat.Gltf)
        {
            BuildGltfReader.ReadJson(await file.ReadAllAsync(ct));
        }
        return decision;
    }

    private async Task<BuildModelUploadResultDto> StoreVersionAsync(BuildModel model, int number, BuildModelFormatDecision decision,
        BuildUploadBuffer file, CancellationToken ct)
    {
        BuildModelVersion version;
        if (decision.Rejected)
        {
            version = BuildModelVersion.Rejected(Tid, model.ProjectId, model.Id, number, decision.Format!.Value, file.FileName,
                file.Length, decision.Message!, _audit.UserId);
            _db.BldModelVersions.Add(version);
            _audit.Record(AuditActions.BuildModelRejected, "Model", model.Id,
                $"Arquivo recusado ({decision.Format}) no modelo {model.Name}: precisa ser exportado para IFC ou GLB.",
                new { versionId = version.Id, format = decision.Format.ToString(), file.Length });
            await _db.SaveBuildAsync(ct);
            return new BuildModelUploadResultDto(BuildTwinMap.Model(model, [version]), BuildTwinMap.Version(version));
        }

        if (!_flags.StorageEnabled) throw new BuildStorageUnavailableException("Armazenamento de arquivos indisponível.");
        var versionId = Guid.NewGuid();
        var key = SourceKey(Tid, model.Id, versionId, decision.Extension);
        await UploadAsync(file, key, decision.ContentType, ct);

        version = BuildModelVersion.Stored(Tid, model.ProjectId, model.Id, versionId, number, decision.Format!.Value, key,
            file.FileName, file.Length, file.Sha256, _audit.UserId);
        _db.BldModelVersions.Add(version);
        if (version.Status == BuildModelVersionStatus.Ready) model.SetCurrentVersion(version.Id);   // PDF/image sheet
        else _db.BldModelJobs.Add(BuildModelProcessingJob.Enqueue(Tid, version.Id, BuildModelJobKind.Parse));
        _audit.Record(AuditActions.BuildModelUploaded, "Model", model.Id,
            $"Versão {number} do modelo {model.Name} enviada ({decision.Format}, {file.Length / 1024} KB).",
            new { versionId, format = decision.Format.ToString(), file.Length, file.Sha256 });
        try
        {
            await _db.SaveBuildAsync(ct);
        }
        catch
        {
            await DeleteQuietlyAsync(key);
            throw;
        }
        var versions = await _db.BldModelVersions.AsNoTracking().Where(v => v.TenantId == Tid && v.ModelId == model.Id).ToListAsync(ct);
        return new BuildModelUploadResultDto(BuildTwinMap.Model(model, versions), BuildTwinMap.Version(version));
    }

    /// <summary>
    /// The internal viewer converted the IFC in the browser (web-ifc) and sends the GLB whose node names are the IFC GlobalIds.
    /// At least <see cref="BuildTwinOptions.ViewerMatchMinRatio"/> of its node names must be elements of this version (422
    /// viewer_mismatch otherwise — prevents attaching another model's file).
    /// </summary>
    public async Task<BuildModelVersionDto> AttachViewerAsync(Guid versionId, BuildUploadBuffer file, CancellationToken ct)
    {
        await _settings.EnsureBuild3DAsync(Tid, ct);
        var v = await LoadVersionAsync(versionId, tracking: true, ct);
        if (v.SourceFormat != BuildModelSourceFormat.Ifc || v.Status != BuildModelVersionStatus.Ready)
            throw new DomainException("O arquivo do visualizador só pode ser anexado a uma versão IFC já processada.", "viewer_requires_parsed_ifc");
        if (BuildFileSignatures.Sniff(file.Head) != BuildFileKind.Glb)
            throw new BuildModelFileException("Envie o visualizador como GLB (glTF binário 2.0).", "invalid_file_content");

        IReadOnlyList<string> names;
        await using (var s = file.Open()) names = BuildGltfReader.NodeNames(BuildGltfReader.ReadGlb(s));
        var known = (await _db.BldModelElements.AsNoTracking().Where(e => e.TenantId == Tid && e.ModelVersionId == v.Id)
            .Select(e => e.ExternalId).ToListAsync(ct)).ToHashSet(StringComparer.Ordinal);
        var match = BuildViewerMatch.Compute(names, known);
        if (!match.Passes(_options.ViewerMatchMinRatio))
            throw new DomainException(
                $"Este GLB não corresponde à versão: só {match.Matched} de {match.Total} nós ({match.Ratio:P0}) têm o GlobalId de um elemento do modelo. "
                + "Gere o arquivo a partir deste IFC, com os nomes dos nós iguais aos GlobalIds.", CodeViewerMismatch);

        if (!_flags.StorageEnabled) throw new BuildStorageUnavailableException("Armazenamento de arquivos indisponível.");
        var key = ViewerKey(Tid, v.ModelId, v.Id);
        await UploadAsync(file, key, "model/gltf-binary", ct);
        v.AttachViewer(key, BuildViewerSource.ConvertedBrowser);
        _audit.Record(AuditActions.BuildModelViewerAttached, "ModelVersion", v.Id,
            $"Arquivo do visualizador anexado à versão {v.Number} ({match.Matched}/{match.Total} nós reconhecidos).",
            new { match.Matched, match.Total, match.Ratio, file.Length });
        await _db.SaveBuildAsync(ct);
        return BuildTwinMap.Version(v);
    }

    /// <summary>Queues a failed version again (fresh attempts).</summary>
    public async Task<BuildModelVersionDto> ReprocessAsync(Guid versionId, CancellationToken ct)
    {
        await _settings.EnsureBuild3DAsync(Tid, ct);
        var v = await LoadVersionAsync(versionId, tracking: true, ct);
        if (v.Status != BuildModelVersionStatus.Failed || v.SourceFileKey is null)
            throw new DomainException("Só uma versão que falhou no processamento pode ser processada de novo.", "version_not_failed");
        v.MarkQueued();
        _db.BldModelJobs.Add(BuildModelProcessingJob.Enqueue(Tid, v.Id, BuildModelJobKind.Parse));
        _audit.Record(AuditActions.BuildModelReprocessed, "ModelVersion", v.Id, $"Versão {v.Number} enviada de novo para processamento.");
        await _db.SaveBuildAsync(ct);
        return BuildTwinMap.Version(v);
    }

    // ── Removal ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Removes a model with all its versions. The private files go first — only keys under this model's own private folder of
    /// this tenant, through the private bucket alone —, then the records: versions, jobs, zones and elements (database cascade)
    /// and the stage bindings that name a version. A storage failure answers 503 with nothing removed, so the call can simply
    /// be repeated (a file already gone counts as removed). 409 while a version is still processing (a worker could write a file
    /// after the removal) and once execution progress was recorded on its elements (that history is never erased).
    /// </summary>
    public async Task DeleteModelAsync(Guid modelId, CancellationToken ct)
    {
        var model = await _db.BldModels.FirstOrDefaultAsync(m => m.TenantId == Tid && m.Id == modelId, ct)
            ?? throw new NotFoundException("BuildModel", modelId);
        await BuildScope.EnsureProjectAsync(_db, Tid, model.ProjectId, ct);
        var versions = await _db.BldModelVersions.AsNoTracking().Where(v => v.TenantId == Tid && v.ModelId == modelId).ToListAsync(ct);
        var versionIds = versions.Select(v => v.Id).ToList();

        if (versions.Any(v => v.Status is BuildModelVersionStatus.Queued or BuildModelVersionStatus.Processing)
            || await _db.BldModelJobs.AnyAsync(j => j.TenantId == Tid && versionIds.Contains(j.VersionId)
                && (j.Status == BuildModelJobStatus.Pending || j.Status == BuildModelJobStatus.Running), ct))
            throw new ConflictException("O modelo ainda está sendo processado. Aguarde terminar para excluí-lo.");
        if (await _db.BldElementEvents.AnyAsync(e => e.TenantId == Tid && versionIds.Contains(e.ModelVersionId), ct))
            throw new ConflictException("Este modelo já tem avanço de execução registrado nos elementos; esse histórico não pode ser apagado.");

        var folder = $"{Folder}/{modelId}";
        var keys = versions.SelectMany(v => new[] { v.SourceFileKey, v.ViewerFileKey })
            .Where(k => StorageKeys.IsPrivateOf(k, Tid, folder)).Select(k => k!).Distinct().ToList();
        if (keys.Count > 0 && !_flags.StorageEnabled)
            throw new BuildStorageUnavailableException("O armazenamento de arquivos está indisponível. Tente novamente.");
        foreach (var key in keys)
        {
            try { await _storage.DeleteAsync(key, ct); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "[Build] Model file removal failed — model={ModelId}", modelId);
                throw new BuildStorageUnavailableException("Não foi possível remover os arquivos do modelo agora. Nada foi excluído; tente novamente.", ex);
            }
        }

        var bindings = await _db.BldModelBindings
            .Where(b => b.TenantId == Tid && b.ModelVersionId != null && versionIds.Contains(b.ModelVersionId.Value)).ToListAsync(ct);
        _db.BldModelBindings.RemoveRange(bindings);
        _db.BldModels.Remove(model);
        _audit.Record(AuditActions.BuildModelDeleted, "Model", model.Id, $"Modelo excluído: {model.Name}.",
            new { projectId = model.ProjectId, versions = versionIds.Count, files = keys.Count });
        await _db.SaveBuildAsync(ct);
    }

    private async Task<BuildModelVersion> LoadVersionAsync(Guid versionId, bool tracking, CancellationToken ct)
    {
        var q = _db.BldModelVersions.Where(v => v.TenantId == Tid && v.Id == versionId);
        return await (tracking ? q : q.AsNoTracking()).FirstOrDefaultAsync(ct) ?? throw new NotFoundException("BuildModelVersion", versionId);
    }

    private async Task UploadAsync(BuildUploadBuffer file, string key, string contentType, CancellationToken ct)
    {
        try
        {
            await using var stream = file.Open();
            await _storage.UploadAsync(new StorageUploadRequest(stream, Path.GetFileName(key), contentType, key, file.Length), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "[Build] Model upload to storage failed — key={Key}", key);
            throw new BuildStorageUnavailableException("Serviço de armazenamento indisponível. Tente novamente.", ex);
        }
    }

    private async Task DeleteQuietlyAsync(string key)
    {
        try { await _storage.DeleteAsync(key, CancellationToken.None); }
        catch (Exception ex) { _logger.LogWarning(ex, "[Build] Could not remove an orphan model file {Key}", key); }
    }
}
