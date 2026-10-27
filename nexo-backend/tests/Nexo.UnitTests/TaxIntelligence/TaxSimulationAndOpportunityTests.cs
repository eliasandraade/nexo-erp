using FluentAssertions;
using Nexo.Application.Modules.TaxIntelligence.Engine;
using Nexo.Domain.Enums;
using Nexo.Domain.Exceptions;
using Nexo.Domain.Modules.TaxIntelligence;
using static Nexo.UnitTests.TaxIntelligence.TaxTestData;

namespace Nexo.UnitTests.TaxIntelligence;

public class TaxSimulationAndOpportunityTests
{
    [Fact]
    public void Converting_conditional_discounts_lowers_the_base_by_the_discount()
    {
        var results = TaxSimulator.Compare(Input(), [new TaxScenario("Incondicional", ConvertConditionalDiscounts: true)], Catalogue());

        results.Should().HaveCount(2);
        results[0].IsBaseline.Should().BeTrue();
        results[0].TaxTotal.Should().Be(8.40m);
        results[1].Output.Lines.Single().Base.Should().Be(790m);
        results[1].TaxTotal.Should().Be(7.90m);
        results[1].TaxDeltaVsBaseline.Should().Be(-0.50m);
    }

    [Fact]
    public void A_scenario_never_changes_the_baseline_input()
    {
        var baseline = Input();
        var before = TaxCanonical.Serialize(baseline);

        TaxSimulator.Compare(baseline, [new TaxScenario("x", Regime: TaxRegime.SimplesNacional, ConvertConditionalDiscounts: true)], Catalogue());

        TaxCanonical.Serialize(baseline).Should().Be(before);
    }

    [Fact]
    public void A_rate_hypothesis_replaces_the_rate_and_is_labelled()
    {
        var results = TaxSimulator.Compare(Input(date: new DateOnly(2027, 3, 1)),
            [new TaxScenario("Hipótese 2027", RateHypotheses: [new RateHypothesis(TaxType.Cbs, 8.8m, "estimativa do usuário")])],
            Catalogue());

        results[0].TaxTotal.Should().Be(0m, "no 2027 CBS rate is in force in the catalogue");
        var cbs = results[1].Output.Lines.Single().Components.Single(c => c.Tax == TaxType.Cbs);
        cbs.RatePercent.Should().Be(8.8m);
        cbs.IsHypothesis.Should().BeTrue();
        results[1].Hypotheses.Should().ContainSingle(h => h.StartsWith("Hipótese: Cbs a 8.8%"));
    }

    [Fact]
    public void Supplier_comparison_nets_the_credit_only_when_assumed()
    {
        var input = Input(direction: TaxDirection.Inbound, counterparty: TaxRegime.LucroReal);
        var results = TaxSimulator.Compare(input,
            [
                new TaxScenario("Sem crédito"),
                new TaxScenario("Com crédito", AssumeCreditable: true),
            ], Catalogue());

        results[1].CreditEstimate.Should().Be(0m);
        results[2].CreditEstimate.Should().Be(8.40m);
        results[2].EffectiveCost.Should().Be(results[2].Charged - 8.40m);
        results[2].Hypotheses.Should().Contain(h => h.Contains("art. 47"));
    }

    [Fact]
    public void Simples_supplier_gives_no_credit_estimate_even_when_assumed()
    {
        var results = TaxSimulator.Compare(Input(direction: TaxDirection.Inbound, counterparty: TaxRegime.SimplesNacional),
            [new TaxScenario("Simples", AssumeCreditable: true)], Catalogue());

        results[1].CreditEstimate.Should().Be(0m);
    }

    [Fact]
    public void Detector_proposes_the_unconditional_discount_with_its_requirements_and_caveats()
    {
        var candidate = new UnconditionalDiscountDetector().Detect(Input(), Catalogue()).Single();

        candidate.Type.Should().Be(TaxOpportunityType.TaxSaving);
        candidate.EstimatedImpact.Should().Be(0.50m);
        candidate.Risk.Should().Be(TaxRiskLevel.Low);
        candidate.Confidence.Should().Be(90);
        candidate.Requirements.Select(r => r.Code).Should().Equal(
            UnconditionalDiscountDetector.RequirementOnDocument,
            UnconditionalDiscountDetector.RequirementNoLaterEvent,
            UnconditionalDiscountDetector.RequirementCommercialTerms);
        candidate.LegalSourceCodes.Should().Contain(TaxLegalCodes.Lc214Art12UnconditionalDiscount);
        candidate.Caveats.Should().Contain("art. 348", "in 2026 the reduction is in the amount stated, not in cash");
    }

    [Theory]
    [InlineData(TaxRegime.SimplesNacional)]
    [InlineData(TaxRegime.LucroReal)]
    public void Detector_stays_quiet_when_there_is_nothing_to_gain(TaxRegime regime)
    {
        var noConditional = new TaxEngineLine(1, TaxItemKind.Good, 1000m, 100m, 0m, 0m, 0m, true);
        var detector = new UnconditionalDiscountDetector();

        detector.Detect(Input(regime, lines: noConditional), Catalogue()).Should().BeEmpty();
        if (regime == TaxRegime.SimplesNacional)
            detector.Detect(Input(regime), Catalogue()).Should().BeEmpty("no IBS/CBS test rate for the Simples — no saving");
        detector.Detect(Input(regime, direction: TaxDirection.Inbound, counterparty: TaxRegime.LucroReal), Catalogue())
            .Should().BeEmpty("only the supplier decides its own discounts");
    }

    [Fact]
    public void Lower_confidence_when_taxes_in_the_price_are_unknown()
        => new UnconditionalDiscountDetector().Detect(Input(lines: Line(informed: false)), Catalogue()).Single().Confidence.Should().Be(70);

    [Fact]
    public void Guardrail_blocks_a_discount_that_still_depends_on_a_later_event()
    {
        var v = TaxGuardrails.OnRequirementAnswered(UnconditionalDiscountDetector.DetectorKey, UnconditionalDiscountDetector.RequirementNoLaterEvent, met: false);

        v.Should().NotBeNull();
        v!.Structure.Should().Be(ProhibitedStructure.DocumentMismatch);
        TaxGuardrails.OnRequirementAnswered(UnconditionalDiscountDetector.DetectorKey, UnconditionalDiscountDetector.RequirementNoLaterEvent, met: true).Should().BeNull();
        TaxGuardrails.OnRequirementAnswered(UnconditionalDiscountDetector.DetectorKey, UnconditionalDiscountDetector.RequirementOnDocument, met: false).Should().BeNull();
    }

    // ── Opportunity workflow (domain) ───────────────────────────────────────────

    private static TaxOpportunity Opportunity(TaxRiskLevel risk = TaxRiskLevel.Low)
    {
        var o = TaxOpportunity.Create(Guid.NewGuid(), TaxOpportunityType.TaxSaving, "t", "s", "d@1", null, "{}", "{}", 10m, risk, 90, "", null);
        o.AddRequirement("a", "A", null);
        o.AddRequirement("b", "B", null);
        return o;
    }

    [Fact]
    public void A_new_opportunity_is_only_identified_never_applied()
    {
        var o = Opportunity();
        o.Status.Should().Be(TaxOpportunityStatus.Identified);
        o.ImpactNature.Should().Be(TaxImpactNature.DefinitiveSaving);
    }

    [Fact]
    public void Approval_needs_review_and_every_requirement_met()
    {
        var o = Opportunity();
        var user = Guid.NewGuid();

        o.Invoking(x => x.Approve(false, user, "u", DateTime.UtcNow)).Should().Throw<DomainException>().Which.Code.Should().Be("tax_opportunity_state");
        o.StartReview(user, "u", DateTime.UtcNow);
        o.AnswerRequirement(o.Requirements.First().Id, true, null, user, "u", DateTime.UtcNow);
        o.Invoking(x => x.Approve(false, user, "u", DateTime.UtcNow)).Should().Throw<DomainException>().Which.Code.Should().Be("tax_opportunity_requirements");

        o.AnswerRequirement(o.Requirements.Last().Id, true, null, user, "u", DateTime.UtcNow);
        o.Approve(false, user, "u", DateTime.UtcNow);
        o.Status.Should().Be(TaxOpportunityStatus.Approved);
        o.Invoking(x => x.Reject("x", user, "u", DateTime.UtcNow)).Should().Throw<DomainException>();
    }

    [Fact]
    public void High_risk_needs_explicit_acknowledgement()
    {
        var o = Opportunity(TaxRiskLevel.High);
        var user = Guid.NewGuid();
        o.StartReview(user, "u", DateTime.UtcNow);
        foreach (var r in o.Requirements.ToList()) o.AnswerRequirement(r.Id, true, null, user, "u", DateTime.UtcNow);

        o.Invoking(x => x.Approve(false, user, "u", DateTime.UtcNow)).Should().Throw<DomainException>().Which.Code.Should().Be("tax_opportunity_high_risk");
        o.Approve(true, user, "u", DateTime.UtcNow);
        o.Status.Should().Be(TaxOpportunityStatus.Approved);
    }

    [Fact]
    public void A_blocked_opportunity_can_never_be_approved()
    {
        var o = Opportunity();
        var user = Guid.NewGuid();
        o.StartReview(user, "u", DateTime.UtcNow);
        o.Block(ProhibitedStructure.DocumentMismatch, "motivo", user, "u", DateTime.UtcNow);

        o.Invoking(x => x.Approve(true, user, "u", DateTime.UtcNow)).Should().Throw<DomainException>().Which.Code.Should().Be("tax_opportunity_blocked");
        o.BlockedAs.Should().Be(ProhibitedStructure.DocumentMismatch);
    }

    [Fact]
    public void Deferral_is_never_read_as_saving()
    {
        TaxOpportunity.NatureOf(TaxOpportunityType.TaxDeferral).Should().Be(TaxImpactNature.Deferral);
        TaxOpportunity.NatureOf(TaxOpportunityType.CashFlowOptimization).Should().Be(TaxImpactNature.CashFlow);
        TaxOpportunity.NatureOf(TaxOpportunityType.CreditOptimization).Should().Be(TaxImpactNature.Credit);
        TaxOpportunity.NatureOf(TaxOpportunityType.RiskReduction).Should().Be(TaxImpactNature.RiskAvoided);
    }
}
