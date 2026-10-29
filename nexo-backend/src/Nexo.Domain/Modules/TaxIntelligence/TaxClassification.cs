using Nexo.Domain.Common;
using Nexo.Domain.Exceptions;

namespace Nexo.Domain.Modules.TaxIntelligence;

/// <summary>
/// One code of the tax classification catalogue (platform-wide): cClassTrib, NCM or NBS, with its description,
/// the source it was taken from and its validity. The software never invents codes: they enter by import from a
/// documented source, start as Draft and are activated explicitly (audited). The coverage matrix only accepts a
/// code that is Active for the date — and, in the definitive mode, professionally approved. Anything else fails
/// closed as "requires classification". A code's text is never edited: a new validity is a new row.
/// </summary>
public class TaxClassificationCode : BaseEntity
{
    private TaxClassificationCode() { }   // EF Core

    public TaxClassificationKind Kind { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    /// <summary>Where it was taken from (official table, version), as documented by whoever imported it.</summary>
    public string Source { get; private set; } = string.Empty;
    public string? SourceVersion { get; private set; }
    public DateOnly ValidFrom { get; private set; }
    public DateOnly? ValidTo { get; private set; }
    public TaxClassificationStatus Status { get; private set; } = TaxClassificationStatus.Draft;
    public ProfessionalReviewStatus ProfessionalReview { get; private set; } = ProfessionalReviewStatus.Pending;
    public string? ProfessionalReviewNotes { get; private set; }
    public string? Notes { get; private set; }
    public Guid? ImportBatchId { get; private set; }
    public Guid? SourceDocumentId { get; private set; }
    public string CreatedByName { get; private set; } = string.Empty;
    public string? StatusChangedByName { get; private set; }
    public DateTime? StatusChangedAt { get; private set; }

    public static TaxClassificationCode Import(TaxClassificationKind kind, string code, string description, string source, string? sourceVersion,
        DateOnly validFrom, DateOnly? validTo, string? notes, Guid? batchId, Guid? sourceDocumentId, string byName)
    {
        var normalized = Normalize(kind, code);
        if (string.IsNullOrWhiteSpace(description)) throw new DomainException($"Código {normalized} sem descrição.", "tax_classification_description");
        if (string.IsNullOrWhiteSpace(source)) throw new DomainException("Informe a fonte do catálogo importado.", "tax_classification_source");
        if (validTo is not null && validTo < validFrom)
            throw new DomainException($"Código {normalized}: o fim da vigência é anterior ao início.", "tax_classification_validity");
        return new TaxClassificationCode
        {
            Kind = kind,
            Code = normalized,
            Description = description.Trim(),
            Source = source.Trim(),
            SourceVersion = string.IsNullOrWhiteSpace(sourceVersion) ? null : sourceVersion.Trim(),
            ValidFrom = validFrom,
            ValidTo = validTo,
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
            ImportBatchId = batchId,
            SourceDocumentId = sourceDocumentId,
            CreatedByName = byName,
        };
    }

    /// <summary>
    /// The form a code is stored and compared in. Formats are checked, never interpreted: cClassTrib up to 10
    /// letters or digits, NCM 8 digits, NBS up to 12 digits (dots removed).
    /// </summary>
    public static string Normalize(TaxClassificationKind kind, string? code)
    {
        var c = (code ?? string.Empty).Trim().Replace(".", string.Empty).ToUpperInvariant();
        var ok = kind switch
        {
            TaxClassificationKind.CClassTrib => c.Length is >= 1 and <= 10 && c.All(char.IsAsciiLetterOrDigit),
            TaxClassificationKind.Ncm => c.Length == 8 && c.All(char.IsAsciiDigit),
            TaxClassificationKind.Nbs => c.Length is >= 1 and <= 12 && c.All(char.IsAsciiDigit),
            _ => false,
        };
        if (!ok) throw new DomainException($"Código {kind} malformado: \"{code}\".", "tax_classification_format");
        return c;
    }

    public bool Covers(DateOnly date) => ValidFrom <= date && (ValidTo is null || date <= ValidTo);

    public void ChangeStatus(TaxClassificationStatus next, string byName, DateTime atUtc)
    {
        if (next == Status) return;
        if (next == TaxClassificationStatus.Draft) throw new DomainException("Um código não volta a rascunho.", "tax_classification_status");
        Status = next;
        StatusChangedByName = byName;
        StatusChangedAt = atUtc;
        SetUpdatedAt();
    }

    /// <summary>Records a professional's view of the code's use in the recorte. Never set by the system.</summary>
    public void RecordProfessionalReview(ProfessionalReviewStatus status, string notes)
    {
        if (status == ProfessionalReviewStatus.Pending) throw new DomainException("Registre a revisão recebida.", "tax_review_status");
        if (string.IsNullOrWhiteSpace(notes)) throw new DomainException("Diga quem revisou e com qual registro.", "tax_review_notes");
        ProfessionalReview = status;
        ProfessionalReviewNotes = notes.Trim();
        SetUpdatedAt();
    }

    public bool IsProfessionallyApproved => ProfessionalReview is ProfessionalReviewStatus.Approved or ProfessionalReviewStatus.ApprovedWithCaveats;
}

/// <summary>
/// How a company classifies one of its products for IBS/CBS (tenant-wide): item kind and the codes, chosen by the
/// company and attributed to who set them. Sales calculations read it; the engine never guesses a code.
/// </summary>
public class TaxItemClassification : TenantEntity
{
    private TaxItemClassification() { }   // EF Core
    private TaxItemClassification(Guid tenantId) : base(tenantId) { }

    public Guid ProductId { get; private set; }
    public TaxItemKind ItemKind { get; private set; }
    public string? Ncm { get; private set; }
    public string? Nbs { get; private set; }
    public string CClassTrib { get; private set; } = string.Empty;
    public string? Notes { get; private set; }
    public Guid SetById { get; private set; }
    public string SetByName { get; private set; } = string.Empty;

    public static TaxItemClassification Create(Guid tenantId, Guid productId, Guid byId, string byName)
        => new(tenantId) { ProductId = productId, SetById = byId, SetByName = byName };

    public void Set(TaxItemKind kind, string? ncm, string? nbs, string cClassTrib, string? notes, Guid byId, string byName)
    {
        if (kind == TaxItemKind.Good && string.IsNullOrWhiteSpace(ncm)) throw new DomainException("Mercadoria precisa de NCM.", "tax_item_classification");
        if (kind == TaxItemKind.Service && string.IsNullOrWhiteSpace(nbs)) throw new DomainException("Serviço precisa de NBS.", "tax_item_classification");
        ItemKind = kind;
        Ncm = string.IsNullOrWhiteSpace(ncm) ? null : TaxClassificationCode.Normalize(TaxClassificationKind.Ncm, ncm);
        Nbs = string.IsNullOrWhiteSpace(nbs) ? null : TaxClassificationCode.Normalize(TaxClassificationKind.Nbs, nbs);
        CClassTrib = TaxClassificationCode.Normalize(TaxClassificationKind.CClassTrib, cClassTrib);
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        SetById = byId;
        SetByName = byName;
        SetUpdatedAt();
    }
}

/// <summary>
/// An ancillary obligation the platform lets companies assess (e.g. an IBS/CBS field set on the NF-e), with its
/// reference. Configurable — the catalogue starts empty and lists only what an operator documented. Assessments
/// refer to it by code and keep its name as it stood.
/// </summary>
public class TaxObligationDefinition : BaseEntity
{
    private TaxObligationDefinition() { }   // EF Core

    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    /// <summary>Legal source code or document behind it.</summary>
    public string Reference { get; private set; } = string.Empty;
    public bool Active { get; private set; } = true;
    public string CreatedByName { get; private set; } = string.Empty;

    public static TaxObligationDefinition Create(string code, string name, string description, string reference, string byName)
    {
        var c = (code ?? string.Empty).Trim().ToUpperInvariant();
        if (c.Length is < 2 or > 60 || !c.All(ch => char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_' or '.'))
            throw new DomainException("Código da obrigação: 2 a 60 letras, números, '-', '_' ou '.'.", "tax_obligation_code");
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(reference))
            throw new DomainException("Informe o nome da obrigação e a referência que a define.", "tax_obligation_fields");
        return new TaxObligationDefinition
        {
            Code = c, Name = name.Trim(), Description = description?.Trim() ?? string.Empty, Reference = reference.Trim(), CreatedByName = byName,
        };
    }

    public void SetActive(bool active)
    {
        Active = active;
        SetUpdatedAt();
    }
}

/// <summary>
/// The result of one obligation inside an assessment: its own status, evidence, remediation and the
/// verification of that remediation. <see cref="ComplianceAssessmentStatus.RemediationVerified"/> stays distinct
/// from <see cref="ComplianceAssessmentStatus.Verified"/>: it proves only the remediated scope.
/// </summary>
public class TaxComplianceObligationResult : StoreEntity
{
    private TaxComplianceObligationResult() { }   // EF Core

    internal TaxComplianceObligationResult(Guid tenantId, Guid assessmentId, TaxObligationResultInput r) : base(tenantId)
    {
        AssessmentId = assessmentId;
        ObligationCode = r.ObligationCode.Trim().ToUpperInvariant();
        ObligationName = r.ObligationName.Trim();
        Status = r.Status;
        EvidenceReference = Clean(r.EvidenceReference);
        EvidenceFileId = r.EvidenceFileId;
        Remediation = Clean(r.Remediation);
        RemediationVerifiedOn = r.RemediationVerifiedOn;
        RemediationVerifiedBy = Clean(r.RemediationVerifiedBy);
        Notes = Clean(r.Notes);
    }

    public Guid AssessmentId { get; private set; }
    public string ObligationCode { get; private set; } = string.Empty;
    /// <summary>The obligation's name as it stood when assessed.</summary>
    public string ObligationName { get; private set; } = string.Empty;
    public ComplianceAssessmentStatus Status { get; private set; }
    public string? EvidenceReference { get; private set; }
    public Guid? EvidenceFileId { get; private set; }
    public string? Remediation { get; private set; }
    public DateOnly? RemediationVerifiedOn { get; private set; }
    public string? RemediationVerifiedBy { get; private set; }
    public string? Notes { get; private set; }

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}

/// <summary>One obligation result as given by whoever assessed.</summary>
public sealed record TaxObligationResultInput(
    string ObligationCode, string ObligationName, ComplianceAssessmentStatus Status, string? EvidenceReference, Guid? EvidenceFileId,
    string? Remediation, DateOnly? RemediationVerifiedOn, string? RemediationVerifiedBy, string? Notes);
