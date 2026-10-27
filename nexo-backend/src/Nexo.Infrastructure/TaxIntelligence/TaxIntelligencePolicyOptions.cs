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
    /// An opportunity can be approved only if every rule version behind it was approved by a tax
    /// professional. Technical activation of a rule is not professional approval. Default: on, except
    /// in Development and Testing (set in DependencyInjection); explicit configuration always wins.
    /// </summary>
    public bool RequireApprovedRulesForDecisions { get; set; } = true;

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
