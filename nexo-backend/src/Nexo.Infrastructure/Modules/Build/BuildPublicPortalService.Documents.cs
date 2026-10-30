using Microsoft.EntityFrameworkCore;
using Nexo.Application.Modules.Build;

namespace Nexo.Infrastructure.Modules.Build;

/// <summary>
/// Portal v1.4 — project documents (flag <c>documents</c>). Only documents the company marked client-visible, as signed URLs
/// (~15 min). Never the storage key, the hash, who uploaded it nor what the AI extracted.
/// </summary>
public sealed partial class BuildPublicPortalService
{
    public async Task<PublicBuildDocumentsDto> GetDocumentsAsync(string companySlug, string projectSlug, string? token, CancellationToken ct)
    {
        var ctx = await ResolveAsync(companySlug, projectSlug, tracking: false, ct);
        await EnsureAccessAsync(ctx, token, ct);
        if (!ctx.Visibility.Documents) throw BuildPortalAccessException.SectionDisabled();
        var tid = ctx.TenantId;
        var rows = await _db.BldDocuments.IgnoreQueryFilters().AsNoTracking()
            .Where(d => d.TenantId == tid && d.ProjectId == ctx.Project.Id && d.ClientVisible)
            .OrderBy(d => d.Category).ThenByDescending(d => d.CreatedAt).Take(500)
            .Select(d => new { d.Title, d.Category, d.FileName, d.ContentType, d.SizeBytes, d.ValidUntil, d.FileKey, d.CreatedAt })
            .ToListAsync(ct);
        var expires = DateTimeOffset.UtcNow.Add(BuildPortalMedia.SignedUrlTtl);
        return new PublicBuildDocumentsDto(rows.Select(r =>
        {
            var url = _media.SignedUrl(r.FileKey, tid);
            return new PublicBuildDocumentDto(r.Title, r.Category.ToString(), r.FileName, r.ContentType, r.SizeBytes, r.ValidUntil, url,
                url is null ? null : expires, Utc(r.CreatedAt));
        }).ToList());
    }
}
