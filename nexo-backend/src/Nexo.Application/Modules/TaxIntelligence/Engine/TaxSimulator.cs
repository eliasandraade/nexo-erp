using Nexo.Domain.Enums;
using Nexo.Domain.Modules.TaxIntelligence;

namespace Nexo.Application.Modules.TaxIntelligence.Engine;

/// <summary>A rate the law has not fixed yet, assumed by the user for a scenario. Always labelled.</summary>
public sealed record RateHypothesis(TaxType Tax, decimal RatePercent, string Label);

/// <summary>
/// What a scenario changes in the baseline. Anything left null keeps the baseline value. Lines, when
/// given, replace the baseline lines (another supplier, another price, another discount).
/// </summary>
public sealed record TaxScenario(
    string Name,
    TaxRegime? Regime = null,
    DateOnly? ReferenceDate = null,
    TaxDirection? Direction = null,
    TaxRegime? CounterpartyRegime = null,
    IReadOnlyList<TaxEngineLine>? Lines = null,
    bool ConvertConditionalDiscounts = false,
    IReadOnlyList<RateHypothesis>? RateHypotheses = null,
    bool AssumeCreditable = false);

/// <summary>
/// The outcome of one scenario. <see cref="Charged"/> is what changes hands (price after discounts,
/// plus surcharges, taxes included as the source stated them). <see cref="EffectiveCost"/> is, for an
/// acquisition, what it costs after the estimated credit; for a supply, the tax burden.
/// </summary>
public sealed record ScenarioResult(
    string Name,
    bool IsBaseline,
    TaxEngineInput Input,
    TaxEngineOutput Output,
    decimal Charged,
    decimal TaxTotal,
    decimal CreditEstimate,
    decimal EffectiveCost,
    decimal TaxDeltaVsBaseline,
    decimal EffectiveCostDeltaVsBaseline,
    bool NoCashEffectInPeriod,
    IReadOnlyList<string> Hypotheses);

/// <summary>
/// Runs what-if scenarios in memory with the same engine as official calculations. It returns
/// results and nothing else: callers persist them as a simulation, never as a calculation, a credit,
/// a ledger entry or a split expectation.
/// </summary>
public static class TaxSimulator
{
    public const string HypothesisKeyPrefix = "HYPOTHESIS.";

    /// <summary>Runs the baseline and each scenario with the given engine version (new work uses the current one).</summary>
    public static IReadOnlyList<ScenarioResult> Compare(
        ITaxEngine engine, TaxEngineInput baseline, IReadOnlyList<TaxScenario> scenarios, IReadOnlyCollection<TaxEngineRule> catalogue)
    {
        var baseResult = Run(engine, "Cenário atual", isBaseline: true, baseline, new TaxScenario("Cenário atual"), catalogue, null);
        var results = new List<ScenarioResult> { baseResult };
        foreach (var s in scenarios)
            results.Add(Run(engine, s.Name, isBaseline: false, Apply(baseline, s), s, catalogue, baseResult));
        return results;
    }

    /// <summary>
    /// Runs one scenario on its own facts, with no baseline to inherit from: a value the input leaves
    /// unknown (the supplier regime, say) stays unknown. Deltas are zero.
    /// </summary>
    public static ScenarioResult RunAlone(ITaxEngine engine, TaxEngineInput input, TaxScenario s, IReadOnlyCollection<TaxEngineRule> catalogue)
        => Run(engine, s.Name, isBaseline: false, Apply(input, s), s, catalogue, null);

    public static TaxEngineInput Apply(TaxEngineInput baseline, TaxScenario s)
    {
        var lines = (s.Lines ?? baseline.Lines).Select(l => s.ConvertConditionalDiscounts
            ? l with { UnconditionalDiscount = l.UnconditionalDiscount + l.ConditionalDiscount, ConditionalDiscount = 0m }
            : l).ToList();
        return baseline with
        {
            ReferenceDate = s.ReferenceDate ?? baseline.ReferenceDate,
            Profile = s.Regime is null ? baseline.Profile : baseline.Profile with { Regime = s.Regime.Value },
            Direction = s.Direction ?? baseline.Direction,
            CounterpartyRegime = s.CounterpartyRegime ?? baseline.CounterpartyRegime,
            Lines = lines,
        };
    }

    private static ScenarioResult Run(
        ITaxEngine engine, string name, bool isBaseline, TaxEngineInput input, TaxScenario s, IReadOnlyCollection<TaxEngineRule> catalogue,
        ScenarioResult? baseline)
    {
        var hypotheses = new List<string>();
        var rules = WithHypotheses(engine.Canonical, catalogue, s.RateHypotheses ?? [], hypotheses);
        var output = engine.Run(input, rules);

        var charged = input.Lines.Sum(l => l.Gross - l.UnconditionalDiscount - l.ConditionalDiscount + l.Surcharges);
        var credit = 0m;
        if (s.AssumeCreditable && input.Direction == TaxDirection.Inbound)
        {
            credit = output.TotalTax;
            hypotheses.Add("Crédito estimado: supõe que o adquirente está no regime regular e que o débito do fornecedor foi extinto (LC 214/2025, art. 47). Não é crédito apropriado.");
        }
        var effective = input.Direction == TaxDirection.Inbound ? charged - credit : output.TotalTax;
        var noCash = output.Notes.Any(n => TaxNoteCodes.NoCashEffect.Contains(n.Code));

        return new ScenarioResult(
            name, isBaseline, input, output, engine.RoundMoney(charged), output.TotalTax, engine.RoundMoney(credit), engine.RoundMoney(effective),
            TaxDeltaVsBaseline: baseline is null ? 0m : output.TotalTax - baseline.TaxTotal,
            EffectiveCostDeltaVsBaseline: baseline is null ? 0m : engine.RoundMoney(effective) - baseline.EffectiveCost,
            noCash, hypotheses);
    }

    /// <summary>
    /// A rate hypothesis replaces the catalogue's rate rules for that tax in this scenario only. It
    /// applies to suppliers under the regular regime — IBS/CBS inside the Simples Nacional is not simulated.
    /// </summary>
    private static IReadOnlyCollection<TaxEngineRule> WithHypotheses(
        ITaxCanonicalizer canonical, IReadOnlyCollection<TaxEngineRule> catalogue, IReadOnlyList<RateHypothesis> hypotheses, List<string> labels)
    {
        if (hypotheses.Count == 0) return catalogue;
        var taxes = hypotheses.Select(h => h.Tax).ToHashSet();
        var rules = catalogue.Where(r => r.Result is not RateResult rate || !taxes.Contains(rate.Tax)).ToList();
        foreach (var h in hypotheses.GroupBy(h => h.Tax).Select(g => g.Last()))
        {
            if (h.RatePercent is < 0 or > 100) throw new Nexo.Domain.Exceptions.DomainException("Alíquota hipotética fora de 0–100%.");
            var key = HypothesisKeyPrefix + h.Tax.ToString().ToUpperInvariant();
            IReadOnlyList<TaxCondition> conditions =
                [new SupplierRegimeNotIn([TaxRegime.SimplesNacional, TaxRegime.SimplesNacionalExcessoSublimite, TaxRegime.Mei])];
            TaxRuleResult result = new RateResult(h.Tax, h.RatePercent);
            var hash = canonical.RuleContentHash(key, 1, DateOnly.MinValue, null, 0, conditions, result);
            rules.Add(new TaxEngineRule(Guid.Empty, key, 1, TaxRuleStatus.Active, DateOnly.MinValue, null, 0, conditions, result, hash, [], IsHypothesis: true));
            labels.Add(FormattableString.Invariant($"Hipótese: {h.Tax} a {h.RatePercent}% — {h.Label}"));
        }
        return rules;
    }
}
