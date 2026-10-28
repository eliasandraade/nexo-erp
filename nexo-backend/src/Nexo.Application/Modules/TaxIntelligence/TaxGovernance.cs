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
    public const string DisabledPolicy = "orken-approval-disabled";

    public const string PilotNature = "INTERNAL PRODUCT / GOVERNANCE APPROVAL FOR CONTROLLED PILOT";
    public const string ProfessionalNature = "PROFESSIONAL FINAL APPROVAL";
    public const string DisabledNature = "NO APPROVAL MODE CONFIGURED";

    public const string NotAProfessionalOpinion =
        "Decisão interna de produto e governança da Andrade Systems para o piloto controlado. Não constitui parecer jurídico, contábil ou tributário profissional.";

    /// <summary>The controlled pilot is a shadow/advisory mode: it never acts on anything fiscal by itself.</summary>
    public const string ShadowNotice =
        "Piloto controlado — resultados para análise e validação. Não executa alteração fiscal, contábil ou financeira automaticamente.";

    public const string DossierNature =
        "Registro auditável da decisão e das premissas utilizadas pelo motor. Não é prova definitiva de conformidade fiscal.";

    public static string PolicyFor(TaxApprovalMode mode) => mode switch
    {
        TaxApprovalMode.ControlledPilot => PilotPolicy,
        TaxApprovalMode.ProfessionalFinal => ProfessionalPolicy,
        _ => DisabledPolicy,
    };

    public static string NatureOf(TaxApprovalMode mode) => mode switch
    {
        TaxApprovalMode.ControlledPilot => PilotNature,
        TaxApprovalMode.ProfessionalFinal => ProfessionalNature,
        _ => DisabledNature,
    };

    public static string NoticeOf(TaxApprovalMode mode) => mode switch
    {
        TaxApprovalMode.ControlledPilot => NotAProfessionalOpinion,
        TaxApprovalMode.ProfessionalFinal => "Somente aprovação profissional registrada é aceita.",
        _ => "Nenhum modo de aprovação configurado: nenhuma regra é aceita onde a aprovação é exigida.",
    };

    /// <summary>
    /// Whether a rule version backs new work in a mode. It must be technically active now; then a professional
    /// approval satisfies either mode and the internal pilot approval only the controlled pilot. No mode is a fallback.
    /// </summary>
    public static bool Satisfies(TaxApprovalMode mode, bool technicallyActive, bool approvedForPilot, bool professionallyApproved)
        => technicallyActive && mode switch
        {
            TaxApprovalMode.ControlledPilot => professionallyApproved || approvedForPilot,
            TaxApprovalMode.ProfessionalFinal => professionallyApproved,
            _ => false,
        };
}

/// <summary>
/// Both approval dimensions of one rule version as they stood when the snapshot was taken — the internal
/// approval with its own policy, actor, scope, validity, caveats and decision reference, and the
/// professional review. A frozen snapshot keeps them even if the version is later reapproved, withdrawn,
/// revoked or superseded.
/// </summary>
public sealed record TaxRuleGovernanceDto(
    Guid RuleVersionId, string Key, int Version, TaxRuleStatus TechnicalStatus,
    InternalApprovalStatus InternalApproval, bool ApprovedForPilot, string? InternalApprovalPolicy,
    Guid? InternalApprovedById, string? InternalApprovedBy, string? InternalApprovedByRole, DateTime? InternalApprovedAt,
    string? InternalApprovalScope, DateOnly? InternalApprovalValidFrom, DateOnly? InternalApprovalValidTo, string? PilotCaveats,
    string? InternalApprovalDecisionRef,
    ProfessionalReviewStatus ProfessionalReview, bool ProfessionallyApproved, string? ProfessionalReviewScope, string? ProfessionalCaveats);

/// <summary>The pilot scope as it stood: the decision that opened it, its window and who activated it.</summary>
public sealed record TaxPilotScopeSnapshotDto(
    string DecisionReference, DateOnly ValidFrom, DateOnly? ValidTo, string ActivatedByName, DateTime ActivatedAt);

/// <summary>The approval governance a calculation or a decision ran under: mode, policy, enforcement, scope and each rule.</summary>
public sealed record TaxGovernanceDto(
    TaxApprovalMode Mode, string Policy, bool Enforced, string Nature, string Notice, string? EffectsNotice,
    TaxPilotScopeSnapshotDto? PilotScope, IReadOnlyList<TaxRuleGovernanceDto> Rules)
{
    /// <summary>Rule versions (key vN) that do not back new work in the mode.</summary>
    public IReadOnlyList<string> NotApproved => Rules
        .Where(r => !TaxGovernance.Satisfies(Mode, r.TechnicalStatus == TaxRuleStatus.Active, r.ApprovedForPilot, r.ProfessionallyApproved))
        .Select(r => $"{r.Key} v{r.Version}").ToList();

    /// <summary>Rule versions (key vN) whose professional review is not an approval for the date.</summary>
    public IReadOnlyList<string> ProfessionalReviewPending => Rules
        .Where(r => !r.ProfessionallyApproved).Select(r => $"{r.Key} v{r.Version}").ToList();

    /// <summary>The exact internal decision records behind the rules — one per distinct decision, never a global stand-in.</summary>
    public IReadOnlyList<string> DecisionReferences => Rules
        .Select(r => r.InternalApprovalDecisionRef).Where(d => !string.IsNullOrWhiteSpace(d)).Select(d => d!)
        .Distinct(StringComparer.Ordinal).OrderBy(d => d, StringComparer.Ordinal).ToList();
}

/// <summary>
/// Where the current tenant/store stands today: kill switch, mode, whether new work is admitted (and why
/// not), the shadow-mode notice of the pilot and the pilot scope. No rule is evaluated.
/// </summary>
public sealed record TaxGovernanceStatusDto(
    bool Enabled, TaxApprovalMode Mode, string Policy, bool Admitted, string? RefusalCode, string? RefusalMessage,
    string? EffectsNotice, TaxPilotScopeSnapshotDto? PilotScope);
