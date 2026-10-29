using System.Text.Json;
using FluentAssertions;
using Nexo.Application.Modules.TaxIntelligence.Engine;
using Nexo.Domain.Enums;
using Nexo.Domain.Exceptions;
using Nexo.Domain.Modules.TaxIntelligence;
using Nexo.Infrastructure.TaxIntelligence;
using Nexo.Infrastructure.TaxIntelligence.Catalog;

namespace Nexo.UnitTests.TaxIntelligence;

public class TaxLedgerAndCatalogueTests
{
    private static readonly Guid Actor = Guid.NewGuid();
    private static readonly DateOnly Day = new(2026, 7, 1);
    private const string Key = "35261012345678000190550010000012341000012345";

    /// <summary>A credit whose requirements were confirmed: 90 to be appropriated.</summary>
    private static TaxCredit Credit(string? key = Key, DateOnly? expires = null)
    {
        var c = TaxCredit.Register(Guid.NewGuid(), CreditTax.Cbs, "NF-e", key, "12345678000190", new DateOnly(2026, 6, 1), expires,
            100m, 90m, "estimativa gerencial", null);
        c.StartRequirementsReview();
        c.ConfirmToBeAppropriated(90m, "requisitos conferidos", new DateOnly(2026, 6, 1), Actor, "u");
        return c;
    }

    [Fact]
    public void Balances_are_sums_of_the_entries()
    {
        var c = Credit();
        c.Balance().ToBeAppropriated.Should().Be(90m);

        c.Appropriate(60m, "ok", Day, Actor, "u");
        c.Use(20m, CreditTax.Cbs, "apuracao-2026-06", new DateOnly(2026, 7, 1), Actor, "u");
        c.Block(10m, "em análise", Day, Actor, "u");

        var b = c.Balance();
        (b.Potential, b.Appropriated, b.Used, b.Blocked, b.Available, b.ToBeAppropriated).Should().Be((90m, 60m, 20m, 10m, 30m, 30m));
        c.Entries.Select(e => e.Type).Should().Equal(
            TaxCreditEntryType.Recognized, TaxCreditEntryType.Appropriated, TaxCreditEntryType.Used, TaxCreditEntryType.Blocked);
    }

    [Fact]
    public void Appropriation_needs_the_electronic_document()
        => Credit(key: null).Invoking(c => c.Appropriate(10m, "x", Day, Actor, "u"))
            .Should().Throw<DomainException>().Which.Code.Should().Be("tax_credit_document");

    [Fact]
    public void A_credit_is_never_used_twice_nor_beyond_its_balance()
    {
        var c = Credit();
        c.Appropriate(50m, "ok", Day, Actor, "u");
        c.Use(30m, CreditTax.Cbs, "obr-1", new DateOnly(2026, 7, 1), Actor, "u");

        c.Invoking(x => x.Use(1m, CreditTax.Cbs, "obr-1", new DateOnly(2026, 7, 1), Actor, "u")).Should().Throw<ConflictException>();
        c.Invoking(x => x.Use(20.01m, CreditTax.Cbs, "obr-2", new DateOnly(2026, 7, 1), Actor, "u"))
            .Should().Throw<DomainException>().Which.Code.Should().Be("tax_credit_amount");
        c.Use(20m, CreditTax.Cbs, "obr-2", new DateOnly(2026, 7, 1), Actor, "u");
        c.Balance().Available.Should().Be(0m);
    }

    [Fact]
    public void Ibs_credit_never_pays_cbs()
    {
        var c = Credit();
        c.Appropriate(50m, "ok", Day, Actor, "u");
        c.Invoking(x => x.Use(10m, CreditTax.Ibs, "obr", new DateOnly(2026, 7, 1), Actor, "u"))
            .Should().Throw<DomainException>().Which.Code.Should().Be("tax_credit_cross_tax");
    }

    [Fact]
    public void A_reversed_appropriation_does_not_come_back_as_appropriable()
    {
        var c = Credit();
        c.Appropriate(50m, "ok", Day, Actor, "u");
        c.ReverseAppropriation(20m, "documento cancelado", Day, Actor, "u");

        var b = c.Balance();
        (b.Appropriated, b.Available, b.ToBeAppropriated).Should().Be((30m, 30m, 40m));
    }

    [Fact]
    public void Expired_credit_cannot_be_used_and_its_balance_is_written_off()
    {
        var c = Credit(expires: new DateOnly(2026, 12, 31));
        c.Appropriate(50m, "ok", Day, Actor, "u");

        c.Invoking(x => x.Use(1m, CreditTax.Cbs, "obr", new DateOnly(2027, 1, 2), Actor, "u"))
            .Should().Throw<DomainException>().Which.Code.Should().Be("tax_credit_expired");
        c.Invoking(x => x.Expire(new DateOnly(2026, 12, 31), Actor, "u")).Should().Throw<DomainException>();
        c.Expire(new DateOnly(2027, 1, 2), Actor, "u").Amount.Should().Be(50m);
        c.Balance().Available.Should().Be(0m);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(0.001)]
    public void Movements_are_positive_cents(decimal amount)
        => Credit().Invoking(c => c.Appropriate(amount, "x", Day, Actor, "u")).Should().Throw<DomainException>();

    [Fact]
    public void Split_reconciliation_reads_expected_versus_actual()
    {
        SplitReconciliation.Of(10m, [], 0).Status.Should().Be(SplitReconciliationStatus.Pending);
        SplitReconciliation.Of(10m, [10.01m], 1).Status.Should().Be(SplitReconciliationStatus.Matched);
        SplitReconciliation.Of(10m, [6m, 6m], 2).Should().Be(new SplitReconciliation(10m, 12m, 2m, SplitReconciliationStatus.Excess));
        SplitReconciliation.Of(10m, [7m], 1).Status.Should().Be(SplitReconciliationStatus.Insufficient);
        SplitReconciliation.Of(10m, [100m, -100m], 0).Status.Should().Be(SplitReconciliationStatus.Pending, "a reversed entry leaves nothing retained");
        SplitReconciliation.Of(10m, [100m, -100m, 10m], 1).Status.Should().Be(SplitReconciliationStatus.Matched);
    }

    [Fact]
    public void A_reversal_cancels_a_settlement_once_and_is_never_reversed_itself()
    {
        var s = SplitSettlement.Record(Guid.NewGuid(), Guid.NewGuid(), 100m, SplitSettlementSource.Manual, "psp-1", DateTime.UtcNow, Actor);
        var r = SplitSettlement.ReversalOf(s, "digitado errado", DateTime.UtcNow, Actor);
        (r.ActualAmount, r.ReversesSettlementId, r.ExternalReference).Should().Be((-100m, s.Id, $"estorno:{s.Id:N}"));
        FluentActions.Invoking(() => SplitSettlement.ReversalOf(r, "x", DateTime.UtcNow, Actor)).Should().Throw<DomainException>();
    }

    [Fact]
    public void Rule_versions_move_status_but_never_back()
    {
        var v = TaxRuleVersion.Create(Guid.NewGuid(), 1, new DateOnly(2026, 1, 1), null, 0, "[]", "{}", "hash", "t", "t", null, []);
        v.Status.Should().Be(TaxRuleStatus.Draft);
        v.ChangeStatus(TaxRuleStatus.Active, DateTime.UtcNow);
        v.Invoking(x => x.ChangeStatus(TaxRuleStatus.Draft, DateTime.UtcNow)).Should().Throw<DomainException>();
        v.ChangeStatus(TaxRuleStatus.Superseded, DateTime.UtcNow);
        v.Invoking(x => x.ChangeStatus(TaxRuleStatus.Active, DateTime.UtcNow)).Should().Throw<DomainException>();
    }

    [Fact]
    public void Profile_versions_are_added_never_edited()
    {
        var p = TaxProfile.Create(Guid.NewGuid());
        var v1 = p.AddVersion(new DateOnly(2026, 1, 1), TaxRegime.SimplesNacional, "SP", "3550308", "4711302", [], [], [], "início", Actor, "u");
        var v2 = p.AddVersion(new DateOnly(2026, 7, 1), TaxRegime.LucroPresumido, "SP", "3550308", "4711302", [], [], [], "saída do Simples", Actor, "u");

        (v1.VersionNumber, v2.VersionNumber).Should().Be((1, 2));
        v1.Regime.Should().Be(TaxRegime.SimplesNacional, "the old version keeps the old regime");
        p.Invoking(x => x.AddVersion(new DateOnly(2026, 1, 1), TaxRegime.LucroReal, "SP", "355030", "4711302", [], [], [], "x", Actor, "u"))
            .Should().Throw<DomainException>("IBGE code has 7 digits");
    }

    [Fact]
    public void Sale_discount_allocation_sums_to_the_cent()
    {
        var parts = SaleTaxFactsReader.Allocate(10m, [1m, 1m, 1m]);
        parts.Sum().Should().Be(10m);
        parts.Should().Equal(3.34m, 3.33m, 3.33m);
        SaleTaxFactsReader.Allocate(0m, [1m]).Should().Equal(0m);
        SaleTaxFactsReader.Allocate(5m, [0m, 0m]).Should().Equal(0m, 0m);
    }

    // ── Catalogue v1 ────────────────────────────────────────────────────────────

    [Fact]
    public void Catalogue_v1_is_frozen()
    {
        // Pinned fingerprints: if one changes, the seeded rows and the engine disagree — add a new version instead.
        var hashes = TaxCatalogV1.Rules.ToDictionary(r => r.Key, r => r.ContentHash);
        hashes.Should().HaveCount(12);
        hashes.Values.Should().OnlyHaveUniqueItems().And.OnlyContain(h => h.Length == 64);
        TaxCanonicalV1.Instance.Hash(string.Join("\n", hashes.OrderBy(h => h.Key, StringComparer.Ordinal).Select(h => $"{h.Key}={h.Value}")))
            .Should().Be(PinnedCatalogueHash);
    }

    /// <summary>Fingerprint of the whole catalogue v1 content (see test above).</summary>
    private const string PinnedCatalogueHash = "10f6a1ee6a81951c672c968b2598fe8a03d49c7d5f1b4f0166dd216808cfb4b1";

    [Fact]
    public void Every_rule_of_the_catalogue_is_valid_and_rests_on_a_seeded_source()
    {
        var codes = TaxCatalogV1.Sources.Select(s => s.Code).ToHashSet();
        foreach (var r in TaxCatalogV1.Rules)
        {
            r.SourceCodes.Should().NotBeEmpty();
            r.SourceCodes.Should().OnlyContain(c => codes.Contains(c), r.Key);
            FluentActions.Invoking(() => TaxRuleContentValidator.Validate(r.Key, r.Conditions, r.Result)).Should().NotThrow(r.Key);
        }
        TaxCatalogV1.Rules.Select(r => r.Key).Should().OnlyHaveUniqueItems();
        TaxCatalogV1.Sources.Should().OnlyContain(s => s.Excerpt.Length > 50);
    }

    [Fact]
    public void Rule_content_round_trips_through_its_json()
    {
        foreach (var r in TaxCatalogV1.Rules)
        {
            var conditions = TaxCanonicalV1.Instance.Deserialize<List<TaxCondition>>(TaxCanonicalV1.Instance.Serialize(r.Conditions));
            var result = TaxCanonicalV1.Instance.Deserialize<TaxRuleResult>(TaxCanonicalV1.Instance.Serialize(r.Result));
            TaxCanonicalV1.Instance.RuleContentHash(r.Key, 1, r.ValidFrom, r.ValidTo, r.Priority, conditions, result).Should().Be(r.ContentHash, r.Key);
        }
    }

    [Fact]
    public void Content_validator_refuses_what_the_engine_cannot_run()
    {
        FluentActions.Invoking(() => TaxRuleContentValidator.Validate("HYPOTHESIS.X", [], new NoteResult("a", "b"))).Should().Throw<DomainException>();
        FluentActions.Invoking(() => TaxRuleContentValidator.Validate("K", [], new RateResult(TaxType.Cbs, 101m))).Should().Throw<DomainException>();
        FluentActions.Invoking(() => TaxRuleContentValidator.Validate("K", [], new RateResult(TaxType.Cbs, 0.12345m))).Should().Throw<DomainException>();
        FluentActions.Invoking(() => TaxRuleContentValidator.Validate("K", [new ProfileFlagIs("whatever", true)], new NoteResult("a", "b"))).Should().Throw<DomainException>();
        FluentActions.Invoking(() => TaxRuleContentValidator.Validate("K", [new ItemGeneralRegime()], new NoteResult("a", "b"))).Should().Throw<DomainException>();
        FluentActions.Invoking(() => TaxCanonicalV1.Instance.Deserialize<TaxRuleResult>("{\"kind\":\"script\",\"code\":\"x\"}")).Should().Throw<Exception>();
    }

    [Fact]
    public void Content_with_missing_lists_or_undefined_enums_never_enters_the_catalogue()
    {
        // An activated rule the engine cannot run would turn every calculation of its dates into a 500.
        string[] bad =
        [
            "[{\"kind\":\"supplier-regime-in\"}]",
            "[{\"kind\":\"supplier-regime-not-in\",\"regimes\":[99]}]",
            "[{\"kind\":\"item-kind-in\"}]",
            "[{\"kind\":\"direction-is\",\"direction\":7}]",
            "[{\"kind\":\"profile-flag-is\",\"value\":true}]",
            "[null]",
        ];
        foreach (var json in bad)
            FluentActions.Invoking(() => TaxRuleContentValidator.Validate("K", TaxCanonicalV1.Instance.Deserialize<List<TaxCondition>>(json), new NoteResult("a", "b")))
                .Should().Throw<DomainException>(json);

        FluentActions.Invoking(() => TaxRuleContentValidator.Validate("K", [], TaxCanonicalV1.Instance.Deserialize<TaxRuleResult>("{\"kind\":\"base-inclusion\",\"component\":99}")))
            .Should().Throw<DomainException>();
        FluentActions.Invoking(() => TaxRuleContentValidator.Validate("K", [], TaxCanonicalV1.Instance.Deserialize<TaxRuleResult>("{\"kind\":\"rate\",\"tax\":9,\"ratePercent\":1}")))
            .Should().Throw<DomainException>();
        FluentActions.Invoking(() => TaxRuleContentValidator.Validate("K", null, new NoteResult("a", "b"))).Should().Throw<DomainException>();
    }

    [Fact]
    public void Ledger_entries_keep_the_date_they_refer_to()
    {
        var c = Credit();
        c.Appropriate(50m, "ok", Day, Actor, "u");
        c.Use(10m, CreditTax.Cbs, "obr", new DateOnly(2026, 6, 30), Actor, "u").OccurredOn.Should().Be(new DateOnly(2026, 6, 30));
        c.Entries.First().OccurredOn.Should().Be(c.AcquiredOn);
    }

    [Fact]
    public void Conditions_are_stored_with_a_kind_discriminator()
    {
        var json = TaxCanonicalV1.Instance.Serialize<IReadOnlyList<TaxCondition>>([new SupplierRegimeNotIn([TaxRegime.Mei]), new ItemGeneralRegime()]);
        using var doc = JsonDocument.Parse(json);
        doc.RootElement[0].GetProperty("kind").GetString().Should().Be("supplier-regime-not-in");
        doc.RootElement[0].GetProperty("regimes")[0].GetString().Should().Be("Mei");
        doc.RootElement[1].GetProperty("kind").GetString().Should().Be("item-general-regime");
    }
}
