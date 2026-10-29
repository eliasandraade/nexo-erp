using Microsoft.EntityFrameworkCore;
using Nexo.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;
using Nexo.Application.Modules.TaxIntelligence;
using Nexo.Domain.Exceptions;
using Nexo.Domain.Modules.TaxIntelligence;
using Nexo.Infrastructure.TaxIntelligence.Files;

namespace Nexo.Infrastructure.TaxIntelligence;

/// <summary>A file read back for an authorised download — never a URL.</summary>
public sealed record TaxFileDownload(Stream Content, string ContentType, string FileName, string Sha256);

/// <summary>The metadata of an upload, as given by the person uploading.</summary>
public sealed record TaxUploadMetadata(TaxEvidenceType Type, Guid? RequirementId, string Description, string? DocumentReference, string? Notes);

/// <summary>Platform documents (professional opinions, catalogue sources): private, hashed, revoked on record, downloaded only by the platform.</summary>
public sealed class TaxDocumentService
{
    private const string AuditEntity = "PlatformDocument";
    private readonly TaxContext _ctx;
    private readonly TaxFileStore _files;

    public TaxDocumentService(TaxContext ctx, TaxFileStore files)
    {
        _ctx = ctx;
        _files = files;
    }

    public async Task<TaxStoredFileDto> UploadAsync(string purpose, string? description, Stream content, string? fileName, CancellationToken ct)
    {
        var stored = await _files.AcceptPlatformFileAsync(content, fileName, ct);
        var doc = TaxPlatformDocument.Accept(purpose, stored, description, _ctx.Audit.UserId, _ctx.Audit.UserName);
        _ctx.Db.TaxPlatformDocuments.Add(doc);
        _ctx.Audit.RecordPlatform(TaxAuditActions.PlatformDocumentUploaded, AuditEntity, doc.Id,
            $"Documento ({purpose}) enviado: {doc.OriginalFileName}, SHA-256 {doc.Sha256[..12]}…",
            new { purpose, doc.OriginalFileName, doc.ContentType, doc.SizeBytes, doc.Sha256, scan = doc.ScanStatus.ToString() });
        await _ctx.Db.SaveChangesAsync(ct);
        return Map(doc);
    }

    public async Task<IReadOnlyList<TaxStoredFileDto>> ListAsync(string? purpose, CancellationToken ct)
    {
        var q = _ctx.Db.TaxPlatformDocuments.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(purpose)) q = q.Where(d => d.Purpose == purpose);
        return (await q.OrderByDescending(d => d.CreatedAt).Take(200).ToListAsync(ct)).Select(Map).ToList();
    }

    public async Task<TaxFileDownload> DownloadAsync(Guid id, CancellationToken ct)
    {
        var doc = await _ctx.Db.TaxPlatformDocuments.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id, ct) ?? throw new NotFoundException("Documento", id);
        return new TaxFileDownload(await _files.OpenAsync(doc.StorageKey, ct), doc.ContentType, doc.OriginalFileName, doc.Sha256);
    }

    public async Task<TaxStoredFileDto> RevokeAsync(Guid id, RevokeTaxFileRequest r, CancellationToken ct)
    {
        var doc = await _ctx.Db.TaxPlatformDocuments.FirstOrDefaultAsync(d => d.Id == id, ct) ?? throw new NotFoundException("Documento", id);
        doc.Revoke(r.Reason, _ctx.Audit.UserName, DateTime.UtcNow);
        _ctx.Audit.RecordPlatform(TaxAuditActions.PlatformDocumentRevoked, AuditEntity, doc.Id, $"Documento revogado: {doc.OriginalFileName}. {r.Reason.Trim()}",
            new { reason = r.Reason.Trim(), doc.Sha256 });
        await _ctx.Db.SaveChangesAsync(ct);
        return Map(doc);
    }

    public static TaxStoredFileDto Map(TaxPlatformDocument d) => new(
        d.Id, d.OriginalFileName, d.ContentType, d.SizeBytes, d.Sha256, d.ScanStatus, d.Status, 1, null, d.Description, d.UploadedByName,
        d.CreatedAt, d.RevokedByName, d.RevokedAt, d.RevocationReason, false, null);
}

/// <summary>
/// A company's private evidence files attached to credits, compliance assessments and split settlements (opportunity
/// evidence goes through <see cref="TaxOpportunityEvidenceService"/>). Every upload, revocation, legal hold and download is
/// audited; files of another tenant are simply not found.
/// </summary>
public sealed class TaxEvidenceService
{
    private const string AuditEntity = "EvidenceFile";
    private readonly TaxContext _ctx;
    private readonly TaxFileStore _files;
    private readonly ILogger<TaxEvidenceService> _log;

    public TaxEvidenceService(TaxContext ctx, TaxFileStore files, ILogger<TaxEvidenceService> log)
    {
        _ctx = ctx;
        _files = files;
        _log = log;
    }

    public TaxFileStorageStatusDto StorageStatus() => new(_files.State == TaxFileStorageState.Available, _files.State.ToString(),
        _files.State == TaxFileStorageState.Available ? "Armazenamento privado disponível." : TaxFileStore.UnavailableMessage(_files.State),
        _files.MaxBytes, TaxFileInspector.AcceptedExtensions, _files.ScannerConfigured);

    /// <summary>Uploads a file for a credit, a compliance assessment or a split settlement of this company.</summary>
    public async Task<TaxEvidenceFileDto> UploadAsync(TaxFileOwner owner, Guid ownerId, TaxEvidenceType type, string? description,
        Stream content, string? fileName, CancellationToken ct)
    {
        if (owner == TaxFileOwner.OpportunityEvidence)
            throw new DomainException("Evidência de oportunidade é enviada pela própria oportunidade.", "tax_file_owner");
        await EnsureOwnerAsync(owner, ownerId, ct);
        var file = await AcceptAsync(owner, ownerId, null, type, description, content, fileName, null, ct);
        await _ctx.Db.SaveChangesAsync(ct);
        return Map(file);
    }

    /// <summary>Stores the bytes and stages the file record and its audit (the caller saves). Refusals are counted.</summary>
    internal async Task<TaxEvidenceFile> AcceptAsync(TaxFileOwner owner, Guid ownerId, Guid? requirementId, TaxEvidenceType type, string? description,
        Stream content, string? fileName, TaxEvidenceFile? replaces, CancellationToken ct)
    {
        TaxStoredContent stored;
        try
        {
            stored = await _files.AcceptTenantFileAsync(_ctx.Tenant.Id, content, fileName, ct);
        }
        catch (DomainException ex)
        {
            _ctx.Metrics.EvidenceUpload(ex.Code ?? "refused");
            _log.LogInformation("Tax evidence upload refused: {Code}", ex.Code);
            throw;
        }
        var file = TaxEvidenceFile.Accept(_ctx.Tenant.Id, owner, ownerId, requirementId, type, stored, description,
            _ctx.Audit.UserId, _ctx.Audit.UserName, replaces);
        _ctx.Db.TaxEvidenceFiles.Add(file);
        _ctx.Audit.Record(TaxAuditActions.EvidenceFileUploaded, AuditEntity, file.Id,
            $"Arquivo de evidência enviado: {file.OriginalFileName} ({owner}), SHA-256 {file.Sha256[..12]}…",
            new { owner = owner.ToString(), ownerId, requirementId, type = type.ToString(), file.ContentType, file.SizeBytes, file.Sha256,
                scan = file.ScanStatus.ToString(), version = file.Version, replaces = replaces?.Id });
        _ctx.Metrics.EvidenceUpload("accepted");
        return file;
    }

    public async Task<IReadOnlyList<TaxEvidenceFileDto>> ListAsync(TaxFileOwner owner, Guid ownerId, CancellationToken ct)
        => (await _ctx.Db.TaxEvidenceFiles.AsNoTracking().Where(f => f.Owner == owner && f.OwnerId == ownerId)
                .OrderBy(f => f.CreatedAt).ToListAsync(ct)).Select(Map).ToList();

    /// <summary>
    /// The bytes of a file of this company, for an authorised user — checked on every download, audited. A revoked file stays
    /// downloadable only by the board (audit of what was withdrawn).
    /// </summary>
    public async Task<TaxFileDownload> DownloadAsync(Guid id, bool canReadRevoked, CancellationToken ct)
    {
        var file = await _ctx.Db.TaxEvidenceFiles.AsNoTracking().FirstOrDefaultAsync(f => f.Id == id, ct) ?? throw new NotFoundException("Arquivo", id);
        if (file.Status == TaxFileStatus.Revoked && !canReadRevoked)
            throw new ForbiddenException("Este arquivo foi revogado; só a diretoria pode baixá-lo para auditoria.");
        var content = await _files.OpenAsync(file.StorageKey, ct);
        _ctx.Audit.Record(TaxAuditActions.EvidenceFileDownloaded, AuditEntity, file.Id, $"Arquivo baixado: {file.OriginalFileName}.",
            new { file.Sha256, status = file.Status.ToString() });
        await _ctx.Db.SaveChangesAsync(ct);
        return new TaxFileDownload(content, file.ContentType, file.OriginalFileName, file.Sha256);
    }

    public async Task<TaxEvidenceFileDto> RevokeAsync(Guid id, RevokeTaxFileRequest r, CancellationToken ct)
    {
        var file = await _ctx.Db.TaxEvidenceFiles.FirstOrDefaultAsync(f => f.Id == id, ct) ?? throw new NotFoundException("Arquivo", id);
        if (file.Owner == TaxFileOwner.OpportunityEvidence)
            throw new DomainException("Revogue a evidência pela oportunidade: o arquivo acompanha a evidência.", "tax_file_owner");
        file.Revoke(r.Reason, _ctx.Audit.UserId, _ctx.Audit.UserName, DateTime.UtcNow);
        _ctx.Audit.Record(TaxAuditActions.EvidenceFileRevoked, AuditEntity, file.Id, $"Arquivo revogado: {file.OriginalFileName}. {r.Reason.Trim()}",
            new { reason = r.Reason.Trim(), file.Sha256 }, AuditSeverity.Warning);
        await _ctx.Db.SaveChangesAsync(ct);
        return Map(file);
    }

    public async Task<TaxEvidenceFileDto> SetLegalHoldAsync(Guid id, SetLegalHoldRequest r, CancellationToken ct)
    {
        var file = await _ctx.Db.TaxEvidenceFiles.FirstOrDefaultAsync(f => f.Id == id, ct) ?? throw new NotFoundException("Arquivo", id);
        file.SetLegalHold(r.Hold, r.Reason, _ctx.Audit.UserName, DateTime.UtcNow);
        _ctx.Audit.Record(TaxAuditActions.EvidenceFileLegalHold, AuditEntity, file.Id,
            $"Retenção legal {(r.Hold ? "aplicada" : "retirada")}: {file.OriginalFileName}. {r.Reason.Trim()}",
            new { hold = r.Hold, reason = r.Reason.Trim() }, AuditSeverity.Warning);
        await _ctx.Db.SaveChangesAsync(ct);
        return Map(file);
    }

    private async Task EnsureOwnerAsync(TaxFileOwner owner, Guid ownerId, CancellationToken ct)
    {
        var exists = owner switch
        {
            TaxFileOwner.Credit => await _ctx.Db.TaxCredits.AnyAsync(c => c.Id == ownerId, ct),
            TaxFileOwner.ComplianceAssessment => await _ctx.Db.TaxComplianceAssessments.AnyAsync(c => c.Id == ownerId, ct),
            TaxFileOwner.SplitSettlement => await _ctx.Db.TaxSplitSettlements.AnyAsync(s => s.Id == ownerId, ct),
            _ => false,
        };
        if (!exists) throw new NotFoundException(owner.ToString(), ownerId);
    }

    public static TaxStoredFileDto MapFile(TaxEvidenceFile f) => new(
        f.Id, f.OriginalFileName, f.ContentType, f.SizeBytes, f.Sha256, f.ScanStatus, f.Status, f.Version, f.ReplacesFileId, f.Description,
        f.UploadedByName, f.CreatedAt, f.RevokedByName, f.RevokedAt, f.RevocationReason, f.LegalHold, f.LegalHoldReason);

    public static TaxEvidenceFileDto Map(TaxEvidenceFile f) => new(MapFile(f), f.Owner, f.OwnerId, f.RequirementId, f.EvidenceType);
}

/// <summary>
/// Evidence of an opportunity with a private file: upload (hash of the bytes), a newer version that supersedes the old one,
/// revocation — under the opportunity's row lock, audited. Superseded and revoked versions stay listed; a decision that
/// already froze a version keeps it in its dossier, unchanged.
/// </summary>
public sealed class TaxOpportunityEvidenceService
{
    private const string AuditEntity = "Opportunity";
    private readonly TaxContext _ctx;
    private readonly TaxOpportunityService _opportunities;
    private readonly TaxEvidenceService _evidence;

    public TaxOpportunityEvidenceService(TaxContext ctx, TaxOpportunityService opportunities, TaxEvidenceService evidence)
    {
        _ctx = ctx;
        _opportunities = opportunities;
        _evidence = evidence;
    }

    public async Task<TaxOpportunityDto> UploadAsync(Guid opportunityId, TaxUploadMetadata m, Stream content, string? fileName, CancellationToken ct)
    {
        var o = await _opportunities.LoadForUpdateAsync(opportunityId, ct);
        var file = await _evidence.AcceptAsync(TaxFileOwner.OpportunityEvidence, o.Id, m.RequirementId, m.Type, m.Description, content, fileName, null, ct);
        var e = o.AddEvidence(m.Type, m.RequirementId, m.Description, m.DocumentReference, file.Sha256, m.Notes, _ctx.Audit.UserId, _ctx.Audit.UserName, file);
        _ctx.Db.TaxOpportunityEvidence.Add(e);
        _ctx.Audit.Record(TaxAuditActions.OpportunityEvidenceAdded, AuditEntity, o.Id, $"Evidência anexada com arquivo: {file.OriginalFileName}.",
            new { evidenceId = e.Id, fileId = file.Id, type = m.Type.ToString(), m.RequirementId, hash = file.Sha256, hashKind = e.HashKind });
        await _opportunities.SaveAsync(ct);
        return await _opportunities.GetAsync(opportunityId, ct);
    }

    /// <summary>A newer version of a piece of evidence — with a new file, or only new metadata (the file, if any, is kept).</summary>
    public async Task<TaxOpportunityDto> ReplaceAsync(Guid opportunityId, Guid evidenceId, ReplaceEvidenceRequest r, Stream? content, string? fileName,
        CancellationToken ct)
    {
        var o = await _opportunities.LoadForUpdateAsync(opportunityId, ct);
        var old = o.Evidence.FirstOrDefault(x => x.Id == evidenceId) ?? throw new NotFoundException("Evidência", evidenceId);
        var oldFile = old.FileId is null ? null : await _ctx.Db.TaxEvidenceFiles.FirstOrDefaultAsync(f => f.Id == old.FileId, ct);
        TaxEvidenceFile? file = null;
        if (content is not null)
            file = await _evidence.AcceptAsync(TaxFileOwner.OpportunityEvidence, o.Id, old.RequirementId, old.Type, r.Description, content, fileName,
                oldFile is { Status: TaxFileStatus.Active } ? oldFile : null, ct);
        else if (oldFile is { Status: TaxFileStatus.Active })
            file = oldFile;
        var hash = file?.Sha256 ?? _opportunities.ReferenceHash(o, old.Type, old.RequirementId, r.Description, r.DocumentReference, r.Notes);
        var e = o.ReplaceEvidence(evidenceId, r.Description, r.DocumentReference, hash, r.Notes, _ctx.Audit.UserId, _ctx.Audit.UserName, file);
        _ctx.Db.TaxOpportunityEvidence.Add(e);
        _ctx.Audit.Record(TaxAuditActions.OpportunityEvidenceReplaced, AuditEntity, o.Id, $"Evidência substituída (versão {e.Version}).",
            new { previousEvidenceId = evidenceId, evidenceId = e.Id, fileId = file?.Id, hash, hashKind = e.HashKind });
        await _opportunities.SaveAsync(ct);
        return await _opportunities.GetAsync(opportunityId, ct);
    }

    public async Task<TaxOpportunityDto> RevokeAsync(Guid opportunityId, Guid evidenceId, RevokeEvidenceRequest r, CancellationToken ct)
    {
        var o = await _opportunities.LoadForUpdateAsync(opportunityId, ct);
        var e = o.Evidence.FirstOrDefault(x => x.Id == evidenceId) ?? throw new NotFoundException("Evidência", evidenceId);
        o.RevokeEvidence(evidenceId, r.Reason, _ctx.Audit.UserId, _ctx.Audit.UserName, DateTime.UtcNow);
        if (e.FileId is { } fileId && await _ctx.Db.TaxEvidenceFiles.FirstOrDefaultAsync(f => f.Id == fileId, ct) is { Status: TaxFileStatus.Active } file)
            file.Revoke(r.Reason, _ctx.Audit.UserId, _ctx.Audit.UserName, DateTime.UtcNow);
        _ctx.Audit.Record(TaxAuditActions.OpportunityEvidenceRevoked, AuditEntity, o.Id, $"Evidência revogada: {r.Reason.Trim()}",
            new { evidenceId, fileId = e.FileId, reason = r.Reason.Trim() }, AuditSeverity.Warning);
        await _opportunities.SaveAsync(ct);
        return await _opportunities.GetAsync(opportunityId, ct);
    }
}
