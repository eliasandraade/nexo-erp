using Nexo.Domain.Enums;
using Nexo.Domain.Modules.TaxIntelligence;

namespace Nexo.Application.Modules.TaxIntelligence.Engine;

/// <summary>
/// The deterministic tax engine. Given the same input and the same rule versions it always returns
/// the same output, byte for byte: no clock, no database, no randomness, no model inference. It does
/// not know any tax by itself — every part of the base and every rate comes from a rule version,
/// and the output records which rule did what and on which legal basis.
///
/// A run: pick, for each rule key, the highest active version covering the reference date; then per
/// line apply the base rules (starting from the gross price), then the rate rules on that base; then
/// the note rules on the whole operation. Rules that cover the date but are not active are listed as
/// not applied.
/// </summary>
public static class TaxEngine
{
    /// <summary>Bumped whenever the evaluation semantics or the canonical form change.</summary>
    public const string Version = "orken-tax-engine/1.0.0";

    public static IReadOnlyList<TaxEngineRule> SelectActive(IEnumerable<TaxEngineRule> rules, DateOnly date)
        => rules
            .Where(r => r.Status == TaxRuleStatus.Active && r.Covers(date))
            .GroupBy(r => r.Key, StringComparer.Ordinal)
            .Select(g => g.OrderByDescending(r => r.Version).First())
            .OrderBy(r => r.Result.Phase).ThenBy(r => r.Priority).ThenBy(r => r.Key, StringComparer.Ordinal)
            .ToList();

    /// <summary>Hash of the rule versions a run used — part of the calculation key.</summary>
    public static string RuleSetHash(IEnumerable<TaxEngineRule> selected)
        => TaxCanonical.Hash(string.Join('\n', selected
            .OrderBy(r => r.Key, StringComparer.Ordinal)
            .Select(r => $"{r.Key}@{r.Version}#{r.ContentHash}")));

    public static TaxEngineOutput Run(TaxEngineInput input, IReadOnlyCollection<TaxEngineRule> catalogue)
    {
        var date = input.ReferenceDate;
        var active = SelectActive(catalogue, date);
        var activeKeys = active.Select(r => r.Key).ToHashSet(StringComparer.Ordinal);
        var notApplied = catalogue
            .Where(r => r.Covers(date) && !activeKeys.Contains(r.Key)
                        && r.Status is TaxRuleStatus.PendingRegulation or TaxRuleStatus.Unverified or TaxRuleStatus.Draft)
            .GroupBy(r => r.Key, StringComparer.Ordinal)
            .Select(g => g.OrderByDescending(r => r.Version).First())
            .OrderBy(r => r.Key, StringComparer.Ordinal)
            .Select(r => new RuleNotApplied(r.VersionId, r.Key, r.Version, r.Status))
            .ToList();

        var executions = new List<RuleExecution>();
        var warnings = new List<string>();
        var lines = new List<LineResult>();

        foreach (var line in input.Lines.OrderBy(l => l.Index))
        {
            if (!line.ExcludedTaxesInformed)
                warnings.Add(FormattableString.Invariant($"Linha {line.Index}: ICMS, ISS, PIS e Cofins embutidos no preço não foram informados — a base pode estar maior do que a real."));
            if (line.SpecialTreatment is not null)
                warnings.Add(FormattableString.Invariant($"Linha {line.Index}: item em regime diferenciado ou específico ({line.SpecialTreatment}) — esta versão da engine não calcula alíquotas reduzidas nem bases específicas."));
            lines.Add(RunLine(input, line, active, executions, warnings));
        }

        var notes = new List<EngineNote>();
        foreach (var rule in active.Where(r => r.Result.Phase == TaxRulePhase.Note))
        {
            var (matched, traces) = Evaluate(rule, input, line: null, warnings);
            var note = (NoteResult)rule.Result;
            executions.Add(Execution(rule, null, matched, traces, matched ? $"nota {note.Code}" : "não se aplica"));
            if (matched) notes.Add(new EngineNote(note.Code, note.Text, rule.Key, rule.LegalSourceCodes));
        }

        if (active.All(r => r.Result is not RateResult))
            warnings.Add(FormattableString.Invariant($"Nenhuma alíquota vigente e ativa em {date:dd/MM/yyyy} no catálogo."));

        var totals = lines.SelectMany(l => l.Components)
            .GroupBy(c => c.Tax)
            .OrderBy(g => g.Key)
            .Select(g => new TaxTotal(g.Key, g.Sum(c => c.Base), g.Sum(c => c.Amount)))
            .ToList();

        return new TaxEngineOutput(
            Version, lines, totals,
            TotalBase: lines.Sum(l => l.Base),
            TotalTax: totals.Sum(t => t.Amount),
            notes, warnings.Distinct().ToList(), executions, notApplied);
    }

    private static LineResult RunLine(
        TaxEngineInput input, TaxEngineLine line, IReadOnlyList<TaxEngineRule> active,
        List<RuleExecution> executions, List<string> warnings)
    {
        var steps = new List<BaseStep>();
        var lineBase = line.Gross;

        foreach (var rule in active.Where(r => r.Result.Phase == TaxRulePhase.Base))
        {
            var (matched, traces) = Evaluate(rule, input, line, warnings);
            string output = "não se aplica";
            if (matched)
            {
                var (component, included) = rule.Result switch
                {
                    BaseInclusion i => (i.Component, true),
                    BaseExclusion e => (e.Component, false),
                    _ => throw new InvalidOperationException("Not a base rule."),
                };
                var delta = BaseDelta(line, component, included);
                lineBase += delta;
                steps.Add(new BaseStep(rule.Key, rule.Version, component, included, delta));
                output = FormattableString.Invariant($"{(included ? "mantém" : "exclui")} {component}: {delta:0.00}");
            }
            executions.Add(Execution(rule, line.Index, matched, traces, output));
        }

        if (lineBase < 0)
        {
            warnings.Add(FormattableString.Invariant($"Linha {line.Index}: a base ficou negativa e foi considerada zero."));
            lineBase = 0;
        }
        lineBase = Money(lineBase);

        var components = new List<ComponentResult>();
        foreach (var rule in active.Where(r => r.Result.Phase == TaxRulePhase.Rate))
        {
            var (matched, traces) = Evaluate(rule, input, line, warnings);
            var rate = (RateResult)rule.Result;
            string output = "não se aplica";
            if (matched)
            {
                if (components.Any(c => c.Tax == rate.Tax))
                    throw new TaxRuleConflictException(FormattableString.Invariant(
                        $"Duas regras ativas dão alíquota de {rate.Tax} à linha {line.Index} ({rule.Key} e {components.First(c => c.Tax == rate.Tax).RuleKey})."));
                var amount = Money(lineBase * rate.RatePercent / 100m);
                components.Add(new ComponentResult(rate.Tax, lineBase, rate.RatePercent, amount, rule.VersionId, rule.Key, rule.IsHypothesis));
                output = FormattableString.Invariant($"{rate.Tax} {rate.RatePercent}% sobre {lineBase:0.00} = {amount:0.00}");
            }
            executions.Add(Execution(rule, line.Index, matched, traces, output));
        }

        return new LineResult(line.Index, line.Gross, lineBase, steps, components);
    }

    /// <summary>Surcharges are added when included; discounts and taxes in the price are deducted when excluded.</summary>
    private static decimal BaseDelta(TaxEngineLine line, BaseComponent component, bool included) => component switch
    {
        BaseComponent.Surcharges => included ? line.Surcharges : 0m,
        BaseComponent.UnconditionalDiscount => included ? 0m : -line.UnconditionalDiscount,
        BaseComponent.ConditionalDiscount => included ? 0m : -line.ConditionalDiscount,
        BaseComponent.ExcludedTaxes => included ? 0m : -line.ExcludedTaxes,
        _ => throw new InvalidOperationException($"Unknown base component {component}."),
    };

    private static (bool Matched, IReadOnlyList<ConditionTrace> Traces) Evaluate(
        TaxEngineRule rule, TaxEngineInput input, TaxEngineLine? line, List<string> warnings)
    {
        var traces = rule.Conditions.Select(c => TaxConditionEvaluator.Evaluate(c, input, line)).ToList();
        var unknown = traces.Where(t => t.Outcome == ConditionOutcome.Unknown).ToList();
        if (unknown.Count > 0 && traces.All(t => t.Outcome != ConditionOutcome.Failed))
            warnings.Add($"A regra {rule.Key} não pôde ser avaliada: {string.Join("; ", unknown.Select(u => u.Actual))}.");
        return (traces.All(t => t.Outcome == ConditionOutcome.Passed), traces);
    }

    private static RuleExecution Execution(TaxEngineRule rule, int? lineIndex, bool matched, IReadOnlyList<ConditionTrace> traces, string output)
        => new(rule.VersionId, rule.Key, rule.Version, rule.ContentHash, lineIndex, matched, traces, output, rule.LegalSourceCodes, rule.IsHypothesis);

    /// <summary>Two decimals, half away from zero — an engineering convention of this engine version, not a legal rule.</summary>
    public static decimal Money(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);
}

/// <summary>Evaluates one condition against the operation and, for line rules, the line.</summary>
public static class TaxConditionEvaluator
{
    public static ConditionTrace Evaluate(TaxCondition condition, TaxEngineInput input, TaxEngineLine? line) => condition switch
    {
        SupplierRegimeIn c => Regime("supplier-regime-in", c.Regimes, SupplierRegime(input), shouldContain: true),
        SupplierRegimeNotIn c => Regime("supplier-regime-not-in", c.Regimes, SupplierRegime(input), shouldContain: false),
        DirectionIs c => new ConditionTrace("direction-is", c.Direction.ToString(), input.Direction.ToString(),
            input.Direction == c.Direction ? ConditionOutcome.Passed : ConditionOutcome.Failed),
        ItemKindIn c => line is null
            ? new ConditionTrace("item-kind-in", Join(c.Kinds), "regra da operação, sem linha", ConditionOutcome.Failed)
            : new ConditionTrace("item-kind-in", Join(c.Kinds), line.Kind.ToString(),
                c.Kinds.Contains(line.Kind) ? ConditionOutcome.Passed : ConditionOutcome.Failed),
        ProfileFlagIs c => Flag(c, input.Profile),
        ItemGeneralRegime => line is null
            ? new ConditionTrace("item-general-regime", "regime geral", "regra da operação, sem linha", ConditionOutcome.Failed)
            : new ConditionTrace("item-general-regime", "regime geral", line.SpecialTreatment ?? "regime geral",
                line.SpecialTreatment is null ? ConditionOutcome.Passed : ConditionOutcome.Failed),
        _ => throw new InvalidOperationException($"Unknown condition {condition.GetType().Name}."),
    };

    /// <summary>Who supplies: the establishment when it sells, the counterparty when it buys.</summary>
    public static TaxRegime? SupplierRegime(TaxEngineInput input)
        => input.Direction == TaxDirection.Outbound ? input.Profile.Regime : input.CounterpartyRegime;

    private static ConditionTrace Regime(string kind, IReadOnlyList<TaxRegime> regimes, TaxRegime? actual, bool shouldContain)
    {
        if (actual is null)
            return new ConditionTrace(kind, Join(regimes), "regime do fornecedor não informado", ConditionOutcome.Unknown);
        var passed = regimes.Contains(actual.Value) == shouldContain;
        return new ConditionTrace(kind, Join(regimes), actual.Value.ToString(), passed ? ConditionOutcome.Passed : ConditionOutcome.Failed);
    }

    private static ConditionTrace Flag(ProfileFlagIs c, TaxEngineProfile profile)
    {
        bool? actual = c.Flag switch
        {
            TaxProfileFlags.AccessoryObligationsCompliant => profile.AccessoryObligationsCompliant,
            _ => throw new InvalidOperationException($"Unknown profile flag {c.Flag}."),
        };
        if (actual is null)
            return new ConditionTrace("profile-flag-is", $"{c.Flag}={c.Value}", $"{c.Flag} não declarado no perfil", ConditionOutcome.Unknown);
        return new ConditionTrace("profile-flag-is", $"{c.Flag}={c.Value}", $"{c.Flag}={actual.Value}",
            actual.Value == c.Value ? ConditionOutcome.Passed : ConditionOutcome.Failed);
    }

    private static string Join<T>(IEnumerable<T> values) => string.Join(",", values);
}
