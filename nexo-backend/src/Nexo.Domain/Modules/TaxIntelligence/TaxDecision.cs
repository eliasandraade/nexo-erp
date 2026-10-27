using Nexo.Domain.Common;

namespace Nexo.Domain.Modules.TaxIntelligence;

/// <summary>
/// The dossier of an approved opportunity — what an auditor or a fiscal inspection would ask for,
/// frozen at approval: the strategy, the facts, the calculation, the rules and versions, the legal
/// basis, the evidence, who approved and when, the risk and the engine version. Immutable; one per
/// opportunity. <see cref="DossierHash"/> fingerprints the whole content.
/// </summary>
public class TaxDecision : StoreEntity
{
    private TaxDecision() { }   // EF Core
    private TaxDecision(Guid tenantId) : base(tenantId) { }

    public Guid OpportunityId { get; private set; }
    public string Strategy { get; private set; } = string.Empty;
    public string DossierJson { get; private set; } = "{}";
    public string DossierHash { get; private set; } = string.Empty;
    public TaxRiskLevel Risk { get; private set; }
    public bool HighRiskAcknowledged { get; private set; }
    /// <summary>Set when the approver also did the whole analysis (segregation of duties waived, with reason).</summary>
    public string? SelfApprovalJustification { get; private set; }
    public string EngineVersion { get; private set; } = string.Empty;
    public Guid ApprovedById { get; private set; }
    public string ApprovedByName { get; private set; } = string.Empty;
    public DateTime ApprovedAt { get; private set; }

    public static TaxDecision Create(
        Guid tenantId, Guid opportunityId, string strategy, string dossierJson, string dossierHash, TaxRiskLevel risk,
        bool highRiskAcknowledged, string engineVersion, Guid approvedById, string approvedByName, DateTime approvedAtUtc,
        string? selfApprovalJustification = null)
        => new(tenantId)
        {
            OpportunityId = opportunityId,
            Strategy = strategy,
            DossierJson = dossierJson,
            DossierHash = dossierHash,
            Risk = risk,
            HighRiskAcknowledged = highRiskAcknowledged,
            SelfApprovalJustification = string.IsNullOrWhiteSpace(selfApprovalJustification) ? null : selfApprovalJustification.Trim(),
            EngineVersion = engineVersion,
            ApprovedById = approvedById,
            ApprovedByName = approvedByName,
            ApprovedAt = approvedAtUtc,
        };
}
