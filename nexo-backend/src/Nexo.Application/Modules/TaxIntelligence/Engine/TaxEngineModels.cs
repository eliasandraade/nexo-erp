using Nexo.Domain.Enums;
using Nexo.Domain.Modules.TaxIntelligence;

namespace Nexo.Application.Modules.TaxIntelligence.Engine;

// Inputs and outputs of the engine. Everything the result depends on is in TaxEngineInput (hashed),
// and the rules it ran are identified by id, version and content hash — nothing is read from the
// database, the clock or the environment during a run.

/// <summary>
/// The tax facts of the establishment on the reference date (from its profile version).
/// <c>AccessoryObligationsCompliant</c> is true only when compliance was verified (a declaration
/// alone is null = unknown); its V1 shape is part of the hashed input and stays as is.
/// </summary>
public sealed record TaxEngineProfile(
    Guid ProfileVersionId,
    TaxRegime Regime,
    string State,
    string CityIbgeCode,
    bool? AccessoryObligationsCompliant);

public sealed record TaxEngineLine(
    int Index,
    TaxItemKind Kind,
    decimal Gross,
    decimal UnconditionalDiscount,
    decimal ConditionalDiscount,
    decimal Surcharges,
    decimal ExcludedTaxes,
    bool ExcludedTaxesInformed,
    /// <summary>Code of the regime diferenciado or específico the item is under; null = general regime.</summary>
    string? SpecialTreatment = null);

public sealed record TaxEngineInput(
    DateOnly ReferenceDate,
    TaxEngineProfile Profile,
    TaxDirection Direction,
    TaxRegime? CounterpartyRegime,
    IReadOnlyList<TaxEngineLine> Lines);

/// <summary>A rule version as the engine runs it. Hypotheses exist only inside simulations.</summary>
public sealed record TaxEngineRule(
    Guid VersionId,
    string Key,
    int Version,
    TaxRuleStatus Status,
    DateOnly ValidFrom,
    DateOnly? ValidTo,
    int Priority,
    IReadOnlyList<TaxCondition> Conditions,
    TaxRuleResult Result,
    string ContentHash,
    IReadOnlyList<string> LegalSourceCodes,
    bool IsHypothesis = false)
{
    public bool Covers(DateOnly date) => ValidFrom <= date && (ValidTo is null || date <= ValidTo);
}

public enum ConditionOutcome
{
    Passed = 1,
    Failed = 2,
    /// <summary>The fact needed is not known — the rule is not applied and a warning says why.</summary>
    Unknown = 3,
}

public sealed record ConditionTrace(string Kind, string Expected, string Actual, ConditionOutcome Outcome);

public sealed record BaseStep(string RuleKey, int RuleVersion, BaseComponent Component, bool Included, decimal Delta);

public sealed record ComponentResult(
    TaxType Tax, decimal Base, decimal RatePercent, decimal Amount, Guid RuleVersionId, string RuleKey, bool IsHypothesis);

public sealed record LineResult(int Index, decimal Gross, decimal Base, IReadOnlyList<BaseStep> BaseSteps, IReadOnlyList<ComponentResult> Components);

public sealed record TaxTotal(TaxType Tax, decimal Base, decimal Amount);

public sealed record EngineNote(string Code, string Text, string RuleKey, IReadOnlyList<string> LegalSourceCodes);

public sealed record RuleExecution(
    Guid RuleVersionId,
    string RuleKey,
    int RuleVersion,
    string ContentHash,
    int? LineIndex,
    bool Matched,
    IReadOnlyList<ConditionTrace> Conditions,
    string Output,
    IReadOnlyList<string> LegalSourceCodes,
    bool IsHypothesis);

/// <summary>A rule that covers the date but is not active (pending regulation, unverified, draft) — shown, never applied.</summary>
public sealed record RuleNotApplied(Guid RuleVersionId, string RuleKey, int RuleVersion, TaxRuleStatus Status);

public sealed record TaxEngineOutput(
    string EngineVersion,
    IReadOnlyList<LineResult> Lines,
    IReadOnlyList<TaxTotal> Totals,
    decimal TotalBase,
    decimal TotalTax,
    IReadOnlyList<EngineNote> Notes,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<RuleExecution> Executions,
    IReadOnlyList<RuleNotApplied> NotApplied);

/// <summary>Two active rules gave the same tax a rate on the same line: the catalogue is inconsistent.</summary>
public sealed class TaxRuleConflictException(string message) : Nexo.Domain.Exceptions.DomainException(message, "tax_rule_conflict");
