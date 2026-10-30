using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Nexo.Application.Common;
using Nexo.Application.Modules.Build;
using Nexo.Domain.Modules.Build;

namespace Nexo.Infrastructure.Modules.Build;

/// <summary>
/// Portal v1.3 — digital twin (flag <c>model3d</c>) and reality capture (flag <c>realityCapture</c>). Read-only.
/// The client only ever receives: the viewer derivative (GLB) as a signed URL valid ~15 minutes — never the source model file,
/// never a storage key —, zones (names, kinds, simplified boxes typed by the company; no internal notes), the compact state
/// map keyed by external ids, and client-visible captures as signed URLs.
/// </summary>
public sealed partial class BuildPublicPortalService
{
    public const string CodeInvalidDate = "invalid_date";

    private async Task<bool> Build3DEnabledAsync(Guid tenantId, CancellationToken ct)
        => await _db.BldTenantSettings.AsNoTracking().Where(s => s.TenantId == tenantId)
               .Select(s => (bool?)s.Build3DEnabled).FirstOrDefaultAsync(ct)
           ?? BuildTenantSettings.DefaultBuild3D;

    public async Task<PublicBuildTwinDto> GetTwinAsync(string companySlug, string projectSlug, string? token, CancellationToken ct)
    {
        var ctx = await ResolveAsync(companySlug, projectSlug, tracking: false, ct);
        await EnsureAccessAsync(ctx, token, ct);
        if (!ctx.Visibility.Model3d) throw BuildPortalAccessException.SectionDisabled();
        var today = BusinessClock.Today();

        var primary = await _twin.PrimaryVersionAsync(ctx.TenantId, ctx.Project.Id, bypass: true, ct);
        var zones = await TwinZonesAsync(ctx, primary?.Id, ct);
        var geometry = BuildTwinService.GeometryOf(primary, zones.Any(z => z.SimplifiedBox is not null));
        string? modelName = null;
        string? discipline = null;
        if (primary is not null)
        {
            var model = await _db.BldModels.IgnoreQueryFilters().AsNoTracking()
                .FirstOrDefaultAsync(m => m.TenantId == ctx.TenantId && m.Id == primary.ModelId, ct);
            modelName = model?.Name;
            discipline = model?.Discipline.ToString();
        }

        // only the derivative made for viewing; the source of an IFC/DXF/PDF is never signed here
        var viewerUrl = primary?.ViewerFileKey is { } key ? _media.SignedUrl(key, ctx.TenantId, BuildModelService.Folder) : null;
        return new PublicBuildTwinDto
        {
            GeometrySource = geometry?.ToString(),
            ModelName = modelName,
            Discipline = discipline,
            ViewerUrl = viewerUrl,
            ViewerExpiresAt = viewerUrl is null ? null : DateTimeOffset.UtcNow.Add(BuildPortalMedia.SignedUrlTtl),
            Zones = zones,
            State = await TwinStateAsync(ctx, primary?.Id, today, ct),
            MinDate = ctx.Project.StartDate,
            MaxDate = today,
            Disclaimer = BuildTwinTexts.Disclaimer(geometry),
        };
    }

    /// <summary>The state map on a date (time slider): yyyy-MM-dd, not after today nor before the project start.</summary>
    public async Task<PublicBuildTwinStateDto> GetTwinStateAsync(string companySlug, string projectSlug, string? token, DateOnly? date,
        CancellationToken ct)
    {
        var ctx = await ResolveAsync(companySlug, projectSlug, tracking: false, ct);
        await EnsureAccessAsync(ctx, token, ct);
        if (!ctx.Visibility.Model3d) throw BuildPortalAccessException.SectionDisabled();
        var today = BusinessClock.Today();
        var d = date ?? today;
        if (d > today || (ctx.Project.StartDate is { } start && d < start))
            throw new BuildPortalAccessException(400, CodeInvalidDate, "Data fora do período da obra (do início até hoje).");
        var primary = await _twin.PrimaryVersionAsync(ctx.TenantId, ctx.Project.Id, bypass: true, ct);
        return await TwinStateAsync(ctx, primary?.Id, d, ct);
    }

    public async Task<PublicBuildRealityCapturesDto> GetRealityCapturesAsync(string companySlug, string projectSlug, string? token,
        int? skip, int? take, CancellationToken ct)
    {
        var ctx = await ResolveAsync(companySlug, projectSlug, tracking: false, ct);
        await EnsureAccessAsync(ctx, token, ct);
        if (!ctx.Visibility.RealityCapture) throw BuildPortalAccessException.SectionDisabled();
        var s = Math.Max(0, skip ?? 0);
        var n = Math.Clamp(take ?? 24, 1, 50);
        var tid = ctx.TenantId;
        var pid = ctx.Project.Id;
        var q = _db.BldRealityCaptures.IgnoreQueryFilters().AsNoTracking()
            .Where(c => c.TenantId == tid && c.ProjectId == pid && c.ClientVisible);
        var total = await q.CountAsync(ct);
        var rows = await q.OrderByDescending(c => c.CapturedAt).Skip(s).Take(n)
            .Select(c => new
            {
                c.Type, c.CapturedAt, c.Title, c.ContentType, c.MediaKey,
                StageName = _db.BldStages.IgnoreQueryFilters().Where(st => st.TenantId == tid && st.Id == c.StageId).Select(st => st.Name).FirstOrDefault(),
                ZoneName = _db.BldModelZones.IgnoreQueryFilters().Where(z => z.TenantId == tid && z.Id == c.ZoneId).Select(z => z.Name).FirstOrDefault(),
            })
            .ToListAsync(ct);
        var expires = DateTimeOffset.UtcNow.Add(BuildPortalMedia.SignedUrlTtl);
        return new PublicBuildRealityCapturesDto(rows.Select(r =>
        {
            var url = _media.SignedUrl(r.MediaKey, tid, BuildRealityCaptureService.Folder);
            return new PublicBuildRealityCaptureDto(r.Type.ToString(), Utc(r.CapturedAt), r.Title, r.StageName, r.ZoneName, r.ContentType,
                url, url is null ? null : expires);
        }).ToList(), total, s, n);
    }

    private async Task<IReadOnlyList<PublicBuildTwinZoneDto>> TwinZonesAsync(Ctx ctx, Guid? versionId, CancellationToken ct)
    {
        var zones = await _db.BldModelZones.IgnoreQueryFilters().AsNoTracking()
            .Where(z => z.TenantId == ctx.TenantId && z.ProjectId == ctx.Project.Id && (z.ModelVersionId == null || z.ModelVersionId == versionId))
            .OrderBy(z => z.ModelVersionId == null).ThenBy(z => z.Order).ThenBy(z => z.Name)
            .ToListAsync(ct);
        var ids = zones.ToDictionary(z => z.Id, PublicZoneId);
        return zones.Select(z => new PublicBuildTwinZoneDto(ids[z.Id], z.Kind.ToString(), z.Name,
            z.ParentZoneId is { } p && ids.TryGetValue(p, out var pid) ? pid : null, z.Elevation, z.Order, z.Box(), z.ExternalId)).ToList();
    }

    private async Task<PublicBuildTwinStateDto> TwinStateAsync(Ctx ctx, Guid? versionId, DateOnly date, CancellationToken ct)
    {
        var r = await _twin.ComputeAsync(ctx.TenantId, ctx.Project.Id, versionId, date, bypass: true, ct);
        var publicIds = await _db.BldModelZones.IgnoreQueryFilters().AsNoTracking()
            .Where(z => z.TenantId == ctx.TenantId && z.ProjectId == ctx.Project.Id && (z.ModelVersionId == null || z.ModelVersionId == versionId))
            .ToListAsync(ct);
        var map = publicIds.ToDictionary(z => z.Id, PublicZoneId);
        return new PublicBuildTwinStateDto(r.Date, r.Counts, r.States,
            r.ZoneStates.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<string>)kv.Value.Where(map.ContainsKey).Select(id => map[id]).ToList()),
            r.Unbound);
    }

    /// <summary>A zone as the portal knows it: its model id (IFC GlobalId) or an opaque id derived from the internal one.</summary>
    private static string PublicZoneId(BuildModelZone z)
        => z.ExternalId ?? "z-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(z.Id.ToString("N"))))[..16].ToLowerInvariant();
}
