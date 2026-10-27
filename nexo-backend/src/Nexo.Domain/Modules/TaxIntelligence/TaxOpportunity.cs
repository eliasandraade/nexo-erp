using Nexo.Domain.Common;
using Nexo.Domain.Exceptions;

namespace Nexo.Domain.Modules.TaxIntelligence;

/// <summary>
/// Something the engine found that might lower taxes, defer them, free cash or reduce risk. It is a
/// proposal to analyse — nothing is ever applied automatically. The workflow is
/// Identified → InReview → Approved (which records a <see cref="TaxDecision"/>) or Rejected; at any
/// point before approval it can be Blocked when it matches a <see cref="ProhibitedStructure"/>, and a
/// blocked opportunity shows no saving and can never be approved.
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

    public TaxOpportunityRequirement AddRequirement(string code, string text, string? legalSourceCode)
    {
        var r = new TaxOpportunityRequirement(TenantId, Id, code, text, legalSourceCode);
        _requirements.Add(r);
        return r;
    }

    public void StartReview(Guid userId, string userName, DateTime atUtc)
    {
        EnsureOpen();
        if (Status != TaxOpportunityStatus.Identified)
            throw new DomainException("A oportunidade já está em análise.", "tax_opportunity_state");
        Status = TaxOpportunityStatus.InReview;
        ReviewStartedById = userId;
        ReviewStartedByName = userName;
        ReviewedById = userId;
        ReviewedByName = userName;
        ReviewedAt = atUtc;
        SetUpdatedAt();
    }

    /// <summary>Answers one requirement. A requirement answered "not met" blocks approval until changed.</summary>
    public void AnswerRequirement(Guid requirementId, bool met, string? note, Guid userId, string userName, DateTime atUtc)
    {
        if (Status != TaxOpportunityStatus.InReview)
            throw new DomainException("Abra a análise antes de responder aos requisitos.", "tax_opportunity_state");
        var r = _requirements.FirstOrDefault(x => x.Id == requirementId)
            ?? throw new NotFoundException("Requisito", requirementId);
        r.Answer(met, note, userId, userName, atUtc);
        SetUpdatedAt();
    }

    public TaxOpportunityEvidence AddEvidence(string description, string? documentReference, string contentHash, Guid userId, string userName)
    {
        EnsureOpen();
        if (string.IsNullOrWhiteSpace(description)) throw new DomainException("Descreva a evidência.");
        var e = new TaxOpportunityEvidence(TenantId, Id, description, documentReference, contentHash, userId, userName);
        _evidence.Add(e);
        SetUpdatedAt();
        return e;
    }

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

    /// <summary>People who analysed the opportunity: who opened the review, answered requirements or added evidence.</summary>
    public IReadOnlySet<Guid> Analysts => new[] { ReviewStartedById }
        .Concat(_requirements.Select(r => r.AnsweredById))
        .Concat(_evidence.Select(e => (Guid?)e.AddedById))
        .Where(id => id is not null).Select(id => id!.Value).ToHashSet();

    /// <summary>Whether this approver did the whole analysis alone (segregation of duties).</summary>
    public bool IsSoleAnalyst(Guid userId) => Analysts.Count > 0 && Analysts.All(a => a == userId);

    /// <summary>
    /// Approval checks. It records nothing but the status — the decision dossier is written by the
    /// caller in the same transaction. Every requirement must be met; a HIGH risk needs the approver
    /// to acknowledge it explicitly; and, with segregation of duties on, whoever analysed it alone
    /// cannot be the only approver unless a justification is recorded (small teams).
    /// </summary>
    public void Approve(bool highRiskAcknowledged, Guid userId, string userName, DateTime atUtc,
        bool requireSeparateApprover = false, string? selfApprovalJustification = null)
    {
        if (Status == TaxOpportunityStatus.Blocked)
            throw new DomainException("Uma estrutura proibida não pode ser aprovada.", "tax_opportunity_blocked");
        if (Status != TaxOpportunityStatus.InReview)
            throw new DomainException("Só uma oportunidade em análise pode ser aprovada.", "tax_opportunity_state");
        var pending = _requirements.Where(r => r.Met != true).Select(r => r.Text).ToList();
        if (pending.Count > 0)
            throw new DomainException($"Há requisitos não atendidos: {string.Join("; ", pending)}", "tax_opportunity_requirements");
        if (Risk == TaxRiskLevel.High && !highRiskAcknowledged)
            throw new DomainException("Risco alto: a aprovação precisa reconhecer o risco expressamente.", "tax_opportunity_high_risk");
        if (requireSeparateApprover && IsSoleAnalyst(userId) && string.IsNullOrWhiteSpace(selfApprovalJustification))
            throw new DomainException(
                "Quem fez a análise sozinho não aprova a própria análise sem justificar. Peça a outra pessoa para revisar ou registre a justificativa.",
                "tax_self_approval_requires_justification");

        Status = TaxOpportunityStatus.Approved;
        ReviewedById = userId;
        ReviewedByName = userName;
        ReviewedAt = atUtc;
        SetUpdatedAt();
    }

    private void EnsureOpen()
    {
        if (Status is TaxOpportunityStatus.Approved or TaxOpportunityStatus.Rejected or TaxOpportunityStatus.Blocked)
            throw new DomainException("Esta oportunidade já foi encerrada.", "tax_opportunity_closed");
    }
}

/// <summary>Something that must be true for the opportunity to be lawful, with its legal basis.</summary>
public class TaxOpportunityRequirement : StoreEntity
{
    private TaxOpportunityRequirement() { }   // EF Core

    internal TaxOpportunityRequirement(Guid tenantId, Guid opportunityId, string code, string text, string? legalSourceCode)
        : base(tenantId)
    {
        OpportunityId = opportunityId;
        Code = code;
        Text = text;
        LegalSourceCode = legalSourceCode;
    }

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

/// <summary>Evidence attached during review (a document reference, a contract clause…). Append-only.</summary>
public class TaxOpportunityEvidence : StoreEntity
{
    private TaxOpportunityEvidence() { }   // EF Core

    internal TaxOpportunityEvidence(Guid tenantId, Guid opportunityId, string description, string? documentReference,
        string contentHash, Guid addedById, string addedByName) : base(tenantId)
    {
        OpportunityId = opportunityId;
        Description = description.Trim();
        DocumentReference = string.IsNullOrWhiteSpace(documentReference) ? null : documentReference.Trim();
        ContentHash = contentHash;
        AddedById = addedById;
        AddedByName = addedByName;
    }

    public Guid OpportunityId { get; private set; }
    public string Description { get; private set; } = string.Empty;
    /// <summary>E.g. the access key of an NF-e or a contract number. No file is stored in this version.</summary>
    public string? DocumentReference { get; private set; }
    public string ContentHash { get; private set; } = string.Empty;
    public Guid AddedById { get; private set; }
    public string AddedByName { get; private set; } = string.Empty;
}
