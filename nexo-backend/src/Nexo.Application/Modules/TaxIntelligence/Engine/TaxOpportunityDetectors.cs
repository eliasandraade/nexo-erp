using Nexo.Domain.Modules.TaxIntelligence;

namespace Nexo.Application.Modules.TaxIntelligence.Engine;

public sealed record OpportunityRequirementSpec(string Code, string Text, string? LegalSourceCode);

/// <summary>An opportunity as a detector proposes it: both scenarios, the estimate, its risk and what must hold.</summary>
public sealed record OpportunityCandidate(
    string DetectorKey,
    TaxOpportunityType Type,
    string Title,
    string Summary,
    ScenarioResult Current,
    ScenarioResult Proposed,
    decimal EstimatedImpact,
    TaxRiskLevel Risk,
    int Confidence,
    IReadOnlyList<string> LegalSourceCodes,
    IReadOnlyList<OpportunityRequirementSpec> Requirements,
    string? Caveats);

/// <summary>
/// Looks at an official calculation and proposes alternatives. Detectors are deterministic and
/// only propose: they never change the operation, and what they return is reviewed by people.
/// </summary>
public interface ITaxOpportunityDetector
{
    /// <summary>"name@version" — stored with each opportunity so it is known which logic produced it.</summary>
    string Key { get; }

    /// <summary>Runs with the engine version that produced the calculation being examined.</summary>
    IEnumerable<OpportunityCandidate> Detect(ITaxEngine engine, TaxEngineInput input, IReadOnlyCollection<TaxEngineRule> catalogue);
}

public static class TaxOpportunityDetectors
{
    public static readonly IReadOnlyList<ITaxOpportunityDetector> All = [new UnconditionalDiscountDetector()];
}

/// <summary>
/// A discount granted under a condition stays in the IBS/CBS base (LC 214/2025, art. 12, § 1º, III);
/// an unconditional one stated on the fiscal document leaves it (§ 2º, III; definition in § 3º).
/// When an outbound operation has conditional discounts on taxed lines, the alternative is to grant
/// them unconditionally — a commercial change (the condition goes away), lawful only if the discount
/// really is on the document and no longer depends on a later event.
/// The impact is a <see cref="TaxOpportunityType.CalculatedTaxReduction"/>: the IBS/CBS calculated on
/// the document goes down. It is never presented as a definitive saving — in 2026 the amount is
/// compensable or waived (art. 348), and whether it becomes an economic gain in any period is for a
/// tax professional to assess, not for the detector.
/// </summary>
public sealed class UnconditionalDiscountDetector : ITaxOpportunityDetector
{
    /// <summary>v2: same logic and scenarios as v1; the impact is a calculated-tax reduction, not a saving.</summary>
    public const string DetectorKey = "unconditional-discount@2";
    /// <summary>Opportunities stored by v1 (classified as a saving) keep their key and their guardrails.</summary>
    public const string DetectorKeyV1 = "unconditional-discount@1";
    public const string RequirementOnDocument = "on-document";
    public const string RequirementNoLaterEvent = "no-later-event";
    public const string RequirementCommercialTerms = "commercial-terms";

    public string Key => DetectorKey;

    public IEnumerable<OpportunityCandidate> Detect(ITaxEngine engine, TaxEngineInput input, IReadOnlyCollection<TaxEngineRule> catalogue)
    {
        if (input.Direction != Nexo.Domain.Modules.TaxIntelligence.TaxDirection.Outbound) yield break;
        if (input.Lines.All(l => l.ConditionalDiscount <= 0)) yield break;

        var results = TaxSimulator.Compare(engine, input,
            [new TaxScenario("Desconto incondicional no documento fiscal", ConvertConditionalDiscounts: true)], catalogue);
        var current = results[0];
        var proposed = results[1];
        var impact = current.TaxTotal - proposed.TaxTotal;
        if (impact <= 0) yield break;   // no taxed line carries a conditional discount

        var conditional = input.Lines.Sum(l => l.ConditionalDiscount);
        var allInformed = input.Lines.All(l => l.ExcludedTaxesInformed);
        var caveats = new List<string>
        {
            "Muda a condição comercial: o desconto deixa de depender de pagamento antecipado, meta ou outro evento futuro.",
            "É redução do IBS/CBS calculado sobre a operação, não economia definitiva: o efeito econômico depende do período e de revisão profissional.",
        };
        if (current.NoCashEffectInPeriod)
            caveats.Add("Em 2026 o IBS e a CBS calculados são compensáveis com PIS/Cofins ou dispensados para quem cumpre as obrigações acessórias (LC 214/2025, art. 348): a redução aparece no valor destacado, não no caixa do período.");
        if (!allInformed)
            caveats.Add("Os tributos atuais embutidos no preço não foram informados: a base e a diferença podem estar superestimadas.");

        yield return new OpportunityCandidate(
            DetectorKey,
            TaxOpportunityType.CalculatedTaxReduction,
            Title: "Conceder o desconto como incondicional",
            Summary: FormattableString.Invariant(
                $"R$ {conditional:0.00} de desconto concedido sob condição integram a base do IBS e da CBS. Concedido sem condição e destacado no documento fiscal, sai da base: redução do IBS/CBS calculado de {impact:0.00} nesta operação."),
            current, proposed, impact,
            TaxRiskLevel.Low,
            Confidence: allInformed ? 90 : 70,
            LegalSourceCodes: [TaxLegalCodes.Lc214Art12Base, TaxLegalCodes.Lc214Art12UnconditionalDiscount],
            Requirements:
            [
                new(RequirementOnDocument, "O desconto consta do documento fiscal da operação.", TaxLegalCodes.Lc214Art12UnconditionalDiscount),
                new(RequirementNoLaterEvent, "O desconto não depende de evento posterior (pagamento antecipado, meta de volume, devolução…).", TaxLegalCodes.Lc214Art12UnconditionalDiscount),
                new(RequirementCommercialTerms, "A proposta ou o contrato foi alterado: a condição comercial deixou de existir.", null),
            ],
            Caveats: string.Join(" ", caveats));
    }
}

/// <summary>
/// Guardrails: answers to requirements that turn an opportunity into a prohibited structure. Then
/// the opportunity is blocked — no saving shown, no approval possible — and the reason is recorded.
/// </summary>
public static class TaxGuardrails
{
    public sealed record Violation(ProhibitedStructure Structure, string Reason);

    public static Violation? OnRequirementAnswered(string detectorKey, string requirementCode, bool met)
    {
        if (met) return null;
        return (detectorKey, requirementCode) switch
        {
            (UnconditionalDiscountDetector.DetectorKey or UnconditionalDiscountDetector.DetectorKeyV1,
                UnconditionalDiscountDetector.RequirementNoLaterEvent) => new Violation(
                ProhibitedStructure.DocumentMismatch,
                "O desconto ainda depende de evento posterior: destacá-lo como incondicional faria o documento fiscal dizer algo diferente da operação real."),
            _ => null,
        };
    }
}
