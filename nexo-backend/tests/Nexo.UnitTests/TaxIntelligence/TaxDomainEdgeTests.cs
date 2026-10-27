using FluentAssertions;
using Nexo.Application.Modules.TaxIntelligence.Engine;
using Nexo.Domain.Enums;
using Nexo.Domain.Exceptions;
using Nexo.Domain.Modules.TaxIntelligence;
using static Nexo.UnitTests.TaxIntelligence.TaxTestData;

namespace Nexo.UnitTests.TaxIntelligence;

/// <summary>
/// The limits of the domain that protect money and audit: what a credit movement may never do, which
/// opportunity transitions are refused, and how the engine treats bases, line conditions and kinds it
/// does not know.
/// </summary>
public class TaxDomainEdgeTests
{
    private static readonly Guid Actor = Guid.NewGuid();
    private static readonly DateOnly Day = new(2026, 7, 1);
    private const string Key = "35261012345678000190550010000012341000012345";

    private static TaxCredit Credit(decimal original = 100m, decimal eligible = 90m, string? key = Key)
        => TaxCredit.Register(Guid.NewGuid(), CreditTax.Cbs, "NF-e", key, null, new DateOnly(2026, 6, 1), null,
            original, eligible, "declarado", null, Actor, "u");

    // ── credits ────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(100, 101, "the eligible part never exceeds the tax stated on the document")]
    [InlineData(100, -1, "an eligible amount is never negative")]
    [InlineData(0, 0, "a credit needs a positive amount")]
    public void A_credit_is_never_registered_beyond_its_document(decimal original, decimal eligible, string because)
        => FluentActions.Invoking(() => Credit(original, eligible)).Should().Throw<DomainException>(because);

    [Theory]
    [InlineData("123")]
    [InlineData("3526101234567800019055001000001234100001234X")]
    public void The_document_key_is_the_44_digit_access_key(string key)
        => FluentActions.Invoking(() => Credit(key: key)).Should().Throw<DomainException>();

    [Fact]
    public void Appropriation_never_exceeds_what_is_still_eligible()
    {
        var c = Credit();
        c.Appropriate(80m, "ok", Day, Actor, "u");

        c.Invoking(x => x.Appropriate(10.01m, "x", Day, Actor, "u"))
            .Should().Throw<DomainException>().Which.Code.Should().Be(TaxCredit.AmountErrorCode);
        c.Appropriate(10m, "resto", Day, Actor, "u");
        c.Balance().NotAppropriated.Should().Be(0m);
    }

    [Fact]
    public void A_use_names_the_obligation_it_pays()
    {
        var c = Credit();
        c.Appropriate(50m, "ok", Day, Actor, "u");
        c.Invoking(x => x.Use(10m, CreditTax.Cbs, "  ", Day, Actor, "u")).Should().Throw<DomainException>();
    }

    [Fact]
    public void Blocking_moves_available_to_blocked_and_back_never_beyond()
    {
        var c = Credit();
        c.Appropriate(50m, "ok", Day, Actor, "u");

        c.Invoking(x => x.Block(50.01m, "x", Day, Actor, "u")).Should().Throw<DomainException>().Which.Code.Should().Be(TaxCredit.AmountErrorCode);
        c.Invoking(x => x.Block(10m, " ", Day, Actor, "u")).Should().Throw<DomainException>("a block says why");
        c.Block(20m, "em fiscalização", Day, Actor, "u");
        (c.Balance().Available, c.Balance().Blocked).Should().Be((30m, 20m));

        c.Invoking(x => x.Unblock(20.01m, "x", Day, Actor, "u")).Should().Throw<DomainException>().Which.Code.Should().Be(TaxCredit.AmountErrorCode);
        c.Unblock(20m, "liberado", Day, Actor, "u");
        (c.Balance().Available, c.Balance().Blocked).Should().Be((50m, 0m));
    }

    [Fact]
    public void Only_the_available_balance_can_be_reversed()
    {
        var c = Credit();
        c.Appropriate(50m, "ok", Day, Actor, "u");
        c.Use(40m, CreditTax.Cbs, "obr", Day, Actor, "u");

        c.Invoking(x => x.ReverseAppropriation(10.01m, "nota cancelada", Day, Actor, "u"))
            .Should().Throw<DomainException>("what was already used cannot be reversed").Which.Code.Should().Be(TaxCredit.AmountErrorCode);
        c.Invoking(x => x.ReverseAppropriation(5m, " ", Day, Actor, "u")).Should().Throw<DomainException>("a reversal says why");
        c.ReverseAppropriation(10m, "nota cancelada", Day, Actor, "u");
        c.Balance().Available.Should().Be(0m);
    }

    // ── opportunity transitions ────────────────────────────────────────────────

    private static TaxOpportunity Opportunity()
    {
        var o = TaxOpportunity.Create(Guid.NewGuid(), TaxOpportunityType.TaxSaving, "t", "s", "d@1", TaxEngineV1.VersionId, null, "{}", "{}", 1m, TaxRiskLevel.Low, 80, "", null);
        o.AddRequirement("a", "A", null);
        return o;
    }

    [Fact]
    public void A_review_is_opened_once_and_answers_need_it()
    {
        var o = Opportunity();
        o.Invoking(x => x.AnswerRequirement(o.Requirements.Single().Id, true, null, Actor, "u", DateTime.UtcNow))
            .Should().Throw<DomainException>().Which.Code.Should().Be("tax_opportunity_state");

        o.StartReview(Actor, "u", DateTime.UtcNow);
        o.Invoking(x => x.StartReview(Actor, "u", DateTime.UtcNow)).Should().Throw<DomainException>().Which.Code.Should().Be("tax_opportunity_state");
        o.Invoking(x => x.AnswerRequirement(Guid.NewGuid(), true, null, Actor, "u", DateTime.UtcNow)).Should().Throw<NotFoundException>();
    }

    [Fact]
    public void A_rejection_says_why_and_closes_the_opportunity()
    {
        var o = Opportunity();
        o.Invoking(x => x.Reject("  ", Actor, "u", DateTime.UtcNow)).Should().Throw<DomainException>();

        o.Reject("Cliente não aceita mudar a condição", Actor, "Diretora", DateTime.UtcNow);

        (o.Status, o.RejectionReason, o.ReviewedByName).Should().Be((TaxOpportunityStatus.Rejected, "Cliente não aceita mudar a condição", "Diretora"));
        o.Invoking(x => x.StartReview(Actor, "u", DateTime.UtcNow)).Should().Throw<DomainException>().Which.Code.Should().Be("tax_opportunity_closed");
        o.Invoking(x => x.AddEvidence("x", null, "h", Actor, "u")).Should().Throw<DomainException>().Which.Code.Should().Be("tax_opportunity_closed");
        o.Invoking(x => x.Block(ProhibitedStructure.Simulation, "x", Actor, "u", DateTime.UtcNow)).Should().Throw<DomainException>();
    }

    [Fact]
    public void Evidence_and_blocking_always_carry_their_text()
    {
        var o = Opportunity();
        o.Invoking(x => x.AddEvidence(" ", null, "h", Actor, "u")).Should().Throw<DomainException>();
        o.Invoking(x => x.Block(ProhibitedStructure.FalseExpense, " ", Actor, "u", DateTime.UtcNow)).Should().Throw<DomainException>();
    }

    // ── engine ─────────────────────────────────────────────────────────────────

    [Fact]
    public void A_base_that_would_go_negative_is_taken_as_zero_and_reported()
    {
        // taxes in the price larger than what is left after the discount (bad source data)
        var output = V1.Run(Input(lines: new TaxEngineLine(1, TaxItemKind.Good, 100m, 90m, 0m, 0m, 50m, true)), Catalogue());

        output.Lines.Single().Base.Should().Be(0m);
        output.TotalTax.Should().Be(0m);
        output.Warnings.Should().Contain(w => w.Contains("base ficou negativa"));
    }

    [Fact]
    public void An_item_kind_condition_selects_lines_and_never_matches_the_whole_operation()
    {
        var day = new DateOnly(2031, 5, 5);
        IReadOnlyList<TaxCondition> servicesOnly = [new ItemKindIn([TaxItemKind.Service])];
        TaxRuleResult rate = new RateResult(TaxType.Cbs, 2m);
        var rule = new TaxEngineRule(Guid.NewGuid(), "SERVICE.RATE", 1, TaxRuleStatus.Active, day, day, 1, servicesOnly, rate,
            TaxCanonicalV1.Instance.RuleContentHash("SERVICE.RATE", 1, day, day, 1, servicesOnly, rate), ["TEST"]);
        var good = new TaxEngineLine(1, TaxItemKind.Good, 100m, 0m, 0m, 0m, 0m, true);
        var service = new TaxEngineLine(2, TaxItemKind.Service, 100m, 0m, 0m, 0m, 0m, true);

        var output = V1.Run(Input(date: day, lines: [good, service]), [rule]);

        output.Lines.Single(l => l.Index == 1).Components.Should().BeEmpty();
        output.Lines.Single(l => l.Index == 2).Components.Single().Amount.Should().Be(2m);
        output.Executions.Single(e => e.LineIndex == 1).Conditions.Single().Actual.Should().Be("Good");
        TaxConditionEvaluatorV1.Evaluate(servicesOnly[0], Input(date: day), line: null).Outcome
            .Should().Be(ConditionOutcome.Failed, "a line condition says nothing about the operation as a whole");
    }

    private sealed record UnknownCondition : TaxCondition;

// Results cannot be extended outside the engine at all: TaxRuleResult has an internal abstract member.

    [Fact]
    public void The_vocabulary_is_closed_kinds_the_engine_does_not_know_are_refused()
    {
        FluentActions.Invoking(() => TaxConditionEvaluatorV1.Evaluate(new UnknownCondition(), Input(), Line()))
            .Should().Throw<InvalidOperationException>();
        FluentActions.Invoking(() => TaxConditionEvaluatorV1.Evaluate(new ProfileFlagIs("noSuchFlag", true), Input(), null))
            .Should().Throw<InvalidOperationException>();
        FluentActions.Invoking(() => TaxRuleContentValidator.Validate("K", [new UnknownCondition()], new NoteResult("a", "b")))
            .Should().Throw<DomainException>();

        var day = new DateOnly(2031, 5, 6);
        IReadOnlyList<TaxCondition> unknown = [new UnknownCondition()];
        TaxRuleResult note = new NoteResult("x", "y");
        var rule = new TaxEngineRule(Guid.NewGuid(), "BAD.CONDITION", 1, TaxRuleStatus.Active, day, day, 1, unknown, note, "h", []);
        FluentActions.Invoking(() => V1.Run(Input(date: day), [rule]))
            .Should().Throw<InvalidOperationException>("a rule the engine cannot evaluate fails loudly, never silently matches");
    }

    [Fact]
    public void Transaction_lines_keep_discounts_within_the_price()
    {
        var t = TaxTransaction.Create(Guid.NewGuid(), TaxTransactionSource.Manual, null, TaxDirection.Outbound, Day, " ", null, " ", "h", Actor);
        (t.Description, t.CounterpartyState).Should().Be((null, null), "blank optional facts are stored as absent");

        t.Invoking(x => x.AddLine(1, TaxItemKind.Good, "x", null, 1m, 10m, 6m, 5m, 0m, 0m, true, null))
            .Should().Throw<DomainException>("discounts cannot exceed the line");
        t.Invoking(x => x.AddLine(1, TaxItemKind.Good, "x", null, 1m, 10m, -1m, 0m, 0m, 0m, true, null))
            .Should().Throw<DomainException>();
        t.AddLine(1, TaxItemKind.Good, " item ", " ", 1m, 10m, 5m, 5m, 0m, 0m, true, " ");
        var line = t.Lines.Single();
        (line.Description, line.Ncm, line.SpecialTreatment).Should().Be(("item", null, null));
    }
}
