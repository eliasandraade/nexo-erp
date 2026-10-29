using System.Text.Json;

namespace Nexo.Application.Modules.TaxIntelligence.Engine;

/// <summary>
/// How an engine version turns its data into text and hashes. Each engine version owns exactly one
/// canonical form; a calculation's input, output and rule-set hashes are only meaningful under the
/// canonical form of the engine that produced them.
/// </summary>
public interface ITaxCanonicalizer
{
    /// <summary>Stable id of the canonical form, e.g. "orken-canonical/1".</summary>
    string Id { get; }

    JsonSerializerOptions Options { get; }

    string Serialize<T>(T value);

    T Deserialize<T>(string json);

    string Hash(string text);

    string HashOf<T>(T value);

    string RuleContentHash(string key, int version, DateOnly validFrom, DateOnly? validTo, int priority,
        IReadOnlyList<TaxCondition> conditions, TaxRuleResult result);
}

/// <summary>
/// One frozen version of the tax engine. Implementations never change behaviour once released: a
/// change is a new implementation with a new <see cref="Version"/>.
/// </summary>
public interface ITaxEngine
{
    /// <summary>Stored with every calculation, e.g. "orken-tax-engine/1.0.0".</summary>
    string Version { get; }

    ITaxCanonicalizer Canonical { get; }

    IReadOnlyList<TaxEngineRule> SelectActive(IEnumerable<TaxEngineRule> rules, DateOnly date);

    string RuleSetHash(IEnumerable<TaxEngineRule> selected);

    TaxEngineOutput Run(TaxEngineInput input, IReadOnlyCollection<TaxEngineRule> catalogue);

    /// <summary>Money rounding of this engine version.</summary>
    decimal RoundMoney(decimal value);
}

/// <summary>
/// The engine versions this build can run. New calculations use <see cref="Current"/>; a stored
/// calculation is reproduced only by the exact version it records — never by the current one.
/// </summary>
public sealed class TaxEngineRegistry
{
    private readonly Dictionary<string, ITaxEngine> _engines;

    public TaxEngineRegistry(IEnumerable<ITaxEngine> engines, string currentVersion)
    {
        _engines = new Dictionary<string, ITaxEngine>(StringComparer.Ordinal);
        foreach (var e in engines)
        {
            if (!_engines.TryAdd(e.Version, e))
                throw new InvalidOperationException($"Engine version {e.Version} registered twice.");
        }
        Current = _engines.TryGetValue(currentVersion, out var current)
            ? current
            : throw new InvalidOperationException($"Current engine {currentVersion} is not registered.");
    }

    /// <summary>What this build ships: engine V1, current.</summary>
    public static TaxEngineRegistry Default { get; } = new([new TaxEngineV1()], TaxEngineV1.VersionId);

    public ITaxEngine Current { get; }

    public IReadOnlyCollection<string> Versions => _engines.Keys;

    public bool TryGet(string version, out ITaxEngine engine) => _engines.TryGetValue(version, out engine!);
}

/// <summary>What a calculation stored, as needed to run it again.</summary>
public sealed record StoredCalculation(
    string EngineVersion,
    string InputJson,
    string InputHash,
    string OutputJson,
    string OutputHash,
    IReadOnlyList<StoredRuleRun> RulesRun);

/// <summary>A rule version the calculation looked at, with the content hash it had then.</summary>
public sealed record StoredRuleRun(Guid RuleVersionId, string RuleKey, int RuleVersion, string ContentHash);

public sealed record ReproductionResult(
    bool Reproducible,
    bool EngineAvailable,
    string StoredEngineVersion,
    string CurrentEngineVersion,
    string? RecomputedOutputHash,
    IReadOnlyList<string> Differences);

/// <summary>
/// Runs a stored calculation again with the engine version it records and compares the result.
/// If that version is not registered in this build the answer is "cannot reproduce" — the current
/// engine is never used in its place.
/// </summary>
public static class TaxReproduction
{
    /// <param name="loadRules">
    /// Loads the given rule versions (executed or listed as not applied), whatever their status today,
    /// deserialising their content with the engine's canonical form.
    /// </param>
    public static async Task<ReproductionResult> VerifyAsync(
        StoredCalculation calc, TaxEngineRegistry registry,
        Func<ITaxEngine, IReadOnlyCollection<Guid>, Task<IReadOnlyList<TaxEngineRule>>> loadRules)
    {
        var current = registry.Current.Version;
        if (!registry.TryGet(calc.EngineVersion, out var engine))
            return new ReproductionResult(false, false, calc.EngineVersion, current, null,
                [$"A engine {calc.EngineVersion} que fez este cálculo não está disponível nesta versão do Orken: a reprodução não pode ser executada (a engine atual, {current}, não é usada no lugar dela)."]);

        var canonical = engine.Canonical;
        var differences = new List<string>();
        if (canonical.Hash(calc.InputJson) != calc.InputHash)
            differences.Add("O input armazenado não corresponde ao seu hash.");
        if (canonical.Hash(calc.OutputJson) != calc.OutputHash)
            differences.Add("O resultado armazenado não corresponde ao seu hash.");

        var storedOutput = canonical.Deserialize<TaxEngineOutput>(calc.OutputJson);
        var ids = calc.RulesRun.Select(r => r.RuleVersionId).Concat(storedOutput.NotApplied.Select(n => n.RuleVersionId)).Distinct().ToList();
        var rules = await loadRules(engine, ids);
        foreach (var run in calc.RulesRun.DistinctBy(r => r.RuleVersionId))
        {
            var rule = rules.FirstOrDefault(r => r.VersionId == run.RuleVersionId);
            if (rule is null) differences.Add($"Versão de regra {run.RuleKey} v{run.RuleVersion} não existe mais no catálogo.");
            else if (rule.ContentHash != run.ContentHash) differences.Add($"O conteúdo de {run.RuleKey} v{run.RuleVersion} mudou desde o cálculo.");
        }

        var asThen = RulesAsThen(calc.RulesRun.Select(r => r.RuleVersionId), storedOutput, rules);
        var input = canonical.Deserialize<TaxEngineInput>(calc.InputJson);
        var recomputed = canonical.HashOf(engine.Run(input, asThen));
        if (recomputed != calc.OutputHash) differences.Add("O resultado recalculado é diferente do armazenado.");

        return new ReproductionResult(differences.Count == 0, true, calc.EngineVersion, current, recomputed, differences);
    }

    /// <summary>The rule versions as they were when the calculation ran: what ran was active, what was listed kept its status.</summary>
    public static IReadOnlyList<TaxEngineRule> RulesAsThen(
        IEnumerable<Guid> executedVersionIds, TaxEngineOutput storedOutput, IEnumerable<TaxEngineRule> rules)
    {
        var executed = executedVersionIds.ToHashSet();
        var listed = storedOutput.NotApplied.ToDictionary(n => n.RuleVersionId, n => n.Status);
        return rules
            .Where(r => executed.Contains(r.VersionId) || listed.ContainsKey(r.VersionId))
            .Select(r => r with { Status = executed.Contains(r.VersionId) ? Nexo.Domain.Modules.TaxIntelligence.TaxRuleStatus.Active : listed[r.VersionId] })
            .ToList();
    }
}
