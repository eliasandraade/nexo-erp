using Nexo.Domain.Common;
using Nexo.Domain.Exceptions;

namespace Nexo.Domain.Modules.TaxIntelligence;

/// <summary>
/// An assessment of the IBS/CBS ancillary obligations of an establishment for a period and a scope —
/// the condition of the 2026 collection waiver (LC 214/2025, art. 348, § 1º). Historical and
/// append-only: an establishment can be verified for one period or scope and not for another, and a
/// newer assessment of the same period is recorded beside the old one (the newest wins for
/// calculations made after it). Never a statement that "the company is compliant": only what was
/// checked, for which period and scope, by whom, with which limitations.
/// </summary>
public class TaxComplianceAssessment : StoreEntity
{
    private TaxComplianceAssessment() { }   // EF Core
    private TaxComplianceAssessment(Guid tenantId) : base(tenantId) { }

    public ComplianceAssessmentStatus Status { get; private set; }
    public DateOnly PeriodFrom { get; private set; }
    public DateOnly PeriodTo { get; private set; }
    /// <summary>What the assessment covers (e.g. "estabelecimento, NF-e e NFC-e emitidas").</summary>
    public string Scope { get; private set; } = string.Empty;
    /// <summary>Obligations that were actually checked, as described by whoever assessed.</summary>
    public List<string> ObligationsChecked { get; private set; } = [];
    /// <summary>What supports the assessment (document references, reports…), as given. Not interpreted.</summary>
    public string? Evidence { get; private set; }
    /// <summary>Who assessed (person or office, possibly external to the company).</summary>
    public string AssessedBy { get; private set; } = string.Empty;
    public DateTime AssessedAt { get; private set; }
    public string? Notes { get; private set; }
    /// <summary>What the assessment does not cover or could not check.</summary>
    public string? Limitations { get; private set; }
    /// <summary>
    /// Taking part in the Programa Nacional de Conformidade Tributária (Ato Conjunto RFB/CGIBS nº 5/2026).
    /// Recorded as a fact only: it never means the obligations are fully met.
    /// </summary>
    public bool PnctParticipation { get; private set; }
    public Guid RecordedById { get; private set; }
    public string RecordedByName { get; private set; } = string.Empty;
    /// <summary>The assessment it replaces for the same period and scope, when it is a reassessment (e.g. after a remediation).</summary>
    public Guid? SupersedesAssessmentId { get; private set; }

    private readonly List<TaxComplianceObligationResult> _results = [];
    /// <summary>Each obligation assessed, with its own status, evidence and remediation.</summary>
    public IReadOnlyCollection<TaxComplianceObligationResult> Results => _results.AsReadOnly();

    public static TaxComplianceAssessment Record(
        Guid tenantId, ComplianceAssessmentStatus status, DateOnly periodFrom, DateOnly periodTo, string? scope,
        IReadOnlyList<string> obligationsChecked, string? evidence, string assessedBy, DateTime assessedAt, string? notes,
        string? limitations, bool pnctParticipation, Guid recordedById, string recordedByName,
        IReadOnlyList<TaxObligationResultInput>? results = null, Guid? supersedes = null)
    {
        if (periodTo < periodFrom) throw new DomainException("O fim do período é anterior ao início.", "tax_compliance_period");
        if (string.IsNullOrWhiteSpace(assessedBy)) throw new DomainException("Informe quem avaliou.", "tax_compliance_assessor");
        results ??= [];
        CheckResults(status, results);
        var checkedList = obligationsChecked.Concat(results.Select(r => r.ObligationName))
            .Select(o => o.Trim()).Where(o => o.Length > 0).Distinct().ToList();
        if (status != ComplianceAssessmentStatus.Unknown && string.IsNullOrWhiteSpace(scope))
            throw new DomainException("Informe o escopo da avaliação (o que ela cobre).", "tax_compliance_scope");
        // A verification carries its evidence per obligation (CheckResults); the free-text field is only a summary.
        if (status is ComplianceAssessmentStatus.Verified or ComplianceAssessmentStatus.RemediationVerified && checkedList.Count == 0)
            throw new DomainException("Uma verificação precisa dizer quais obrigações foram conferidas e com qual evidência.", "tax_compliance_evidence");
        if (status is ComplianceAssessmentStatus.InconsistencyIdentified or ComplianceAssessmentStatus.UnderRemediation
            or ComplianceAssessmentStatus.NonCompliant && string.IsNullOrWhiteSpace(notes))
            throw new DomainException("Descreva a inconsistência ou o que está sendo regularizado.", "tax_compliance_notes");

        var assessment = new TaxComplianceAssessment(tenantId)
        {
            Status = status,
            PeriodFrom = periodFrom,
            PeriodTo = periodTo,
            Scope = scope?.Trim() ?? string.Empty,
            ObligationsChecked = checkedList,
            Evidence = Clean(evidence),
            AssessedBy = assessedBy.Trim(),
            AssessedAt = assessedAt,
            Notes = Clean(notes),
            Limitations = Clean(limitations),
            PnctParticipation = pnctParticipation,
            RecordedById = recordedById,
            RecordedByName = recordedByName,
            SupersedesAssessmentId = supersedes,
        };
        foreach (var r in results)
            assessment._results.Add(new TaxComplianceObligationResult(tenantId, assessment.Id, r));
        return assessment;
    }

    /// <summary>
    /// The structured results must support the overall status. A verification (plain or of a remediation) is never
    /// only free text: it lists each obligation with its evidence, and none of them may be open. Verified means
    /// every obligation verified or not applicable; RemediationVerified means at least one remediation verified
    /// (with what was remediated, when and by whom) and nothing left open — it never becomes Verified by itself.
    /// </summary>
    private static void CheckResults(ComplianceAssessmentStatus status, IReadOnlyList<TaxObligationResultInput> results)
    {
        if (results.Select(r => r.ObligationCode.Trim().ToUpperInvariant()).Distinct().Count() != results.Count)
            throw new DomainException("Cada obrigação aparece uma vez na avaliação.", "tax_compliance_duplicate_obligation");
        foreach (var r in results) CheckResult(r);

        if (status is not (ComplianceAssessmentStatus.Verified or ComplianceAssessmentStatus.RemediationVerified)) return;
        if (results.Count == 0)
            throw new DomainException("Uma verificação lista cada obrigação conferida, com sua evidência — não só texto livre.", "tax_compliance_results");
        if (status == ComplianceAssessmentStatus.Verified
            && (results.Any(r => r.Status is not (ComplianceAssessmentStatus.Verified or ComplianceAssessmentStatus.NotApplicable))
                || results.All(r => r.Status == ComplianceAssessmentStatus.NotApplicable)))
            throw new DomainException("Verificada só quando cada obrigação foi verificada (ou não se aplica).", "tax_compliance_results");
        if (status == ComplianceAssessmentStatus.RemediationVerified
            && (results.Any(r => r.Status is not (ComplianceAssessmentStatus.Verified or ComplianceAssessmentStatus.RemediationVerified
                    or ComplianceAssessmentStatus.NotApplicable))
                || results.All(r => r.Status != ComplianceAssessmentStatus.RemediationVerified)))
            throw new DomainException("Regularização verificada exige ao menos uma regularização verificada e nenhuma obrigação em aberto.",
                "tax_compliance_results");
    }

    private static void CheckResult(TaxObligationResultInput r)
    {
        if (string.IsNullOrWhiteSpace(r.ObligationCode) || string.IsNullOrWhiteSpace(r.ObligationName))
            throw new DomainException("Cada resultado indica a obrigação avaliada.", "tax_compliance_obligation");
        var withEvidence = !string.IsNullOrWhiteSpace(r.EvidenceReference) || r.EvidenceFileId is not null;
        switch (r.Status)
        {
            case ComplianceAssessmentStatus.Verified when !withEvidence:
                throw new DomainException($"{r.ObligationName}: verificada sem evidência.", "tax_compliance_evidence");
            case ComplianceAssessmentStatus.InconsistencyIdentified or ComplianceAssessmentStatus.NonCompliant when string.IsNullOrWhiteSpace(r.Notes):
                throw new DomainException($"{r.ObligationName}: descreva a inconsistência.", "tax_compliance_notes");
            case ComplianceAssessmentStatus.UnderRemediation when string.IsNullOrWhiteSpace(r.Remediation):
                throw new DomainException($"{r.ObligationName}: descreva o que está sendo regularizado.", "tax_compliance_remediation");
            case ComplianceAssessmentStatus.RemediationVerified
                when string.IsNullOrWhiteSpace(r.Remediation) || r.RemediationVerifiedOn is null || string.IsNullOrWhiteSpace(r.RemediationVerifiedBy) || !withEvidence:
                throw new DomainException($"{r.ObligationName}: a regularização verificada diz o que foi regularizado, quando, por quem e com qual evidência.",
                    "tax_compliance_remediation");
        }
    }

    public bool Covers(DateOnly date) => PeriodFrom <= date && date <= PeriodTo;

    /// <summary>
    /// The compliance fact handed to the engine for a date inside the period. Only a verification
    /// counts as met; a declaration, an inconsistency under remediation, a verified remediation, PNCT
    /// participation or "not applicable" stay unknown (the rule that needs it is not applied, and the
    /// calculation says why). A recorded non-compliance is false.
    /// </summary>
    public bool? ForRules => Status switch
    {
        ComplianceAssessmentStatus.Verified => true,
        ComplianceAssessmentStatus.NonCompliant => false,
        _ => null,
    };

    /// <summary>The assessment in force on a date: the newest one whose period covers it, or none.</summary>
    public static TaxComplianceAssessment? InForce(IEnumerable<TaxComplianceAssessment> assessments, DateOnly date)
        => assessments.Where(a => a.Covers(date)).OrderByDescending(a => a.CreatedAt).ThenByDescending(a => a.AssessedAt).FirstOrDefault();

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
