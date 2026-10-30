using Microsoft.EntityFrameworkCore;
using Nexo.Application.Common.Interfaces;
using Nexo.Application.Modules.Build;
using Nexo.Domain.Exceptions;
using Nexo.Domain.Modules.Build;
using Nexo.Infrastructure.Modules.Interpreter;
using Nexo.Infrastructure.Persistence;

namespace Nexo.Infrastructure.Modules.Build;

/// <summary>
/// Orken Build seen from the platform (SuperAdmin, Wave 6): per company with the module — projects, published portals, model
/// versions by status, processing jobs (failed/pending, last error sanitized), storage by category (from the stored SizeBytes
/// columns), AI suggestions of the last 30 days and the feature flags; recent jobs; retry of a failed job (audited with the
/// platform actor). Cross-tenant by design: every query ignores the tenant filter and groups by tenant — a fixed number of
/// queries whatever the number of companies.
/// </summary>
public sealed class BuildPlatformObservabilityService
{
    public const string ModuleKey = "build";
    public const int MaxJobs = 200;

    private readonly NexoDbContext _db;
    private readonly IAuditWriter _audit;

    public BuildPlatformObservabilityService(NexoDbContext db, IAuditWriter audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<BuildPlatformOverviewDto> OverviewAsync(CancellationToken ct)
    {
        var subscriptions = await _db.ModuleSubscriptions.IgnoreQueryFilters().AsNoTracking()
            .Where(s => s.ModuleKey == ModuleKey)
            .Select(s => new { s.TenantId, s.Status, s.CreatedAt })
            .ToListAsync(ct);
        var projects = await _db.BldProjects.IgnoreQueryFilters().AsNoTracking()
            .GroupBy(p => new { p.TenantId, p.Status })
            .Select(g => new { g.Key.TenantId, g.Key.Status, Count = g.Count() })
            .ToListAsync(ct);
        var tenantIds = subscriptions.Select(s => s.TenantId).Concat(projects.Select(p => p.TenantId)).Distinct().ToList();
        var names = await _db.Tenants.IgnoreQueryFilters().AsNoTracking().Where(t => tenantIds.Contains(t.Id))
            .Select(t => new { t.Id, t.CompanyName }).ToDictionaryAsync(t => t.Id, t => t.CompanyName, ct);

        var portals = await _db.BldProjectPortals.IgnoreQueryFilters().AsNoTracking().Where(p => p.IsPublished)
            .GroupBy(p => p.TenantId).Select(g => new { TenantId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.TenantId, x => x.Count, ct);
        var versions = await _db.BldModelVersions.IgnoreQueryFilters().AsNoTracking()
            .GroupBy(v => new { v.TenantId, v.Status })
            .Select(g => new { g.Key.TenantId, g.Key.Status, Count = g.Count(), Bytes = g.Sum(v => v.SourceFileKey != null ? v.SizeBytes : 0L) })
            .ToListAsync(ct);
        var jobs = await _db.BldModelJobs.IgnoreQueryFilters().AsNoTracking()
            .Where(j => j.Status == BuildModelJobStatus.Failed || j.Status == BuildModelJobStatus.Pending || j.Status == BuildModelJobStatus.Running)
            .GroupBy(j => new { j.TenantId, j.Status })
            .Select(g => new { g.Key.TenantId, g.Key.Status, Count = g.Count() })
            .ToListAsync(ct);
        var lastErrors = await _db.BldModelJobs.IgnoreQueryFilters().AsNoTracking()
            .Where(j => j.Status == BuildModelJobStatus.Failed && j.Error != null
                        && j.UpdatedAt == _db.BldModelJobs.IgnoreQueryFilters()
                            .Where(x => x.TenantId == j.TenantId && x.Status == BuildModelJobStatus.Failed && x.Error != null)
                            .Max(x => x.UpdatedAt))
            .Select(j => new { j.TenantId, j.Error, j.UpdatedAt })
            .ToListAsync(ct);
        var reality = await _db.BldRealityCaptures.IgnoreQueryFilters().AsNoTracking()
            .GroupBy(r => r.TenantId).Select(g => new { TenantId = g.Key, Bytes = g.Sum(r => r.SizeBytes) })
            .ToDictionaryAsync(x => x.TenantId, x => x.Bytes, ct);
        var documents = await _db.BldDocuments.IgnoreQueryFilters().AsNoTracking()
            .GroupBy(d => d.TenantId).Select(g => new { TenantId = g.Key, Bytes = g.Sum(d => d.SizeBytes) })
            .ToDictionaryAsync(x => x.TenantId, x => x.Bytes, ct);
        var photos = await _db.BldDailyLogPhotos.IgnoreQueryFilters().AsNoTracking()
            .GroupBy(p => p.TenantId).Select(g => new { TenantId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.TenantId, x => x.Count, ct);
        var since = DateTime.UtcNow.AddDays(-30);
        var ai = await _db.BldAiSuggestions.IgnoreQueryFilters().AsNoTracking().Where(s => s.CreatedAt >= since)
            .GroupBy(s => new { s.TenantId, s.Status })
            .Select(g => new
            {
                g.Key.TenantId, g.Key.Status, Count = g.Count(),
                Input = g.Sum(s => (long)s.InputTokens), Output = g.Sum(s => (long)s.OutputTokens), Cost = g.Sum(s => s.CostMicros),
            })
            .ToListAsync(ct);
        var flags = await _db.BldTenantSettings.IgnoreQueryFilters().AsNoTracking()
            .ToDictionaryAsync(s => s.TenantId, ct);

        var tenants = tenantIds.Select(tid =>
        {
            var sub = subscriptions.Where(s => s.TenantId == tid).OrderByDescending(s => s.CreatedAt).FirstOrDefault();
            var tenantProjects = projects.Where(p => p.TenantId == tid).ToList();
            var tenantVersions = versions.Where(v => v.TenantId == tid).ToList();
            var tenantAi = ai.Where(a => a.TenantId == tid).ToList();
            var accepted = tenantAi.Where(a => a.Status is BuildAiSuggestionStatus.Accepted or BuildAiSuggestionStatus.PartiallyAccepted).Sum(a => a.Count);
            var rejected = tenantAi.Where(a => a.Status == BuildAiSuggestionStatus.Rejected).Sum(a => a.Count);
            var modelsBytes = tenantVersions.Sum(v => v.Bytes);
            var realityBytes = reality.GetValueOrDefault(tid);
            var documentBytes = documents.GetValueOrDefault(tid);
            var error = lastErrors.Where(e => e.TenantId == tid).OrderByDescending(e => e.UpdatedAt).FirstOrDefault();
            var f = flags.GetValueOrDefault(tid);
            return new BuildPlatformTenantDto(
                tid, names.GetValueOrDefault(tid) ?? "(empresa removida)", sub?.Status.ToString(),
                tenantProjects.Where(p => p.Status is BuildProjectStatus.Planning or BuildProjectStatus.InProgress or BuildProjectStatus.Paused).Sum(p => p.Count),
                tenantProjects.Sum(p => p.Count),
                portals.GetValueOrDefault(tid),
                tenantVersions.ToDictionary(v => v.Status.ToString(), v => v.Count),
                jobs.Where(j => j.TenantId == tid && j.Status == BuildModelJobStatus.Failed).Sum(j => j.Count),
                jobs.Where(j => j.TenantId == tid && j.Status != BuildModelJobStatus.Failed).Sum(j => j.Count),
                SanitizeError(error?.Error), error is null ? null : BuildScope.Utc(error.UpdatedAt),
                new BuildPlatformStorageDto(modelsBytes, realityBytes, documentBytes, null, photos.GetValueOrDefault(tid),
                    modelsBytes + realityBytes + documentBytes),
                new BuildPlatformAiUsageDto(tenantAi.Sum(a => a.Count), accepted, rejected,
                    accepted + rejected > 0 ? Math.Round((decimal)accepted / (accepted + rejected), 3) : null,
                    tenantAi.Sum(a => a.Input), tenantAi.Sum(a => a.Output), tenantAi.Sum(a => a.Cost)),
                f is null
                    ? new BuildPlatformFlagsDto(BuildTenantSettings.DefaultBuild3D, BuildTenantSettings.DefaultBuildAi,
                        BuildTenantSettings.DefaultBuildVision, BuildTenantSettings.DefaultBuildPredict, false)
                    : new BuildPlatformFlagsDto(f.Build3DEnabled, f.BuildAiEnabled, f.BuildVisionEnabled, f.BuildPredictEnabled, true));
        }).OrderBy(t => t.CompanyName, StringComparer.CurrentCultureIgnoreCase).ToList();

        var totals = new BuildPlatformTotalsDto(tenants.Count, tenants.Sum(t => t.ProjectsActive), tenants.Sum(t => t.ProjectsTotal),
            tenants.Sum(t => t.PublishedPortals), tenants.Sum(t => t.JobsFailed), tenants.Sum(t => t.JobsPending),
            tenants.Sum(t => t.Storage.TotalBytes), tenants.Sum(t => t.Ai30d.Suggestions), tenants.Sum(t => t.Ai30d.CostMicros));
        return new BuildPlatformOverviewDto(DateTimeOffset.UtcNow, tenants, totals);
    }

    /// <summary>Recent processing jobs with the given status (default Failed), newest first.</summary>
    public async Task<IReadOnlyList<BuildPlatformJobDto>> JobsAsync(string? status, int? take, CancellationToken ct)
    {
        var parsed = BuildScope.ParseEnum(status, BuildModelJobStatus.Failed, "Status do job");
        var n = Math.Clamp(take ?? 50, 1, MaxJobs);
        var rows = await (from j in _db.BldModelJobs.IgnoreQueryFilters().AsNoTracking()
                          join v in _db.BldModelVersions.IgnoreQueryFilters().AsNoTracking() on j.VersionId equals v.Id
                          join t in _db.Tenants.IgnoreQueryFilters().AsNoTracking() on j.TenantId equals t.Id into tj
                          from t in tj.DefaultIfEmpty()
                          where j.Status == parsed && v.TenantId == j.TenantId
                          orderby j.UpdatedAt descending
                          select new { Job = j, v.ProjectId, v.Number, v.SourceFormat, CompanyName = t != null ? t.CompanyName : null })
            .Take(n).ToListAsync(ct);
        return rows.Select(r => Map(r.Job, r.CompanyName, r.ProjectId, r.Number, r.SourceFormat)).ToList();
    }

    /// <summary>
    /// Queues a failed job again as a new job of the same kind (the failed one stays as history); a failed version goes back to
    /// Queued. 422 job_not_failed / job_source_missing / job_already_queued. Audited with the platform actor (Warning).
    /// </summary>
    public async Task<BuildPlatformJobDto> RetryAsync(Guid jobId, PlatformActor actor, CancellationToken ct)
    {
        var job = await _db.BldModelJobs.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(j => j.Id == jobId, ct)
                  ?? throw new NotFoundException("BuildModelProcessingJob", jobId);
        if (job.Status != BuildModelJobStatus.Failed)
            throw new DomainException("Só um job que falhou pode ser executado de novo.", "job_not_failed");
        var version = await _db.BldModelVersions.IgnoreQueryFilters()
                          .FirstOrDefaultAsync(v => v.Id == job.VersionId && v.TenantId == job.TenantId, ct)
                      ?? throw new NotFoundException("BuildModelVersion", job.VersionId);
        if (version.SourceFileKey is null)
            throw new DomainException("A versão não tem arquivo armazenado para processar.", "job_source_missing");
        if (await _db.BldModelJobs.IgnoreQueryFilters().AnyAsync(j => j.VersionId == version.Id
                && (j.Status == BuildModelJobStatus.Pending || j.Status == BuildModelJobStatus.Running), ct))
            throw new DomainException("Esta versão já tem um job na fila.", "job_already_queued");

        var retry = BuildModelProcessingJob.Enqueue(job.TenantId, version.Id, job.Kind);
        _db.BldModelJobs.Add(retry);
        if (version.Status == BuildModelVersionStatus.Failed && job.Kind == BuildModelJobKind.Parse)
            version.MarkQueued("Na fila de novo (suporte Orken).");
        var companyName = await _db.Tenants.IgnoreQueryFilters().Where(t => t.Id == job.TenantId).Select(t => t.CompanyName)
            .FirstOrDefaultAsync(ct);
        _audit.Stage(AuditActions.BuildModelJobRetried, AuditSeverity.Warning, "BuildModelProcessingJob", retry.Id.ToString(),
            $"Job de processamento do modelo (versão {version.Number}, {job.Kind}) recolocado na fila pela plataforma.",
            tenantId: job.TenantId, actorId: actor.Id, actorName: actor.Name, actorType: "platform",
            metadata: new { failedJobId = job.Id, versionId = version.Id, kind = job.Kind.ToString(), userAgent = actor.UserAgent, correlationId = actor.RequestId });
        await _db.SaveChangesAsync(ct);
        return Map(retry, companyName, version.ProjectId, version.Number, version.SourceFormat);
    }

    private static BuildPlatformJobDto Map(BuildModelProcessingJob j, string? companyName, Guid projectId, int versionNumber,
        BuildModelSourceFormat format)
        => new(j.Id, j.TenantId, companyName ?? "(empresa removida)", projectId, j.VersionId, versionNumber, format.ToString(), j.Kind.ToString(),
            j.Status.ToString(), j.Attempts, SanitizeError(j.Error), BuildScope.Utc(j.CreatedAt), BuildScope.Utc(j.FinishedAt));

    /// <summary>
    /// The error without anything that could identify a client file: words with a path separator, storage-like keys or a file
    /// extension of the model formats become "[arquivo]". Clipped to 300 characters. No regular expressions.
    /// </summary>
    public static string? SanitizeError(string? error)
    {
        if (string.IsNullOrWhiteSpace(error)) return null;
        var words = error.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Select(w =>
        {
            var bare = w.Trim('\'', '"', '(', ')', '[', ']', ',', ';', ':', '.');
            var lower = bare.ToLowerInvariant();
            var looksLikeFile = bare.Contains('/') || bare.Contains('\\') || lower.StartsWith("private", StringComparison.Ordinal)
                || FileExtensions.Any(x => lower.EndsWith(x, StringComparison.Ordinal));
            return looksLikeFile ? "[arquivo]" : w;
        });
        var clean = string.Join(' ', words);
        return clean.Length > 300 ? clean[..300] + "…" : clean;
    }

    private static readonly string[] FileExtensions =
        [".ifc", ".ifczip", ".glb", ".gltf", ".dxf", ".dwg", ".rvt", ".pdf", ".png", ".jpg", ".jpeg", ".webp", ".zip", ".tmp", ".las", ".e57"];
}
