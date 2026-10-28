using Nexo.Domain.Modules.TaxIntelligence;

namespace Nexo.Infrastructure.TaxIntelligence;

/// <summary>
/// Approval policy of Tax Intelligence (section <c>TaxIntelligence:Policy</c>). The defaults are the
/// conservative production stance; Development and Testing relax only what would make the catalogue
/// unusable before a tax professional reviews it.
/// </summary>
public sealed class TaxIntelligencePolicyOptions
{
    public const string Section = "TaxIntelligence:Policy";

    /// <summary>
    /// Which approval a rule version needs (see <see cref="TaxApprovalMode"/>). Default: the controlled
    /// pilot authorised by the product's governance decision — the internal pilot approval (or a
    /// professional one) suffices. <see cref="TaxApprovalMode.ProfessionalFinal"/> is the definitive
    /// production mode: only a professional approval counts. The internal approval is never a professional one.
    /// </summary>
    public TaxApprovalMode Mode { get; set; } = TaxApprovalMode.ControlledPilot;

    /// <summary>
    /// An opportunity can be approved only if every rule version behind it is approved for the
    /// <see cref="Mode"/>. Technical activation of a rule is no approval at all. Default: on, except in
    /// Development and Testing (set in DependencyInjection); explicit configuration always wins.
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
