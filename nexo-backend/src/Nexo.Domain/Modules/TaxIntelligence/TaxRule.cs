using Nexo.Domain.Common;
using Nexo.Domain.Exceptions;

namespace Nexo.Domain.Modules.TaxIntelligence;

/// <summary>
/// A tax rule's stable identity (platform-wide). Its behaviour lives in <see cref="TaxRuleVersion"/>s:
/// a change of law or a correction is a new version, never an edit, so any past calculation can be
/// rebuilt with exactly what was in force.
/// </summary>
public class TaxRule : BaseEntity
{
    private readonly List<TaxRuleVersion> _versions = [];

    private TaxRule() { }   // EF Core

    /// <summary>Stable key, e.g. "CBS.RATE.2026-TEST". Unique; never reused for another rule.</summary>
    public string Key { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;

    public IReadOnlyCollection<TaxRuleVersion> Versions => _versions.AsReadOnly();

    public static TaxRule Create(string key, string name, string description)
    {
        if (string.IsNullOrWhiteSpace(key)) throw new DomainException("Informe a chave da regra.");
        if (string.IsNullOrWhiteSpace(name)) throw new DomainException("Informe o nome da regra.");
        return new TaxRule { Key = key.Trim().ToUpperInvariant(), Name = name.Trim(), Description = description.Trim() };
    }
}

/// <summary>
/// One version of a rule: validity, conditions and result, plus the provisions it rests on. The
/// content (conditions, result, validity, priority) is fixed at creation and fingerprinted in
/// <see cref="ContentHash"/>; only the status moves afterwards, and each move is audited.
/// Conditions and result are typed JSON produced and read by the engine (Application layer).
/// </summary>
public class TaxRuleVersion : BaseEntity
{
    private readonly List<TaxRuleVersionSource> _sources = [];

    private TaxRuleVersion() { }   // EF Core

    public Guid RuleId { get; private set; }
    public int Version { get; private set; }
    public TaxRuleStatus Status { get; private set; }
    public DateOnly ValidFrom { get; private set; }
    /// <summary>Last day the version applies; null = open-ended.</summary>
    public DateOnly? ValidTo { get; private set; }
    public int Priority { get; private set; }
    public string ConditionsJson { get; private set; } = "[]";
    public string ResultJson { get; private set; } = "{}";
    /// <summary>SHA-256 of the canonical content, computed by the engine when the version is built.</summary>
    public string ContentHash { get; private set; } = string.Empty;
    /// <summary>How the version entered the catalogue ("seed:…" in migrations, "platform" via the admin API).</summary>
    public string Origin { get; private set; } = string.Empty;
    public string CreatedBy { get; private set; } = string.Empty;
    public string? Notes { get; private set; }
    public DateTime? ActivatedAt { get; private set; }
    public DateTime? StatusChangedAt { get; private set; }

    /// <summary>Professional review of the interpretation — independent of <see cref="Status"/> (technical).</summary>
    public ProfessionalReviewStatus ProfessionalReview { get; private set; } = ProfessionalReviewStatus.Pending;
    public string? ProfessionalReviewedBy { get; private set; }
    /// <summary>Professional identification of the reviewer (e.g. a council registration), when the profession has one.</summary>
    public string? ProfessionalReference { get; private set; }
    public DateTime? ProfessionalReviewedAt { get; private set; }
    /// <summary>What the review covered — an approval is valid only inside it.</summary>
    public string? ProfessionalReviewScope { get; private set; }
    public DateOnly? ProfessionalReviewValidFrom { get; private set; }
    public DateOnly? ProfessionalReviewValidTo { get; private set; }
    public string? ProfessionalReviewAssumptions { get; private set; }
    public string? ProfessionalReviewCaveats { get; private set; }
    public string? ProfessionalReviewNotes { get; private set; }
    /// <summary>Establishment regimes the current review covers (names of <c>TaxRegime</c>).</summary>
    public List<string> ProfessionalReviewRegimes { get; private set; } = [];
    /// <summary>Date of the review or opinion, as informed.</summary>
    public DateOnly? ProfessionalReviewedOn { get; private set; }
    /// <summary>The authenticated user who recorded the current review — never the professional.</summary>
    public Guid? ProfessionalReviewRecordedById { get; private set; }
    public string? ProfessionalReviewRecordedBy { get; private set; }
    public string? ProfessionalReviewRecordedByRole { get; private set; }
    /// <summary>The history record of the current review.</summary>
    public Guid? ProfessionalReviewRecordId { get; private set; }
    public Guid? ProfessionalReviewDocumentId { get; private set; }

    /// <summary>
    /// Approved (with or without caveats) by a professional, for a date inside the review's validity and — when the
    /// regime of the operation is known — for a regime the review covers. A review dated in the future, expired,
    /// asking for changes or rejecting never approves.
    /// </summary>
    public bool IsProfessionallyApprovedOn(DateOnly date, string? regime = null)
        => ProfessionalReview is ProfessionalReviewStatus.Approved or ProfessionalReviewStatus.ApprovedWithCaveats
           && ProfessionalReviewValidFrom is not null && ProfessionalReviewValidFrom <= date
           && (ProfessionalReviewValidTo is null || date <= ProfessionalReviewValidTo)
           && (regime is null || ProfessionalReviewRegimes.Contains(regime, StringComparer.Ordinal));

    /// <summary>
    /// Internal product/governance approval for the controlled pilot — independent of
    /// <see cref="ProfessionalReview"/>, which it never changes. It is not a professional tax opinion.
    /// </summary>
    public InternalApprovalStatus InternalApproval { get; private set; } = InternalApprovalStatus.NotApproved;
    /// <summary>The internal approval policy it was granted under (e.g. "orken-pilot-approval/1").</summary>
    public string? InternalApprovalPolicy { get; private set; }
    /// <summary>The authenticated user who recorded the approval (a governance actor, never a professional identity).</summary>
    public Guid? InternalApprovedById { get; private set; }
    /// <summary>That user's display name, as authenticated.</summary>
    public string? InternalApprovedBy { get; private set; }
    /// <summary>The capability under which they acted (e.g. "Platform").</summary>
    public string? InternalApprovedByRole { get; private set; }
    public DateTime? InternalApprovedAt { get; private set; }
    public string? InternalApprovalScope { get; private set; }
    public DateOnly? InternalApprovalValidFrom { get; private set; }
    public DateOnly? InternalApprovalValidTo { get; private set; }
    /// <summary>The caveats accepted with the approval — they never disappear because of it.</summary>
    public string? InternalApprovalCaveats { get; private set; }
    /// <summary>Where the governance decision is recorded (document or ticket).</summary>
    public string? InternalApprovalDecisionRef { get; private set; }
    public string? InternalApprovalWithdrawalReason { get; private set; }
    public DateTime? InternalApprovalWithdrawnAt { get; private set; }

    /// <summary>
    /// Authorises new work in the controlled pilot on a date: the version is technically <see cref="TaxRuleStatus.Active"/>
    /// now, and approved internally with that date inside the approval's validity. A version later revoked or
    /// superseded keeps its approval on record (history) but no longer authorises anything new.
    /// </summary>
    public bool IsApprovedForPilotOn(DateOnly date)
        => Status == TaxRuleStatus.Active
           && InternalApproval == InternalApprovalStatus.ApprovedForControlledPilot
           && InternalApprovalValidFrom is not null && InternalApprovalValidFrom <= date
           && (InternalApprovalValidTo is null || date <= InternalApprovalValidTo);

    /// <summary>
    /// Records the internal pilot approval. Only an active version qualifies — one awaiting regulation,
    /// with an unverified source, a draft or a finished one stays blocked. The approval states its policy,
    /// who decided, scope, validity (inside the version's), caveats and where the decision is recorded.
    /// The professional review is left exactly as it was.
    /// </summary>
    public void ApproveForControlledPilot(InternalApprovalInput r, DateTime atUtc)
    {
        if (Status != TaxRuleStatus.Active)
            throw new DomainException(
                $"Só uma versão ativa pode ser aprovada para o piloto; uma versão {Status} continua bloqueada.", "tax_pilot_approval_status");
        if (string.IsNullOrWhiteSpace(r.Policy) || r.ApprovedById == Guid.Empty || string.IsNullOrWhiteSpace(r.ApprovedBy)
            || string.IsNullOrWhiteSpace(r.ApprovedByRole) || string.IsNullOrWhiteSpace(r.Scope) || string.IsNullOrWhiteSpace(r.DecisionReference))
            throw new DomainException("A aprovação interna diz a política, quem decidiu, o escopo e onde a decisão está registrada.", "tax_pilot_approval_fields");
        if (string.IsNullOrWhiteSpace(r.Caveats))
            throw new DomainException("A aprovação interna registra as ressalvas aceitas.", "tax_pilot_approval_caveats");
        if (r.ValidTo is not null && r.ValidTo < r.ValidFrom)
            throw new DomainException("O fim da validade da aprovação é anterior ao início.", "tax_pilot_approval_validity");
        if (r.ValidFrom < ValidFrom || (ValidTo is not null && (r.ValidTo is null || r.ValidTo > ValidTo)))
            throw new DomainException("A aprovação interna não vai além da vigência da versão.", "tax_pilot_approval_validity");

        InternalApproval = InternalApprovalStatus.ApprovedForControlledPilot;
        InternalApprovalPolicy = r.Policy.Trim();
        InternalApprovedById = r.ApprovedById;
        InternalApprovedBy = r.ApprovedBy.Trim();
        InternalApprovedByRole = r.ApprovedByRole.Trim();
        InternalApprovedAt = atUtc;
        InternalApprovalScope = r.Scope.Trim();
        InternalApprovalValidFrom = r.ValidFrom;
        InternalApprovalValidTo = r.ValidTo;
        InternalApprovalCaveats = r.Caveats.Trim();
        InternalApprovalDecisionRef = r.DecisionReference.Trim();
        InternalApprovalWithdrawalReason = null;
        InternalApprovalWithdrawnAt = null;
        SetUpdatedAt();
    }

    /// <summary>Withdraws a pilot approval (e.g. a suspension criterion was met). The approval's record is kept.</summary>
    public void WithdrawPilotApproval(string reason, DateTime atUtc)
    {
        if (InternalApproval != InternalApprovalStatus.ApprovedForControlledPilot)
            throw new DomainException("Esta versão não está aprovada para o piloto.", "tax_pilot_approval_state");
        if (string.IsNullOrWhiteSpace(reason))
            throw new DomainException("Diga por que a aprovação para o piloto foi retirada.", "tax_pilot_approval_reason");
        InternalApproval = InternalApprovalStatus.Withdrawn;
        InternalApprovalWithdrawalReason = reason.Trim();
        InternalApprovalWithdrawnAt = atUtc;
        SetUpdatedAt();
    }

    public IReadOnlyCollection<TaxRuleVersionSource> Sources => _sources.AsReadOnly();

    public bool Covers(DateOnly date) => ValidFrom <= date && (ValidTo is null || date <= ValidTo);

    /// <summary>
    /// Records a tax professional's review. Content and technical status never change. An approval
    /// states its scope and from when it holds; an approval with caveats states them; a review that
    /// asks for changes or rejects says why. Each review is audited by the caller.
    /// </summary>
    public TaxProfessionalReviewRecord RecordProfessionalReview(ProfessionalReviewInput r, DateTime atUtc)
    {
        if (r.Status == ProfessionalReviewStatus.Pending)
            throw new DomainException("Pendente é o estado inicial; registre a revisão recebida.", "tax_review_status");
        if (string.IsNullOrWhiteSpace(r.Reviewer)) throw new DomainException("Informe quem revisou.", "tax_review_reviewer");
        if (r.RecordedById == Guid.Empty || string.IsNullOrWhiteSpace(r.RecordedByName))
            throw new DomainException("O registro da revisão identifica quem o fez no sistema.", "tax_review_recorder");
        if (r.ValidFrom is not null && r.ValidTo is not null && r.ValidTo < r.ValidFrom)
            throw new DomainException("O fim da validade da revisão é anterior ao início.", "tax_review_validity");
        if (r.ReviewedOn is { } on && DateOnly.FromDateTime(atUtc).AddDays(1) < on)
            throw new DomainException("A data da revisão não pode ser futura.", "tax_review_date");
        var approval = r.Status is ProfessionalReviewStatus.Approved or ProfessionalReviewStatus.ApprovedWithCaveats;
        if (approval && (string.IsNullOrWhiteSpace(r.Scope) || r.ValidFrom is null))
            throw new DomainException("Uma aprovação diz o escopo e desde quando vale.", "tax_review_scope");
        if (approval && !r.CoveredRegimes.Any(x => !string.IsNullOrWhiteSpace(x)))
            throw new DomainException("Uma aprovação diz a quais regimes do estabelecimento se aplica.", "tax_review_regimes");
        if (approval && (r.ValidFrom < ValidFrom || (ValidTo is not null && (r.ValidTo is null || r.ValidTo > ValidTo))))
            throw new DomainException("A aprovação profissional não vai além da vigência da versão.", "tax_review_validity");
        if (r.Status == ProfessionalReviewStatus.ApprovedWithCaveats && string.IsNullOrWhiteSpace(r.Caveats))
            throw new DomainException("Aprovação com ressalvas precisa das ressalvas.", "tax_review_caveats");
        if (r.Status is ProfessionalReviewStatus.ChangesRequired or ProfessionalReviewStatus.Rejected && string.IsNullOrWhiteSpace(r.Notes))
            throw new DomainException("Uma revisão que pede mudança ou rejeita precisa dizer por quê.", "tax_review_notes");

        var record = TaxProfessionalReviewRecord.From(Id, ProfessionalReview, r);
        ProfessionalReview = r.Status;
        ProfessionalReviewedBy = record.Reviewer;
        ProfessionalReference = record.ProfessionalReference;
        ProfessionalReviewedAt = atUtc;
        ProfessionalReviewedOn = record.ReviewedOn;
        ProfessionalReviewScope = record.Scope;
        ProfessionalReviewRegimes = [.. record.CoveredRegimes];
        ProfessionalReviewValidFrom = r.ValidFrom;
        ProfessionalReviewValidTo = r.ValidTo;
        ProfessionalReviewAssumptions = record.Assumptions;
        ProfessionalReviewCaveats = record.Caveats;
        ProfessionalReviewNotes = record.Notes;
        ProfessionalReviewRecordedById = record.RecordedById;
        ProfessionalReviewRecordedBy = record.RecordedByName;
        ProfessionalReviewRecordedByRole = record.RecordedByRole;
        ProfessionalReviewRecordId = record.Id;
        ProfessionalReviewDocumentId = record.SupportingDocumentId;
        SetUpdatedAt();
        return record;
    }

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    public static TaxRuleVersion Create(
        Guid ruleId, int version, DateOnly validFrom, DateOnly? validTo, int priority,
        string conditionsJson, string resultJson, string contentHash, string origin, string createdBy,
        string? notes, IEnumerable<Guid> legalSourceIds)
    {
        if (version < 1) throw new DomainException("A versão começa em 1.");
        if (validTo is not null && validTo < validFrom) throw new DomainException("O fim da vigência é anterior ao início.");
        if (string.IsNullOrWhiteSpace(contentHash)) throw new DomainException("Versão sem impressão digital do conteúdo.");

        var v = new TaxRuleVersion
        {
            RuleId = ruleId,
            Version = version,
            Status = TaxRuleStatus.Draft,
            ValidFrom = validFrom,
            ValidTo = validTo,
            Priority = priority,
            ConditionsJson = conditionsJson,
            ResultJson = resultJson,
            ContentHash = contentHash,
            Origin = origin,
            CreatedBy = createdBy,
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
        };
        foreach (var id in legalSourceIds.Distinct())
            v._sources.Add(new TaxRuleVersionSource(v.Id, id));
        return v;
    }

    /// <summary>
    /// Moves the status. Content never changes; an active version can only be superseded or revoked
    /// (it may already have produced calculations), and a finished version never comes back.
    /// </summary>
    public void ChangeStatus(TaxRuleStatus next, DateTime atUtc)
    {
        if (next == Status) return;
        var allowed = Status switch
        {
            TaxRuleStatus.Draft or TaxRuleStatus.PendingRegulation or TaxRuleStatus.Unverified =>
                next is TaxRuleStatus.PendingRegulation or TaxRuleStatus.Unverified or TaxRuleStatus.Active or TaxRuleStatus.Revoked,
            TaxRuleStatus.Active => next is TaxRuleStatus.Superseded or TaxRuleStatus.Revoked,
            _ => false,
        };
        if (!allowed)
            throw new DomainException($"Uma versão {Status} não pode passar para {next}.", "tax_rule_status_transition");

        Status = next;
        StatusChangedAt = atUtc;
        if (next == TaxRuleStatus.Active) ActivatedAt = atUtc;
        SetUpdatedAt();
    }
}

/// <summary>
/// A professional review as received — who reviewed, under which identification, when, scope (text and covered
/// regimes), validity, assumptions, caveats and the supporting document — and, apart, the authenticated user who
/// records it.
/// </summary>
public sealed record ProfessionalReviewInput(
    ProfessionalReviewStatus Status, string Reviewer, string? ProfessionalReference, string? Scope, DateOnly? ValidFrom,
    DateOnly? ValidTo, string? Assumptions, string? Caveats, string? Notes,
    IReadOnlyList<string> CoveredRegimes, DateOnly? ReviewedOn, Guid? SupportingDocumentId,
    Guid RecordedById, string RecordedByName, string RecordedByRole);

/// <summary>
/// An internal pilot approval as decided: policy, the authenticated actor (id, display name, capability),
/// scope, validity, caveats and where the decision is recorded.
/// </summary>
public sealed record InternalApprovalInput(
    string Policy, Guid ApprovedById, string ApprovedBy, string ApprovedByRole, string Scope, DateOnly ValidFrom, DateOnly? ValidTo,
    string Caveats, string DecisionReference);

/// <summary>Join between a rule version and a legal source (tax_rule_version_sources).</summary>
public class TaxRuleVersionSource
{
    private TaxRuleVersionSource() { }   // EF Core

    public TaxRuleVersionSource(Guid ruleVersionId, Guid legalSourceId)
    {
        RuleVersionId = ruleVersionId;
        LegalSourceId = legalSourceId;
    }

    public Guid RuleVersionId { get; private set; }
    public Guid LegalSourceId { get; private set; }
    public TaxLegalSource? LegalSource { get; private set; }
}
