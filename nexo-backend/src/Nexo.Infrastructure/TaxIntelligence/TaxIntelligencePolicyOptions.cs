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

    /// <summary>
    /// Segregation of duties: whoever did the whole analysis alone must justify approving it
    /// (small teams can, on record) instead of being silently the sole approver. Default: on.
    /// </summary>
    public bool RequireSeparateApprover { get; set; } = true;
}
