using Nexo.Application.Modules.TaxIntelligence.Engine;
using Nexo.Domain.Enums;
using Nexo.Domain.Modules.TaxIntelligence;

namespace Nexo.Infrastructure.TaxIntelligence.Catalog;

/// <summary>
/// Versions added to catalogue V1 after it shipped, as typed engine vocabulary. Like the original
/// catalogue, the migration that inserts them (TaxIntelligenceReviewDelta) carries literal rows and
/// never reads this class; the unit tests check both agree under the frozen canonical form V1.
/// FROZEN once released.
/// </summary>
public static class TaxCatalogV1Amendments
{
    public const string Origin = "seed:tax-catalog-v1.1";

    /// <summary>
    /// IBSCBS.NOTE.2026-WAIVER v2: same conditions, validity and priority as v1; only the text changes.
    /// v1 said "Perfil declara obrigações acessórias em dia", but the rule fires only on a verified
    /// assessment. v1 stays (Superseded) so calculations made with it reproduce. Professional review Pending.
    /// </summary>
    public static readonly Guid WaiverRuleId = Guid.Parse("7a9c2e00-0000-4000-8000-000000000009");
    public static readonly Guid WaiverV1Id = Guid.Parse("7a9c3e00-0000-4000-8000-000000000009");
    public static readonly Guid WaiverV2Id = Guid.Parse("7a9c3e00-0000-4000-8000-000000000109");
    public const string WaiverKey = "IBSCBS.NOTE.2026-WAIVER";
    public static readonly DateOnly WaiverFrom = new(2026, 1, 1);
    public static readonly DateOnly WaiverTo = new(2026, 12, 31);
    public const int WaiverPriority = 30;

    public static readonly IReadOnlyList<TaxCondition> WaiverConditions =
    [
        new DirectionIs(TaxDirection.Outbound),
        new SupplierRegimeNotIn([TaxRegime.SimplesNacional, TaxRegime.SimplesNacionalExcessoSublimite, TaxRegime.Mei]),
        new ProfileFlagIs(TaxProfileFlags.AccessoryObligationsCompliant, true),
    ];

    public static readonly TaxRuleResult WaiverResult = new NoteResult(TaxNoteCodes.Waiver2026,
        "Obrigações acessórias verificadas no período e no escopo indicados: o art. 348, § 1º, da LC 214/2025 dispensa o recolhimento do IBS e da CBS de 2026 de quem cumpre as obrigações acessórias; PIS e Cofins continuam devidos integralmente (§ 2º).");

    public const string WaiverV2Notes =
        "Nova redação da nota: a v1 dizia \"Perfil declara…\", mas a regra só dispara com cumprimento verificado. Condições, vigência e prioridade iguais às da v1. Revisão profissional pendente.";

    public static string WaiverV2ContentHash => TaxCanonicalV1.Instance.RuleContentHash(
        WaiverKey, 2, WaiverFrom, WaiverTo, WaiverPriority, WaiverConditions, WaiverResult);
}
