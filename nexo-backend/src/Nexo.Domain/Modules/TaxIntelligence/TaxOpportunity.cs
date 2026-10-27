using Nexo.Domain.Common;
using Nexo.Domain.Exceptions;

namespace Nexo.Domain.Modules.TaxIntelligence;

/// <summary>
/// Something the engine found that might lower taxes, defer them, free cash or reduce risk. It is a
/// proposal to analyse — nothing is ever applied automatically. The workflow is
/// Identified → InReview → Approved (a scenario approval, recorded as a <see cref="TaxDecision"/>) or
/// Rejected; then, after the operation really happened, ExecutionVerified or ExecutionDiverged
/// (recorded as a <see cref="TaxExecutionVerification"/>). At any point before approval it can be
/// Blocked when it matches a <see cref="ProhibitedStructure"/>; a blocked opportunity shows no
/// saving and can never be approved. A scenario approval never states that a future fiscal document
/// exists: the requirements of the execution phase are only answered after the approval.
/// </summary>
public class TaxOpportunity : StoreEntity
{
    private readonly List<TaxOpportunityRequirement> _requirements = [];
    private readonly List<TaxOpportunityEvidence> _evidence = [];

    private TaxOpportunity() { }   // EF Core
    private TaxOpportunity(Guid tenantId) : base(tenantId) { }

    public TaxOpportunityType Type { get; private set; }
    public TaxOpportunityStatus Status { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public string Summary { get; private set; } = string.Empty;
    /// <summary>Detector that found it, e.g. "unconditional-discount@1".</summary>
    public string DetectorKey { get; private set; } = string.Empty;
    /// <summary>Engine version that produced the scenarios (their JSON is in its canonical form).</summary>
    public string EngineVersion { get; private set; } = string.Empty;
    public Guid? SourceCalculationId { get; private set; }
    public string CurrentScenarioJson { get; private set; } = "{}";
    public string ProposedScenarioJson { get; private set; } = "{}";
    /// <summary>Estimated effect in R$ (positive = less tax or more cash); its nature is in <see cref="ImpactNature"/>.</summary>
    public decimal EstimatedImpact { get; private set; }
    public TaxRiskLevel Risk { get; private set; }
    /// <summary>0–100: how sure the estimate is, given the data the engine had.</summary>
    public int Confidence { get; private set; }
    public string LegalSourceCodes { get; private set; } = string.Empty;
    public string? Caveats { get; private set; }
    public ProhibitedStructure? BlockedAs { get; private set; }
    public string? BlockReason { get; private set; }
    public string? RejectionReason { get; private set; }
    public Guid? ReviewedById { get; private set; }
    public string? ReviewedByName { get; private set; }
    public DateTime? ReviewedAt { get; private set; }
    /// <summary>Who opened the analysis — with requirement answers and evidence, the people who analysed it.</summary>
    public Guid? ReviewStartedById { get; private set; }
    public string? ReviewStartedByName { get; private set; }
    /// <summary>
    /// Everyone who ever took part in the analysis (opened it, answered a requirement, added or reviewed
    /// evidence). Only grows: a later answer or review never erases an earlier participant.
    /// </summary>
    public List<Guid> ParticipantIds { get; private set; } = [];

    public IReadOnlyCollection<TaxOpportunityRequirement> Requirements => _requirements.AsReadOnly();
    public IReadOnlyCollection<TaxOpportunityEvidence> Evidence => _evidence.AsReadOnly();

    /// <summary>Deferral is never a saving, credits are not cash: the type fixes how the impact is read.</summary>
    public TaxImpactNature ImpactNature => NatureOf(Type);

    public static TaxImpactNature NatureOf(TaxOpportunityType type) => type switch
    {
        TaxOpportunityType.TaxSaving => TaxImpactNature.DefinitiveSaving,
        TaxOpportunityType.TaxDeferral => TaxImpactNature.Deferral,
        TaxOpportunityType.CashFlowOptimization => TaxImpactNature.CashFlow,
        TaxOpportunityType.CreditOptimization or TaxOpportunityType.RefundOpportunity => TaxImpactNature.Credit,
        TaxOpportunityType.CalculatedTaxReduction => TaxImpactNature.CalculatedTaxReduction,
        _ => TaxImpactNature.RiskAvoided,
    };

    public static TaxOpportunity Create(
        Guid tenantId, TaxOpportunityType type, string title, string summary, string detectorKey, string engineVersion,
        Guid? sourceCalculationId, string currentScenarioJson, string proposedScenarioJson, decimal estimatedImpact,
        TaxRiskLevel risk, int confidence, string legalSourceCodes, string? caveats)
    {
        if (confidence is < 0 or > 100) throw new DomainException("Confiança vai de 0 a 100.");
        return new TaxOpportunity(tenantId)
        {
            Type = type,
            Status = TaxOpportunityStatus.Identified,
            Title = title,
            Summary = summary,
            DetectorKey = detectorKey,
            EngineVersion = engineVersion,
            SourceCalculationId = sourceCalculationId,
            CurrentScenarioJson = currentScenarioJson,
            ProposedScenarioJson = proposedScenarioJson,
            EstimatedImpact = estimatedImpact,
            Risk = risk,
            Confidence = confidence,
            LegalSourceCodes = legalSourceCodes,
            Caveats = caveats,
        };
    }

    public TaxOpportunityRequirement AddRequirement(string code, string text, string? legalSourceCode,
        RequirementPhase phase = RequirementPhase.ScenarioApproval, TaxEvidenceType? requiredEvidence = null)
    {
        var r = new TaxOpportunityRequirement(TenantId, Id, code, text, legalSourceCode, phase, requiredEvidence);
        _requirements.Add(r);
        return r;
    }

    public void StartReview(Guid userId, string userName, DateTime atUtc)
    {
        EnsureOpen();
        if (Status != TaxOpportunityStatus.Identified)
            throw new DomainException("A oportunidade já está em análise.", "tax_opportunity_state");
        Status = TaxOpportunityStatus.InReview;
        Participate(userId);
        ReviewStartedById = userId;
        ReviewStartedByName = userName;
        ReviewedById = userId;
        ReviewedByName = userName;
        ReviewedAt = atUtc;
        SetUpdatedAt();
    }

    /// <summary>
    /// Answers one requirement. Scenario requirements are answered during the analysis; execution
    /// requirements only after the scenario was approved — before that the operation has not happened.
    /// </summary>
    public void AnswerRequirement(Guid requirementId, bool met, string? note, Guid userId, string userName, DateTime atUtc)
    {
        var r = _requirements.FirstOrDefault(x => x.Id == requirementId)
            ?? throw new NotFoundException("Requisito", requirementId);
        if (r.Phase == RequirementPhase.ScenarioApproval && Status != TaxOpportunityStatus.InReview)
            throw new DomainException("Abra a análise antes de responder aos requisitos do cenário.", "tax_opportunity_state");
        if (r.Phase == RequirementPhase.ExecutionVerification && Status != TaxOpportunityStatus.Approved)
            throw new DomainException("Requisitos da execução só depois que o cenário for aprovado e a operação acontecer.", "tax_opportunity_execution_state");
        r.Answer(met, note, userId, userName, atUtc);
        Participate(userId);
        SetUpdatedAt();
    }

    /// <summary>
    /// Attaches evidence, optionally to the requirement it supports. Evidence of the execution phase
    /// (the fiscal document of the operation…) is only accepted after the scenario approval.
    /// </summary>
    public TaxOpportunityEvidence AddEvidence(
        TaxEvidenceType type, Guid? requirementId, string description, string? documentReference, string contentHash, string? notes,
        Guid userId, string userName)
    {
        if (Status is TaxOpportunityStatus.Rejected or TaxOpportunityStatus.Blocked
            or TaxOpportunityStatus.ExecutionVerified or TaxOpportunityStatus.ExecutionDiverged)
            throw new DomainException("Esta oportunidade já foi encerrada.", "tax_opportunity_closed");
        if (string.IsNullOrWhiteSpace(description)) throw new DomainException("Descreva a evidência.");
        var requirement = requirementId is null ? null
            : _requirements.FirstOrDefault(r => r.Id == requirementId) ?? throw new NotFoundException("Requisito", requirementId.Value);
        var executionPhase = Status == TaxOpportunityStatus.Approved;
        if (executionPhase && requirement?.Phase != RequirementPhase.ExecutionVerification)
            throw new DomainException("Depois da aprovação do cenário, só entram evidências ligadas a requisitos da execução.", "tax_evidence_phase");
        if (!executionPhase && requirement?.Phase == RequirementPhase.ExecutionVerification)
            throw new DomainException("Evidência da execução só depois que o cenário for aprovado e a operação acontecer.", "tax_evidence_phase");

        var e = new TaxOpportunityEvidence(TenantId, Id, type, requirementId, description, documentReference, contentHash, notes, userId, userName);
        _evidence.Add(e);
        Participate(userId);
        SetUpdatedAt();
        return e;
    }

    /// <summary>Accepts or rejects a piece of evidence. A rejected one does not support any requirement.</summary>
    public void ReviewEvidence(Guid evidenceId, bool accepted, string? notes, Guid userId, string userName, DateTime atUtc)
    {
        if (Status is TaxOpportunityStatus.Rejected or TaxOpportunityStatus.Blocked
            or TaxOpportunityStatus.ExecutionVerified or TaxOpportunityStatus.ExecutionDiverged)
            throw new DomainException("Esta oportunidade já foi encerrada.", "tax_opportunity_closed");
        var e = _evidence.FirstOrDefault(x => x.Id == evidenceId) ?? throw new NotFoundException("Evidência", evidenceId);
        if (!accepted && string.IsNullOrWhiteSpace(notes)) throw new DomainException("Diga por que a evidência foi recusada.");
        e.Review(accepted, notes, userId, userName, atUtc);
        Participate(userId);
        SetUpdatedAt();
    }

    /// <summary>Whether the requirement has the evidence it asks for (of its type, linked to it, not rejected).</summary>
    public bool HasRequiredEvidence(TaxOpportunityRequirement r)
        => r.RequiredEvidence is null
           || _evidence.Any(e => e.RequirementId == r.Id && e.Type == r.RequiredEvidence && e.Status != TaxEvidenceStatus.Rejected);

    /// <summary>
    /// Marks the opportunity as a prohibited structure. Final: the saving is no longer offered and
    /// the opportunity can never be approved. The reason stays on record.
    /// </summary>
    public void Block(ProhibitedStructure structure, string reason, Guid userId, string userName, DateTime atUtc)
    {
        EnsureOpen();
        if (string.IsNullOrWhiteSpace(reason)) throw new DomainException("Informe o motivo do bloqueio.");
        Status = TaxOpportunityStatus.Blocked;
        BlockedAs = structure;
        BlockReason = reason.Trim();
        ReviewedById = userId;
        ReviewedByName = userName;
        ReviewedAt = atUtc;
        SetUpdatedAt();
    }

    public void Reject(string reason, Guid userId, string userName, DateTime atUtc)
    {
        EnsureOpen();
        if (string.IsNullOrWhiteSpace(reason)) throw new DomainException("Informe o motivo da rejeição.");
        Status = TaxOpportunityStatus.Rejected;
        RejectionReason = reason.Trim();
        ReviewedById = userId;
        ReviewedByName = userName;
        ReviewedAt = atUtc;
        SetUpdatedAt();
    }

    /// <summary>
    /// People who took part in the analysis: who opened the review, answered requirements, added evidence
    /// or accepted/rejected evidence. None of them can be an independent approver.
    /// </summary>
    public IReadOnlySet<Guid> Analysts => ParticipantIds.Select(id => (Guid?)id)
        .Append(ReviewStartedById)
        .Concat(_requirements.Select(r => r.AnsweredById))
        .Concat(_evidence.Select(e => (Guid?)e.AddedById))
        .Concat(_evidence.Select(e => e.ReviewedById))
        .Where(id => id is not null).Select(id => id!.Value).ToHashSet();

    private void Participate(Guid userId)
    {
        if (!ParticipantIds.Contains(userId)) ParticipantIds.Add(userId);
    }

    /// <summary>Whether this approver did the whole analysis alone (segregation of duties).</summary>
    public bool IsSoleAnalyst(Guid userId) => Analysts.Count > 0 && Analysts.All(a => a == userId);

    /// <summary>
    /// Scenario approval: the proposed structure is acceptable before the operation. It records
    /// nothing but the status — the decision dossier is written by the caller in the same transaction.
    /// Every scenario requirement must be met and have the evidence it asks for; a HIGH risk needs the
    /// approver to acknowledge it explicitly; and the approver must satisfy the maker-checker
    /// <paramref name="approval"/> the policy derived from risk and materiality. A justification lets
    /// whoever analysed alone approve only where the policy allows it — never where independence is required.
    /// </summary>
    public void Approve(bool highRiskAcknowledged, Guid userId, string userName, DateTime atUtc,
        ApprovalRequirement approval = ApprovalRequirement.SelfApprovalWithJustification, string? selfApprovalJustification = null)
    {
        if (Status == TaxOpportunityStatus.Blocked)
            throw new DomainException("Uma estrutura proibida não pode ser aprovada.", "tax_opportunity_blocked");
        if (Status != TaxOpportunityStatus.InReview)
            throw new DomainException("Só uma oportunidade em análise pode ser aprovada.", "tax_opportunity_state");
        var scenario = _requirements.Where(r => r.Phase == RequirementPhase.ScenarioApproval).ToList();
        var pending = scenario.Where(r => r.Met != true).Select(r => r.Text).ToList();
        if (pending.Count > 0)
            throw new DomainException($"Há requisitos não atendidos: {string.Join("; ", pending)}", "tax_opportunity_requirements");
        var unsupported = scenario.Where(r => !HasRequiredEvidence(r)).Select(r => r.Text).ToList();
        if (unsupported.Count > 0)
            throw new DomainException($"Falta evidência para: {string.Join("; ", unsupported)}", "tax_opportunity_evidence_missing");
        if (Risk == TaxRiskLevel.High && !highRiskAcknowledged)
            throw new DomainException("Risco alto: a aprovação precisa reconhecer o risco expressamente.", "tax_opportunity_high_risk");
        CheckApprover(approval, userId, selfApprovalJustification);

        Status = TaxOpportunityStatus.Approved;
        ReviewedById = userId;
        ReviewedByName = userName;
        ReviewedAt = atUtc;
        SetUpdatedAt();
    }

    private void CheckApprover(ApprovalRequirement approval, Guid userId, string? justification)
    {
        switch (approval)
        {
            case ApprovalRequirement.IndependentApprover when Analysts.Contains(userId):
                throw new DomainException(
                    "Esta aprovação exige alguém que não participou da análise. Justificativa não substitui a independência.",
                    "tax_approval_requires_independent_approver");
            case ApprovalRequirement.IdentifiedReviewer when !Analysts.Any(a => a != userId):
                throw new DomainException(
                    "Esta aprovação exige que outra pessoa identificada tenha revisado a análise. Justificativa não substitui o revisor.",
                    "tax_approval_requires_reviewer");
            case ApprovalRequirement.SelfApprovalWithJustification when IsSoleAnalyst(userId) && string.IsNullOrWhiteSpace(justification):
                throw new DomainException(
                    "Quem fez a análise sozinho não aprova a própria análise sem justificar. Peça a outra pessoa para revisar ou registre a justificativa.",
                    "tax_self_approval_requires_justification");
        }
    }

    /// <summary>
    /// Execution verification: after the operation, whether it really happened as the scenario was
    /// approved. Verified needs every execution requirement met with its evidence (the authorised
    /// fiscal document…); a divergence is recorded with what diverged. Final either way.
    /// </summary>
    public void VerifyExecution(TaxExecutionOutcome outcome, string? notes, Guid userId, string userName, DateTime atUtc)
    {
        if (Status != TaxOpportunityStatus.Approved)
            throw new DomainException("Só um cenário aprovado pode ter a execução verificada.", "tax_opportunity_execution_state");
        var execution = _requirements.Where(r => r.Phase == RequirementPhase.ExecutionVerification).ToList();
        if (outcome == TaxExecutionOutcome.Verified)
        {
            var pending = execution.Where(r => r.Met != true).Select(r => r.Text).ToList();
            if (pending.Count > 0)
                throw new DomainException($"Há requisitos da execução não confirmados: {string.Join("; ", pending)}", "tax_execution_requirements");
            var unsupported = execution.Where(r => !HasRequiredEvidence(r)).Select(r => r.Text).ToList();
            if (unsupported.Count > 0)
                throw new DomainException($"Falta evidência final para: {string.Join("; ", unsupported)}", "tax_execution_evidence_missing");
        }
        else if (string.IsNullOrWhiteSpace(notes))
            throw new DomainException("Descreva o que divergiu do cenário aprovado.", "tax_execution_divergence_notes");

        Status = outcome == TaxExecutionOutcome.Verified ? TaxOpportunityStatus.ExecutionVerified : TaxOpportunityStatus.ExecutionDiverged;
        ReviewedById = userId;
        ReviewedByName = userName;
        ReviewedAt = atUtc;
        SetUpdatedAt();
    }

    private void EnsureOpen()
    {
        if (Status is TaxOpportunityStatus.Approved or TaxOpportunityStatus.Rejected or TaxOpportunityStatus.Blocked
            or TaxOpportunityStatus.ExecutionVerified or TaxOpportunityStatus.ExecutionDiverged)
            throw new DomainException("Esta oportunidade já foi encerrada.", "tax_opportunity_closed");
    }
}

/// <summary>Something that must be true for the opportunity to be lawful, with its legal basis.</summary>
public class TaxOpportunityRequirement : StoreEntity
{
    private TaxOpportunityRequirement() { }   // EF Core

    internal TaxOpportunityRequirement(Guid tenantId, Guid opportunityId, string code, string text, string? legalSourceCode,
        RequirementPhase phase, TaxEvidenceType? requiredEvidence)
        : base(tenantId)
    {
        OpportunityId = opportunityId;
        Code = code;
        Text = text;
        LegalSourceCode = legalSourceCode;
        Phase = phase;
        RequiredEvidence = requiredEvidence;
    }

    /// <summary>Checked before the operation (scenario) or after it happened (execution).</summary>
    public RequirementPhase Phase { get; private set; } = RequirementPhase.ScenarioApproval;
    /// <summary>Evidence type that must be linked to this requirement, when the requirement asks for one.</summary>
    public TaxEvidenceType? RequiredEvidence { get; private set; }

    public Guid OpportunityId { get; private set; }
    /// <summary>Stable code the detector and the guardrails recognise, e.g. "no-later-event".</summary>
    public string Code { get; private set; } = string.Empty;
    public string Text { get; private set; } = string.Empty;
    public string? LegalSourceCode { get; private set; }
    /// <summary>Null = not answered yet.</summary>
    public bool? Met { get; private set; }
    public string? Note { get; private set; }
    public Guid? AnsweredById { get; private set; }
    public string? AnsweredByName { get; private set; }
    public DateTime? AnsweredAt { get; private set; }

    internal void Answer(bool met, string? note, Guid userId, string userName, DateTime atUtc)
    {
        Met = met;
        Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        AnsweredById = userId;
        AnsweredByName = userName;
        AnsweredAt = atUtc;
        SetUpdatedAt();
    }
}

/// <summary>
/// Evidence attached during the analysis or the execution check (a proposal, a contract, the
/// authorised fiscal document…), typed and linked to the requirement it supports. Content never
/// changes; only its review status moves (accepted / rejected, with who and why).
/// </summary>
public class TaxOpportunityEvidence : StoreEntity
{
    private TaxOpportunityEvidence() { }   // EF Core

    internal TaxOpportunityEvidence(Guid tenantId, Guid opportunityId, TaxEvidenceType type, Guid? requirementId, string description,
        string? documentReference, string contentHash, string? notes, Guid addedById, string addedByName) : base(tenantId)
    {
        OpportunityId = opportunityId;
        Type = type;
        RequirementId = requirementId;
        Description = description.Trim();
        DocumentReference = string.IsNullOrWhiteSpace(documentReference) ? null : documentReference.Trim();
        ContentHash = contentHash;
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        Status = TaxEvidenceStatus.Submitted;
        AddedById = addedById;
        AddedByName = addedByName;
    }

    public Guid OpportunityId { get; private set; }
    public TaxEvidenceType Type { get; private set; }
    /// <summary>Requirement this evidence supports, if any.</summary>
    public Guid? RequirementId { get; private set; }
    public string Description { get; private set; } = string.Empty;
    /// <summary>
    /// Where the document is (access key of an NF-e, contract number, a reference in the company's own
    /// archive). No file is stored by this version — private upload is a next step.
    /// </summary>
    public string? DocumentReference { get; private set; }
    public string ContentHash { get; private set; } = string.Empty;
    public string? Notes { get; private set; }
    public TaxEvidenceStatus Status { get; private set; }
    public Guid AddedById { get; private set; }
    public string AddedByName { get; private set; } = string.Empty;
    public Guid? ReviewedById { get; private set; }
    public string? ReviewedByName { get; private set; }
    public DateTime? ReviewedAt { get; private set; }
    public string? ReviewNotes { get; private set; }

    internal void Review(bool accepted, string? notes, Guid userId, string userName, DateTime atUtc)
    {
        Status = accepted ? TaxEvidenceStatus.Accepted : TaxEvidenceStatus.Rejected;
        ReviewedById = userId;
        ReviewedByName = userName;
        ReviewedAt = atUtc;
        ReviewNotes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        SetUpdatedAt();
    }
}
