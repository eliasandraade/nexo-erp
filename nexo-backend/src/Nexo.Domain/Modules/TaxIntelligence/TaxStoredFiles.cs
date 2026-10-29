using Nexo.Domain.Common;
using Nexo.Domain.Exceptions;

namespace Nexo.Domain.Modules.TaxIntelligence;

/// <summary>What the file store measured and where it put the bytes — set once, when the file is accepted.</summary>
public sealed record TaxStoredContent(string StorageKey, string OriginalFileName, string ContentType, string Extension, long SizeBytes,
    string Sha256, TaxFileScanStatus ScanStatus, string? ScanEngine);

/// <summary>
/// A private file of a company's tax evidence (a contract, an authorised NF-e XML, a report…). The bytes live in
/// private storage under an internal key; the original name is metadata only and never becomes a path. The hash
/// is the SHA-256 of the bytes actually received. A file never changes: a newer version replaces it, or it is
/// revoked — both audited, both kept. Nothing is ever deleted automatically; a legal hold blocks any disposal.
/// </summary>
public class TaxEvidenceFile : StoreEntity
{
    private TaxEvidenceFile() { }   // EF Core
    private TaxEvidenceFile(Guid tenantId) : base(tenantId) { }

    public TaxFileOwner Owner { get; private set; }
    public Guid OwnerId { get; private set; }
    /// <summary>Requirement it supports, for opportunity evidence.</summary>
    public Guid? RequirementId { get; private set; }
    public TaxEvidenceType EvidenceType { get; private set; }
    public string StorageKey { get; private set; } = string.Empty;
    public string OriginalFileName { get; private set; } = string.Empty;
    public string ContentType { get; private set; } = string.Empty;
    public string Extension { get; private set; } = string.Empty;
    public long SizeBytes { get; private set; }
    /// <summary>SHA-256 (hex, lower case) of the stored bytes.</summary>
    public string Sha256 { get; private set; } = string.Empty;
    public TaxFileScanStatus ScanStatus { get; private set; }
    public string? ScanEngine { get; private set; }
    public string? Description { get; private set; }
    public int Version { get; private set; } = 1;
    public Guid? ReplacesFileId { get; private set; }
    public Guid? ReplacedByFileId { get; private set; }
    public TaxFileStatus Status { get; private set; } = TaxFileStatus.Active;
    public Guid UploadedById { get; private set; }
    public string UploadedByName { get; private set; } = string.Empty;
    public DateTime? RevokedAt { get; private set; }
    public Guid? RevokedById { get; private set; }
    public string? RevokedByName { get; private set; }
    public string? RevocationReason { get; private set; }
    public bool LegalHold { get; private set; }
    public string? LegalHoldReason { get; private set; }
    public DateTime? LegalHoldChangedAt { get; private set; }
    public string? LegalHoldChangedByName { get; private set; }

    public static TaxEvidenceFile Accept(Guid tenantId, TaxFileOwner owner, Guid ownerId, Guid? requirementId, TaxEvidenceType type,
        TaxStoredContent content, string? description, Guid byId, string byName, TaxEvidenceFile? replaces = null)
    {
        if (ownerId == Guid.Empty) throw new DomainException("Diga a que o arquivo pertence.", "tax_file_owner");
        if (content.SizeBytes <= 0) throw new DomainException("Arquivo vazio.", "tax_file_empty");
        if (content.Sha256.Length != 64 || !content.Sha256.All(Uri.IsHexDigit))
            throw new DomainException("Hash do arquivo inválido.", "tax_file_hash");
        if (content.ScanStatus == TaxFileScanStatus.Rejected)
            throw new DomainException("O arquivo foi recusado pela verificação de conteúdo.", "tax_file_scan_rejected");
        if (replaces is not null && (replaces.Owner != owner || replaces.OwnerId != ownerId || replaces.Status != TaxFileStatus.Active))
            throw new DomainException("Só um arquivo ativo do mesmo registro pode ser substituído.", "tax_file_replace");
        var file = new TaxEvidenceFile(tenantId)
        {
            Owner = owner,
            OwnerId = ownerId,
            RequirementId = requirementId,
            EvidenceType = type,
            StorageKey = content.StorageKey,
            OriginalFileName = content.OriginalFileName,
            ContentType = content.ContentType,
            Extension = content.Extension,
            SizeBytes = content.SizeBytes,
            Sha256 = content.Sha256,
            ScanStatus = content.ScanStatus,
            ScanEngine = content.ScanEngine,
            Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            Version = replaces is null ? 1 : replaces.Version + 1,
            ReplacesFileId = replaces?.Id,
            UploadedById = byId,
            UploadedByName = byName,
        };
        if (replaces is not null)
        {
            replaces.Status = TaxFileStatus.Replaced;
            replaces.ReplacedByFileId = file.Id;
            replaces.SetUpdatedAt();
        }
        return file;
    }

    /// <summary>Withdraws the file on record (wrong file, wrong operation…). The bytes and the record stay for the audit trail.</summary>
    public void Revoke(string reason, Guid byId, string byName, DateTime atUtc)
    {
        if (Status == TaxFileStatus.Revoked) throw new DomainException("O arquivo já foi revogado.", "tax_file_state");
        if (string.IsNullOrWhiteSpace(reason)) throw new DomainException("Diga por que o arquivo foi revogado.", "tax_file_reason");
        Status = TaxFileStatus.Revoked;
        RevokedAt = atUtc;
        RevokedById = byId;
        RevokedByName = byName;
        RevocationReason = reason.Trim();
        SetUpdatedAt();
    }

    /// <summary>Places or lifts a legal hold. While held, no retention policy makes the file eligible for disposal.</summary>
    public void SetLegalHold(bool hold, string reason, string byName, DateTime atUtc)
    {
        if (string.IsNullOrWhiteSpace(reason)) throw new DomainException("Diga o motivo da retenção legal.", "tax_file_hold_reason");
        LegalHold = hold;
        LegalHoldReason = reason.Trim();
        LegalHoldChangedAt = atUtc;
        LegalHoldChangedByName = byName;
        SetUpdatedAt();
    }
}

/// <summary>
/// A private document kept by the platform (no tenant): the professional opinion or technical note behind a
/// professional review, the official file a classification catalogue was imported from. Same storage rules as
/// <see cref="TaxEvidenceFile"/>: internal key, SHA-256 of the bytes, never changed, revoked on record.
/// </summary>
public class TaxPlatformDocument : BaseEntity
{
    private TaxPlatformDocument() { }   // EF Core

    public string Purpose { get; private set; } = string.Empty;
    public string StorageKey { get; private set; } = string.Empty;
    public string OriginalFileName { get; private set; } = string.Empty;
    public string ContentType { get; private set; } = string.Empty;
    public string Extension { get; private set; } = string.Empty;
    public long SizeBytes { get; private set; }
    public string Sha256 { get; private set; } = string.Empty;
    public TaxFileScanStatus ScanStatus { get; private set; }
    public string? Description { get; private set; }
    public TaxFileStatus Status { get; private set; } = TaxFileStatus.Active;
    public Guid UploadedById { get; private set; }
    public string UploadedByName { get; private set; } = string.Empty;
    public DateTime? RevokedAt { get; private set; }
    public string? RevokedByName { get; private set; }
    public string? RevocationReason { get; private set; }

    public const string ProfessionalReviewPurpose = "professional-review";
    public const string ClassificationSourcePurpose = "classification-source";

    public static TaxPlatformDocument Accept(string purpose, TaxStoredContent content, string? description, Guid byId, string byName)
    {
        if (purpose is not (ProfessionalReviewPurpose or ClassificationSourcePurpose))
            throw new DomainException("Finalidade do documento desconhecida.", "tax_document_purpose");
        if (content.SizeBytes <= 0) throw new DomainException("Arquivo vazio.", "tax_file_empty");
        if (content.ScanStatus == TaxFileScanStatus.Rejected)
            throw new DomainException("O arquivo foi recusado pela verificação de conteúdo.", "tax_file_scan_rejected");
        return new TaxPlatformDocument
        {
            Purpose = purpose,
            StorageKey = content.StorageKey,
            OriginalFileName = content.OriginalFileName,
            ContentType = content.ContentType,
            Extension = content.Extension,
            SizeBytes = content.SizeBytes,
            Sha256 = content.Sha256,
            ScanStatus = content.ScanStatus,
            Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            UploadedById = byId,
            UploadedByName = byName,
        };
    }

    public void Revoke(string reason, string byName, DateTime atUtc)
    {
        if (Status == TaxFileStatus.Revoked) throw new DomainException("O documento já foi revogado.", "tax_file_state");
        if (string.IsNullOrWhiteSpace(reason)) throw new DomainException("Diga por que o documento foi revogado.", "tax_file_reason");
        Status = TaxFileStatus.Revoked;
        RevokedAt = atUtc;
        RevokedByName = byName;
        RevocationReason = reason.Trim();
        SetUpdatedAt();
    }
}
