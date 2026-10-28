using Nexo.Domain.Modules.TaxIntelligence;

namespace Nexo.Application.Modules.TaxIntelligence;

/// <summary>
/// Vocabulary of the approval governance. The internal pilot approval is a decision of Andrade Systems'
/// product governance — an INTERNAL PRODUCT / GOVERNANCE APPROVAL FOR CONTROLLED PILOT — and never a
/// professional tax opinion; it is always shown beside the professional review, which keeps its own state.
/// </summary>
public static class TaxGovernance
{
    public const string PilotPolicy = "orken-pilot-approval/1";
    public const string ProfessionalPolicy = "orken-professional-approval/1";

    public const string PilotNature = "INTERNAL PRODUCT / GOVERNANCE APPROVAL FOR CONTROLLED PILOT";
    public const string ProfessionalNature = "PROFESSIONAL FINAL APPROVAL";

    public const string NotAProfessionalOpinion =
        "Decisão interna de produto e governança da Andrade Systems para o piloto controlado. Não constitui parecer jurídico, contábil ou tributário profissional.";

    public const string DecisionRecord = "docs/tax-intelligence/homologation/internal-pilot-approval-2026.md";

    public const string DossierNature =
        "Registro auditável da decisão e das premissas utilizadas pelo motor. Não é prova definitiva de conformidade fiscal.";

    public static string PolicyFor(TaxApprovalMode mode) => mode == TaxApprovalMode.ControlledPilot ? PilotPolicy : ProfessionalPolicy;

    public static string NatureOf(TaxApprovalMode mode) => mode == TaxApprovalMode.ControlledPilot ? PilotNature : ProfessionalNature;

    /// <summary>A professional approval satisfies either mode; the internal pilot approval only the controlled pilot.</summary>
    public static bool Satisfies(TaxApprovalMode mode, bool approvedForPilot, bool professionallyApproved)
        => professionallyApproved || (mode == TaxApprovalMode.ControlledPilot && approvedForPilot);
}

/// <summary>
/// The two approval dimensions of one rule version, side by side: they coexist, and the internal one
/// never stands in for the professional one.
/// </summary>
public sealed record TaxRuleGovernanceDto(
    Guid RuleVersionId, string Key, int Version,
    InternalApprovalStatus InternalApproval, bool ApprovedForPilot, string? PilotCaveats,
    ProfessionalReviewStatus ProfessionalReview, bool ProfessionallyApproved, string? ProfessionalCaveats);

/// <summary>The approval governance a calculation ran under: mode, policy, whether it was enforced, and each rule.</summary>
public sealed record TaxGovernanceDto(
    TaxApprovalMode Mode, string Policy, bool Enforced, string Nature, string Notice, IReadOnlyList<TaxRuleGovernanceDto> Rules)
{
    /// <summary>Rule versions (key vN) that do not satisfy the mode.</summary>
    public IReadOnlyList<string> NotApproved => Rules
        .Where(r => !TaxGovernance.Satisfies(Mode, r.ApprovedForPilot, r.ProfessionallyApproved))
        .Select(r => $"{r.Key} v{r.Version}").ToList();

    /// <summary>Rule versions (key vN) whose professional review is not an approval for the date.</summary>
    public IReadOnlyList<string> ProfessionalReviewPending => Rules
        .Where(r => !r.ProfessionallyApproved).Select(r => $"{r.Key} v{r.Version}").ToList();
}
