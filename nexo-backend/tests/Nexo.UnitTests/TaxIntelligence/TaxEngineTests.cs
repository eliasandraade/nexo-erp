using System.Globalization;
using FluentAssertions;
using Nexo.Application.Modules.TaxIntelligence.Engine;
using Nexo.Domain.Enums;
using Nexo.Domain.Modules.TaxIntelligence;
using static Nexo.UnitTests.TaxIntelligence.TaxTestData;

namespace Nexo.UnitTests.TaxIntelligence;

public class TaxEngineTests
{
    [Fact]
    public void Regular_regime_2026_applies_the_test_rates_on_the_base_of_article_12()
    {
        var output = V1.Run(Input(), Catalogue());

        var line = output.Lines.Single();
        line.Base.Should().Be(840m, "1000 gross + 20 surcharges − 100 unconditional − 80 taxes in the price; the conditional 50 stays");
        line.Components.Should().HaveCount(2);
        line.Components.Single(c => c.Tax == TaxType.Cbs).Amount.Should().Be(7.56m);
        line.Components.Single(c => c.Tax == TaxType.IbsState).Amount.Should().Be(0.84m);
        output.TotalTax.Should().Be(8.40m);
        output.Totals.Select(t => t.Tax).Should().Equal(TaxType.Cbs, TaxType.IbsState);
    }

    [Fact]
    public void Every_rule_looked_at_is_traced_with_its_conditions_and_legal_sources()
    {
        var output = V1.Run(Input(), Catalogue());

        var cbs = output.Executions.Single(e => e.RuleKey == "CBS.RATE.2026-TEST");
        cbs.Matched.Should().BeTrue();
        cbs.Conditions.Should().OnlyContain(c => c.Outcome == ConditionOutcome.Passed);
        cbs.LegalSourceCodes.Should().Contain([TaxLegalCodes.Lc214Art346, TaxLegalCodes.Lc214Art348]);
        cbs.Output.Should().Be("Cbs 0.9% sobre 840.00 = 7.56");

        output.Executions.Single(e => e.RuleKey == "IBSCBS.NOTE.2026-SIMPLES").Matched.Should().BeFalse();
        output.Notes.Select(n => n.Code).Should().Contain(TaxNoteCodes.Compensation2026);
    }

    [Theory]
    [InlineData(TaxRegime.SimplesNacional)]
    [InlineData(TaxRegime.SimplesNacionalExcessoSublimite)]
    [InlineData(TaxRegime.Mei)]
    public void Simples_Nacional_gets_no_test_rate_and_a_note(TaxRegime regime)
    {
        var output = V1.Run(Input(regime), Catalogue());

        output.TotalTax.Should().Be(0m);
        output.Lines.Single().Components.Should().BeEmpty();
        output.Notes.Should().ContainSingle(n => n.Code == TaxNoteCodes.Simples2026);
    }

    [Fact]
    public void Waiver_note_only_when_the_profile_declares_compliance()
    {
        V1.Run(Input(compliant: true), Catalogue()).Notes.Select(n => n.Code).Should().Contain(TaxNoteCodes.Waiver2026);
        V1.Run(Input(compliant: false), Catalogue()).Notes.Select(n => n.Code).Should().NotContain(TaxNoteCodes.Waiver2026);

        var undeclared = V1.Run(Input(compliant: null), Catalogue());
        undeclared.Notes.Select(n => n.Code).Should().NotContain(TaxNoteCodes.Waiver2026);
        undeclared.Warnings.Should().Contain(w => w.Contains("IBSCBS.NOTE.2026-WAIVER"), "an undeclared fact is reported, not assumed");
    }

    [Fact]
    public void Item_under_a_special_regime_gets_no_rate_and_a_warning()
    {
        var output = V1.Run(Input(lines: Line(special: "reducao-60")), Catalogue());

        output.TotalTax.Should().Be(0m);
        output.Warnings.Should().Contain(w => w.Contains("regime diferenciado"));
    }

    [Fact]
    public void Inbound_with_unknown_supplier_regime_is_not_guessed()
    {
        var output = V1.Run(Input(direction: TaxDirection.Inbound), Catalogue());

        output.TotalTax.Should().Be(0m);
        output.Warnings.Should().Contain(w => w.Contains("regime do fornecedor não informado"));

        V1.Run(Input(direction: TaxDirection.Inbound, counterparty: TaxRegime.LucroReal), Catalogue())
            .TotalTax.Should().Be(8.40m, "for an acquisition the supplier is the counterparty");
    }

    [Fact]
    public void Missing_taxes_in_the_price_are_flagged()
    {
        var output = V1.Run(Input(lines: Line(informed: false)), Catalogue());

        output.Lines.Single().Base.Should().Be(920m);
        output.Warnings.Should().Contain(w => w.Contains("não foram informados"));
    }

    [Fact]
    public void Rules_pending_regulation_or_unverified_are_listed_and_never_applied()
    {
        var output = V1.Run(Input(date: new DateOnly(2027, 3, 1)), Catalogue());

        output.TotalTax.Should().Be(0m, "no 2027 rate is active in the catalogue");
        output.NotApplied.Select(n => (n.RuleKey, n.Status)).Should().Contain(
        [
            ("CBS.RATE.REFERENCE-2027-2033", TaxRuleStatus.PendingRegulation),
            ("IBS.RATE.2027-2028-STATE", TaxRuleStatus.Unverified),
            ("IBS.RATE.2027-2028-MUNICIPAL", TaxRuleStatus.Unverified),
        ]);
        output.Warnings.Should().Contain(w => w.StartsWith("Nenhuma alíquota vigente"));
    }

    [Fact]
    public void The_same_input_gives_byte_identical_output()
    {
        var a = TaxCanonicalV1.Instance.Serialize(V1.Run(Input(), Catalogue()));
        var b = TaxCanonicalV1.Instance.Serialize(V1.Run(Input(), Catalogue().Reverse().ToList()));

        b.Should().Be(a, "catalogue order must not matter");
        TaxCanonicalV1.Instance.Hash(a).Should().HaveLength(64);
    }

    [Fact]
    public void Canonical_form_ignores_trailing_zeros_of_decimals()
    {
        var a = Input(lines: new TaxEngineLine(1, TaxItemKind.Good, 10.5m, 0m, 0m, 0m, 0m, true));
        var b = Input(lines: new TaxEngineLine(1, TaxItemKind.Good, 10.50m, 0m, 0m, 0m, 0m, true));

        TaxCanonicalV1.Instance.HashOf(b).Should().Be(TaxCanonicalV1.Instance.HashOf(a));
    }

    [Fact]
    public void Output_does_not_depend_on_the_server_culture()
    {
        var invariant = WithCulture(CultureInfo.InvariantCulture, () => TaxCanonicalV1.Instance.Serialize(V1.Run(Input(), Catalogue())));
        var brazil = WithCulture(new CultureInfo("pt-BR"), () => TaxCanonicalV1.Instance.Serialize(V1.Run(Input(), Catalogue())));

        brazil.Should().Be(invariant);
    }

    [Fact]
    public void Money_keeps_cents_with_half_away_from_zero()
    {
        V1.RoundMoney(0.125m).Should().Be(0.13m);
        V1.RoundMoney(2.675m).Should().Be(2.68m);
        var output = V1.Run(Input(lines: new TaxEngineLine(1, TaxItemKind.Good, 0.05m, 0m, 0m, 0m, 0m, true)), Catalogue());
        output.Lines.Single().Components.Single(c => c.Tax == TaxType.Cbs).Amount.Should().Be(0.00m);
    }

    [Fact]
    public void Two_active_rates_for_the_same_tax_are_a_catalogue_conflict()
    {
        var day = new DateOnly(2031, 3, 3);
        var rules = new[] { Rate("A.RATE", 1, 1m, day, day), Rate("B.RATE", 1, 2m, day, day) };

        var act = () => V1.Run(Input(date: day), rules);

        act.Should().Throw<TaxRuleConflictException>();
    }

    [Fact]
    public void Temporal_lookup_picks_the_version_in_force_on_the_date()
    {
        var y2031 = new DateOnly(2031, 1, 1);
        var y2032 = new DateOnly(2032, 1, 1);
        var rules = new[]
        {
            Rate("X.RATE", 1, 1m, y2031, new DateOnly(2031, 12, 31)),
            Rate("X.RATE", 2, 2m, y2032, null),
        };

        V1.Run(Input(date: new DateOnly(2031, 6, 1)), rules).Lines.Single().Components.Single().RatePercent.Should().Be(1m);
        V1.Run(Input(date: new DateOnly(2032, 6, 1)), rules).Lines.Single().Components.Single().RatePercent.Should().Be(2m);
    }

    [Fact]
    public void A_new_version_never_changes_a_past_result_run_with_the_old_versions()
    {
        var day = new DateOnly(2031, 3, 3);
        var v1 = Rate("X.RATE", 1, 1m, day, day);
        var past = TaxCanonicalV1.Instance.Serialize(V1.Run(Input(date: day), [v1]));

        var v2 = Rate("X.RATE", 2, 3m, day, day);
        var now = V1.Run(Input(date: day), [v1, v2]);
        now.Lines.Single().Components.Single().RatePercent.Should().Be(3m, "the highest active version covering the date wins");

        TaxCanonicalV1.Instance.Serialize(V1.Run(Input(date: day), [v1])).Should().Be(past, "rebuilding with the versions it used gives the same output");
        V1.RuleSetHash(V1.SelectActive([v1, v2], day))
            .Should().NotBe(V1.RuleSetHash(V1.SelectActive([v1], day)));
    }

    [Fact]
    public void Superseded_and_revoked_versions_are_not_selected()
    {
        var day = new DateOnly(2031, 3, 3);
        var rules = new[]
        {
            Rate("X.RATE", 1, 1m, day, day, TaxRuleStatus.Superseded),
            Rate("Y.RATE", 1, 1m, day, day, TaxRuleStatus.Revoked, TaxType.IbsState),
        };

        V1.SelectActive(rules, day).Should().BeEmpty();
        V1.Run(Input(date: day), rules).NotApplied.Should().BeEmpty("only rules still to come are listed");
    }

    private static T WithCulture<T>(CultureInfo culture, Func<T> f)
    {
        var (c, ui) = (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture);
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = culture;
        try { return f(); }
        finally { CultureInfo.CurrentCulture = c; CultureInfo.CurrentUICulture = ui; }
    }
}
