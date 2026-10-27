using Nexo.Application.Modules.TaxIntelligence.Engine;
using Nexo.Domain.Enums;
using Nexo.Domain.Modules.TaxIntelligence;
using Nexo.Infrastructure.TaxIntelligence.Catalog;

namespace Nexo.UnitTests.TaxIntelligence;

internal static class TaxTestData
{
    public static readonly DateOnly Mid2026 = new(2026, 6, 15);

    /// <summary>The seeded catalogue as the engine sees it.</summary>
    public static IReadOnlyList<TaxEngineRule> Catalogue() => TaxCatalogV1.Rules
        .Select(r => new TaxEngineRule(r.VersionId, r.Key, 1, r.Status, r.ValidFrom, r.ValidTo, r.Priority, r.Conditions, r.Result,
            r.ContentHash, r.SourceCodes))
        .ToList();

    public static TaxEngineProfile Profile(TaxRegime regime = TaxRegime.LucroPresumido, bool? compliant = null)
        => new(Guid.Parse("11111111-1111-4111-8111-111111111111"), regime, "SP", "3550308", compliant);

    /// <summary>Gross 1000, unconditional 100, conditional 50, surcharges 20, taxes in the price 80 → base 840.</summary>
    public static TaxEngineLine Line(int index = 1, bool informed = true, string? special = null)
        => new(index, TaxItemKind.Good, 1000m, 100m, 50m, 20m, informed ? 80m : 0m, informed, special);

    public static TaxEngineInput Input(
        TaxRegime regime = TaxRegime.LucroPresumido, DateOnly? date = null, TaxDirection direction = TaxDirection.Outbound,
        TaxRegime? counterparty = null, bool? compliant = null, params TaxEngineLine[] lines)
        => new(date ?? Mid2026, Profile(regime, compliant), direction, counterparty, lines.Length == 0 ? [Line()] : lines);

    public static TaxEngineRule Rate(string key, int version, decimal rate, DateOnly from, DateOnly? to,
        TaxRuleStatus status = TaxRuleStatus.Active, TaxType tax = TaxType.Cbs)
    {
        TaxRuleResult result = new RateResult(tax, rate);
        IReadOnlyList<TaxCondition> conditions = [];
        return new TaxEngineRule(Guid.NewGuid(), key, version, status, from, to, 10, conditions, result,
            TaxCanonical.RuleContentHash(key, version, from, to, 10, conditions, result), ["TEST-SOURCE"]);
    }
}
