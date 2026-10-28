using Nexo.Domain.Modules.TaxIntelligence;

namespace Nexo.Infrastructure.TaxIntelligence;

/// <summary>
/// Approval policy of Tax Intelligence (section <c>TaxIntelligence:Policy</c>). Everything defaults to
/// fail closed: with nothing configured the module runs no rule and records no decision. Using it takes
/// explicit configuration — <see cref="Enabled"/>, a <see cref="Mode"/> and, for the controlled pilot, a
/// scope kept by the platform. Development and Testing may relax only the approval enforcement.
/// </summary>
public sealed class TaxIntelligencePolicyOptions
{
    public const string Section = "TaxIntelligence:Policy";

    /// <summary>
    /// Global kill switch. Off (the default, and whenever it is not configured): no calculation,
    /// simulation, detection, decision or execution verification runs. Reading and reproducing what was
    /// already recorded stays available.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Which approval a rule version needs (see <see cref="TaxApprovalMode"/>). Default
    /// <see cref="TaxApprovalMode.Disabled"/>: no approval is accepted. The controlled pilot is entered only
    /// by configuring <c>Mode=ControlledPilot</c> explicitly — it then accepts the internal pilot approval (or a
    /// professional one), inside the pilot scope. <see cref="TaxApprovalMode.ProfessionalFinal"/> is the
    /// definitive mode: only a professional approval counts. The internal approval is never a professional one.
    /// </summary>
    public TaxApprovalMode Mode { get; set; } = TaxApprovalMode.Disabled;

    /// <summary>
    /// An opportunity can be approved only if every rule version behind it is approved for the
    /// <see cref="Mode"/> (and, in the pilot, the tenant/store is in the pilot scope). Technical activation
    /// of a rule is no approval at all. Default: on, except in Development and Testing (set in
    /// DependencyInjection, which never changes the Mode); explicit configuration always wins.
    /// </summary>
    public bool RequireApprovedRulesForDecisions { get; set; } = true;

    /// <summary>
    /// A calculation runs only if every active rule version the engine could apply on its date is
    /// approved for the <see cref="Mode"/>; otherwise it is refused (fail closed). Same defaults as above.
    /// </summary>
    public bool RequireApprovedRulesForCalculations { get; set; } = true;

    /// <summary>Maker-checker for LOW risk. Default: whoever analysed alone may approve with a justification.</summary>
    public ApprovalRequirement LowRisk { get; set; } = ApprovalRequirement.SelfApprovalWithJustification;

    /// <summary>Maker-checker for MEDIUM risk. Default: another identified person must have reviewed.</summary>
    public ApprovalRequirement MediumRisk { get; set; } = ApprovalRequirement.IdentifiedReviewer;

    /// <summary>Maker-checker for HIGH risk. Default: an approver who took no part in the analysis.</summary>
    public ApprovalRequirement HighRisk { get; set; } = ApprovalRequirement.IndependentApprover;

    /// <summary>
    /// Estimated impact (R$) at or above which an independent approver is required whatever the risk.
    /// Null = no materiality rule. The amount is a business decision of the owner.
    /// </summary>
    public decimal? MaterialityThreshold { get; set; }

    /// <summary>The strictest approval the risk and the materiality of an opportunity require.</summary>
    public ApprovalRequirement RequiredFor(TaxRiskLevel risk, decimal estimatedImpact)
    {
        var byRisk = risk switch
        {
            TaxRiskLevel.High => HighRisk,
            TaxRiskLevel.Medium => MediumRisk,
            _ => LowRisk,
        };
        var material = MaterialityThreshold is not null && estimatedImpact >= MaterialityThreshold;
        return material && byRisk < ApprovalRequirement.IndependentApprover ? ApprovalRequirement.IndependentApprover : byRisk;
    }
}
