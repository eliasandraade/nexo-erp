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
/// Reality capture — company side: photos, 360° photos, videos, drone media and point clouds of the site, stored privately
/// (<c>private/tenants/{t}/build/reality/{project}/{id}.&lt;ext&gt;</c>, upload context <c>build-reality</c>), identified by
/// their first bytes, with per-family size limits. Point clouds are accepted and stored, not processed. Hidden from the client
/// portal until marked client-visible.
/// </summary>
public sealed class BuildRealityCaptureService
{
    public const string Folder = "build/reality";
    public const string Context = "build-reality";

    private readonly NexoDbContext _db;
    private readonly ICurrentTenant _tenant;
    private readonly BuildAuditTrail _audit;
    private readonly IStorageProvider _storage;
    private readonly IPrivateFileUrls _urls;
    private readonly IIntegrationFeatureFlags _flags;
    private readonly BuildTwinOptions _options;
    private readonly ILogger<BuildRealityCaptureService> _logger;

    public BuildRealityCaptureService(NexoDbContext db, ICurrentTenant tenant, BuildAuditTrail audit, IStorageProvider storage,
        IPrivateFileUrls urls, IIntegrationFeatureFlags flags, BuildTwinOptions options, ILogger<BuildRealityCaptureService> logger)
    {
        _db = db;
        _tenant = tenant;
        _audit = audit;
        _storage = storage;
        _urls = urls;
        _flags = flags;
        _options = options;
        _logger = logger;
    }

    private Guid Tid => _tenant.Id;

    public sealed record CreateCapture(string Type, string Title, DateTimeOffset? CapturedAt, Guid? StageId, Guid? ZoneId,
        string? ElementExternalId, string? Notes, bool ClientVisible);

    public async Task<BuildRealityCaptureDto> CreateAsync(Guid projectId, CreateCapture r, BuildUploadBuffer file, CancellationToken ct)
    {
        await BuildScope.EnsureProjectAsync(_db, Tid, projectId, ct);
        var type = BuildTwinParse.Enum<BuildRealityCaptureType>(r.Type, "Tipo de captura");
        var kind = BuildCaptureFormats.Of(BuildFileSignatures.Sniff(file.Head));
        if (kind is null || !BuildCaptureFormats.Accepts(type, kind.Family))
            throw new BuildModelFileException($"O conteúdo do arquivo não corresponde ao tipo {type}: envie {BuildCaptureFormats.Expected(type)}.",
                "invalid_file_content");
        var max = _options.MaxCaptureBytesFor(kind.Family);
        if (file.Length > max) throw new BuildFileTooLargeException($"Arquivo grande demais para {type}: o limite é {max / (1024 * 1024)} MB.");
        await ValidateRefsAsync(projectId, r.StageId, r.ZoneId, ct);

        if (!_flags.StorageEnabled) throw new BuildStorageUnavailableException("Armazenamento de arquivos indisponível.");
        var id = Guid.NewGuid();
        var key = StorageKeys.Build(Tid, $"{Folder}/{projectId}", id.ToString("N") + kind.Extension, isPrivate: true);
        try
        {
            await using var stream = file.Open();
            await _storage.UploadAsync(new StorageUploadRequest(stream, "captura" + kind.Extension, kind.ContentType, key, file.Length), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "[Build] Reality capture upload failed — key={Key}", key);
            throw new BuildStorageUnavailableException("Serviço de armazenamento indisponível. Tente novamente.", ex);
        }

        var c = BuildRealityCapture.Create(Tid, projectId, id, type, (r.CapturedAt ?? DateTimeOffset.UtcNow).UtcDateTime, r.Title, key,
            kind.ContentType, file.Length, _audit.UserId);
        c.Describe(r.Title, r.Notes, c.CapturedAt, r.StageId, r.ZoneId, r.ElementExternalId);
        c.SetClientVisible(r.ClientVisible);
        _db.BldRealityCaptures.Add(c);
        _audit.Record(AuditActions.BuildRealityCaptureSaved, "RealityCapture", c.Id, $"Captura da obra enviada ({type}): {c.Title}.",
            new { projectId, type = type.ToString(), file.Length, c.ClientVisible });
        await _db.SaveBuildAsync(ct);
        return Map(c);
    }

    public async Task<IReadOnlyList<BuildRealityCaptureDto>> ListAsync(Guid projectId, string? type, Guid? stageId, Guid? zoneId,
        string? elementExternalId, DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        await BuildScope.EnsureProjectAsync(_db, Tid, projectId, ct);
        var q = _db.BldRealityCaptures.AsNoTracking().Where(c => c.TenantId == Tid && c.ProjectId == projectId);
        if (!string.IsNullOrWhiteSpace(type))
        {
            var t = BuildTwinParse.Enum<BuildRealityCaptureType>(type, "Tipo de captura");
            q = q.Where(c => c.Type == t);
        }
        if (stageId is { } s) q = q.Where(c => c.StageId == s);
        if (zoneId is { } z) q = q.Where(c => c.ZoneId == z);
        if (!string.IsNullOrWhiteSpace(elementExternalId)) q = q.Where(c => c.ElementExternalId == elementExternalId.Trim());
        if (from is { } f) q = q.Where(c => c.CapturedAt >= BrazilMidnightUtc(f));
        if (to is { } t2) q = q.Where(c => c.CapturedAt < BrazilMidnightUtc(t2.AddDays(1)));
        var rows = await q.OrderByDescending(c => c.CapturedAt).Take(500).ToListAsync(ct);
        return rows.Select(Map).ToList();
    }

    public async Task<BuildRealityCaptureDto> GetAsync(Guid id, CancellationToken ct) => Map(await LoadAsync(id, tracking: false, ct));

    public async Task<BuildRealityCaptureDto> UpdateAsync(Guid id, UpdateBuildRealityCaptureRequest r, CancellationToken ct)
    {
        var c = await LoadAsync(id, tracking: true, ct);
        await ValidateRefsAsync(c.ProjectId, r.StageId, r.ZoneId, ct);
        c.Describe(r.Title, r.Notes, r.CapturedAt?.UtcDateTime ?? c.CapturedAt, r.StageId, r.ZoneId, r.ElementExternalId);
        _audit.Record(AuditActions.BuildRealityCaptureSaved, "RealityCapture", c.Id, $"Captura da obra alterada: {c.Title}.");
        await _db.SaveBuildAsync(ct);
        return Map(c);
    }

    public async Task<BuildRealityCaptureDto> SetVisibilityAsync(Guid id, bool visible, CancellationToken ct)
    {
        var c = await LoadAsync(id, tracking: true, ct);
        c.SetClientVisible(visible);
        _audit.Record(AuditActions.BuildRealityCaptureVisibility, "RealityCapture", c.Id,
            visible ? $"Captura visível ao cliente: {c.Title}." : $"Captura ocultada do cliente: {c.Title}.");
        await _db.SaveBuildAsync(ct);
        return Map(c);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var c = await LoadAsync(id, tracking: true, ct);
        _db.BldRealityCaptures.Remove(c);
        _audit.Record(AuditActions.BuildRealityCaptureDeleted, "RealityCapture", c.Id, $"Captura da obra excluída: {c.Title}.");
        await _db.SaveBuildAsync(ct);
        if (_flags.StorageEnabled)
        {
            try { await _storage.DeleteAsync(c.MediaKey, ct); }
            catch (Exception ex) when (ex is not OperationCanceledException) { _logger.LogWarning(ex, "[Build] Could not delete capture file"); }
        }
    }

    private async Task<BuildRealityCapture> LoadAsync(Guid id, bool tracking, CancellationToken ct)
    {
        var q = _db.BldRealityCaptures.Where(c => c.TenantId == Tid && c.Id == id);
        return await (tracking ? q : q.AsNoTracking()).FirstOrDefaultAsync(ct) ?? throw new NotFoundException("BuildRealityCapture", id);
    }

    private async Task ValidateRefsAsync(Guid projectId, Guid? stageId, Guid? zoneId, CancellationToken ct)
    {
        await BuildScope.EnsureStageAsync(_db, Tid, projectId, stageId, ct);
        if (zoneId is { } z && !await _db.BldModelZones.AnyAsync(x => x.TenantId == Tid && x.Id == z && x.ProjectId == projectId, ct))
            throw new DomainException("A zona não é desta obra.", "invalid_zone");
    }

    private static DateTime BrazilMidnightUtc(DateOnly d) => d.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc).AddHours(3);

    private BuildRealityCaptureDto Map(BuildRealityCapture c)
    {
        var url = _urls.For(c.MediaKey);
        return new BuildRealityCaptureDto(c.Id, c.ProjectId, c.Type.ToString(), BuildScope.Utc(c.CapturedAt), c.StageId, c.ZoneId,
            c.ElementExternalId, c.Title, c.Notes, c.ContentType, c.SizeBytes, c.ClientVisible, url,
            url is null ? null : DateTimeOffset.UtcNow.Add(PrivateFileUrls.Ttl), Processed: c.Type != BuildRealityCaptureType.PointCloud,
            BuildScope.Utc(c.CreatedAt));
    }
}
