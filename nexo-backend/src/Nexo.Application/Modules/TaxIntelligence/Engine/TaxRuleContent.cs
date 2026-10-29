using System.Text.Json.Serialization;
using Nexo.Domain.Enums;
using Nexo.Domain.Modules.TaxIntelligence;

namespace Nexo.Application.Modules.TaxIntelligence.Engine;

// The vocabulary rules are written in. It is deliberately small and closed: each condition and each
// result is a typed record, serialised with a "kind" discriminator into tax_rule_versions. A new kind
// is a code change reviewed like any other — rules cannot run arbitrary expressions.

/// <summary>A test a rule makes on the operation (and, for line rules, on the line).</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(SupplierRegimeIn), "supplier-regime-in")]
[JsonDerivedType(typeof(SupplierRegimeNotIn), "supplier-regime-not-in")]
[JsonDerivedType(typeof(DirectionIs), "direction-is")]
[JsonDerivedType(typeof(ItemKindIn), "item-kind-in")]
[JsonDerivedType(typeof(ProfileFlagIs), "profile-flag-is")]
[JsonDerivedType(typeof(ItemGeneralRegime), "item-general-regime")]
public abstract record TaxCondition;

/// <summary>
/// The supplier's regime is one of these. The supplier is the establishment on outbound operations
/// and the counterparty on inbound ones; an unknown counterparty regime makes the test indeterminate.
/// </summary>
public sealed record SupplierRegimeIn(IReadOnlyList<TaxRegime> Regimes) : TaxCondition;

public sealed record SupplierRegimeNotIn(IReadOnlyList<TaxRegime> Regimes) : TaxCondition;

public sealed record DirectionIs(TaxDirection Direction) : TaxCondition;

/// <summary>Only meaningful for line rules.</summary>
public sealed record ItemKindIn(IReadOnlyList<TaxItemKind> Kinds) : TaxCondition;

/// <summary>
/// The item is under the general regime — not a regime diferenciado (reduced rates) nor específico.
/// Rates of the transition years apply "with the respective reduction" or on other bases to those
/// (LC 214/2025, arts. 344, parágrafo único, and 348, III, a and b), which this engine version does
/// not compute: such lines get no rate and a warning.
/// </summary>
public sealed record ItemGeneralRegime : TaxCondition;

/// <summary>A declared fact of the tax profile. Flags are a closed list (<see cref="TaxProfileFlags"/>).</summary>
public sealed record ProfileFlagIs(string Flag, bool Value) : TaxCondition;

public static class TaxProfileFlags
{
    /// <summary>The establishment fulfils the IBS/CBS ancillary obligations (LC 214/2025, art. 348, § 1º).</summary>
    public const string AccessoryObligationsCompliant = "accessoryObligationsCompliant";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal) { AccessoryObligationsCompliant };
}

/// <summary>A part of the line the base rules can keep in or take out of the tax base.</summary>
public enum BaseComponent
{
    Surcharges = 1,
    UnconditionalDiscount = 2,
    ConditionalDiscount = 3,
    ExcludedTaxes = 4,
}

/// <summary>What a rule produces when its conditions pass.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(BaseInclusion), "base-inclusion")]
[JsonDerivedType(typeof(BaseExclusion), "base-exclusion")]
[JsonDerivedType(typeof(RateResult), "rate")]
[JsonDerivedType(typeof(NoteResult), "note")]
public abstract record TaxRuleResult
{
    internal abstract TaxRulePhase Phase { get; }
}

/// <summary>
/// The component stays in the base. The engine starts every line at its gross price: an included
/// surcharge is added to it, an included discount is simply not deducted.
/// </summary>
public sealed record BaseInclusion(BaseComponent Component) : TaxRuleResult
{
    internal override TaxRulePhase Phase => TaxRulePhase.Base;
}

/// <summary>The component leaves the base: a discount or a tax in the price is deducted from it.</summary>
public sealed record BaseExclusion(BaseComponent Component) : TaxRuleResult
{
    internal override TaxRulePhase Phase => TaxRulePhase.Base;
}

/// <summary>A rate applied to the line base, in percent (0.9 = 0,9%).</summary>
public sealed record RateResult(TaxType Tax, decimal RatePercent) : TaxRuleResult
{
    internal override TaxRulePhase Phase => TaxRulePhase.Rate;
}

/// <summary>Something the reader must know about the operation (e.g. a collection waiver). No amount.</summary>
public sealed record NoteResult(string Code, string Text) : TaxRuleResult
{
    internal override TaxRulePhase Phase => TaxRulePhase.Note;
}

/// <summary>Base rules run first, then rates on the resulting base, then notes about the operation.</summary>
internal enum TaxRulePhase
{
    Base = 0,
    Rate = 1,
    Note = 2,
}
