using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Nexo.Application.Modules.TaxIntelligence;
using Nexo.Application.Modules.TaxIntelligence.Engine;
using Nexo.Domain.Enums;
using Nexo.Domain.Exceptions;
using Nexo.Domain.Modules.TaxIntelligence;
using Nexo.Infrastructure.Persistence.Migrations;
using Nexo.Infrastructure.TaxIntelligence.Catalog;
using static Nexo.UnitTests.TaxIntelligence.TaxTestData;

namespace Nexo.UnitTests.TaxIntelligence;

/// <summary>
/// Pre-merge hardening: engine and canonical versioning, a self-contained Foundation migration, the
/// nature of a calculated-tax reduction, ancillary-obligation states, text verification apart from
/// professional review, and segregation of duties. None of these tests changes or asserts a tax thesis.
/// </summary>
public class TaxHardeningTests
{
    // ── Engine registry and reproduction ────────────────────────────────────────

    /// <summary>A stand-in for a future engine: different version, different canonical form, must never run V1 work.</summary>
    private sealed class FakeEngineV2 : ITaxEngine
    {
        public const string VersionId = "orken-tax-engine/2.0.0-test";
        public int Runs { get; private set; }
        public string Version => VersionId;
        public ITaxCanonicalizer Canonical { get; } = new IndentedCanonical();
        public IReadOnlyList<TaxEngineRule> SelectActive(IEnumerable<TaxEngineRule> rules, DateOnly date) => V1.SelectActive(rules, date);
        public string RuleSetHash(IEnumerable<TaxEngineRule> selected) => "v2:" + V1.RuleSetHash(selected);
        public decimal RoundMoney(decimal value) => decimal.Round(value, 4);

        public TaxEngineOutput Run(TaxEngineInput input, IReadOnlyCollection<TaxEngineRule> catalogue)
        {
            Runs++;
            return V1.Run(input, catalogue) with { EngineVersion = VersionId };
        }
    }

    /// <summary>A different canonical strategy (indented JSON, different fingerprint) — what a V2 might choose.</summary>
    private sealed class IndentedCanonical : ITaxCanonicalizer
    {
        private readonly JsonSerializerOptions _options = new(TaxCanonicalV1.Instance.Options) { WriteIndented = true };
        public string Id => "test-canonical/2";
        public JsonSerializerOptions Options => _options;
        public string Serialize<T>(T value) => JsonSerializer.Serialize(value, _options);
        public T Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, _options)!;
        public string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("v2|" + text))).ToLowerInvariant();
        public string HashOf<T>(T value) => Hash(Serialize(value));

        public string RuleContentHash(string key, int version, DateOnly validFrom, DateOnly? validTo, int priority,
            IReadOnlyList<TaxCondition> conditions, TaxRuleResult result)
            => Hash($"{key}|{version}|{validFrom}|{validTo}|{priority}|{Serialize(conditions)}|{Serialize(result)}");
    }

    private static StoredCalculation StoredByV1(out TaxEngineOutput output)
    {
        var c = V1.Canonical;
        var input = Input();
        output = V1.Run(input, Catalogue());
        var inputJson = c.Serialize(input);
        var outputJson = c.Serialize(output);
        return new StoredCalculation(V1.Version, inputJson, c.Hash(inputJson), outputJson, c.Hash(outputJson),
            output.Executions.Select(e => new StoredRuleRun(e.RuleVersionId, e.RuleKey, e.RuleVersion, e.ContentHash)).ToList());
    }

    private static Func<ITaxEngine, IReadOnlyCollection<Guid>, Task<IReadOnlyList<TaxEngineRule>>> Loader(List<ITaxEngine>? seen = null)
        => (engine, ids) =>
        {
            seen?.Add(engine);
            return Task.FromResult<IReadOnlyList<TaxEngineRule>>(Catalogue().Where(r => ids.Contains(r.VersionId)).ToList());
        };

    [Fact]
    public void Registry_selects_an_engine_by_its_exact_version()
    {
        var v2 = new FakeEngineV2();
        var registry = new TaxEngineRegistry([V1, v2], FakeEngineV2.VersionId);

        registry.Current.Should().BeSameAs(v2);
        registry.TryGet(TaxEngineV1.VersionId, out var found).Should().BeTrue();
        found.Should().BeSameAs(V1);
        registry.TryGet("orken-tax-engine/1.0", out _).Should().BeFalse("versions are matched exactly, never by prefix");
        registry.Versions.Should().BeEquivalentTo([TaxEngineV1.VersionId, FakeEngineV2.VersionId]);

        TaxEngineRegistry.Default.Current.Version.Should().Be("orken-tax-engine/1.0.0");
        FluentActions.Invoking(() => new TaxEngineRegistry([V1, new TaxEngineV1()], TaxEngineV1.VersionId))
            .Should().Throw<InvalidOperationException>("a version is registered once");
        FluentActions.Invoking(() => new TaxEngineRegistry([V1], FakeEngineV2.VersionId))
            .Should().Throw<InvalidOperationException>("the current engine must be registered");
    }

    [Fact]
    public async Task A_V1_calculation_is_still_reproduced_by_V1_when_a_V2_is_current()
    {
        var v2 = new FakeEngineV2();
        var registry = new TaxEngineRegistry([V1, v2], FakeEngineV2.VersionId);
        var seen = new List<ITaxEngine>();
        var stored = StoredByV1(out _);

        var result = await TaxReproduction.VerifyAsync(stored, registry, Loader(seen));

        result.Reproducible.Should().BeTrue(string.Join(" | ", result.Differences));
        result.EngineAvailable.Should().BeTrue();
        result.StoredEngineVersion.Should().Be(TaxEngineV1.VersionId);
        result.CurrentEngineVersion.Should().Be(FakeEngineV2.VersionId);
        result.RecomputedOutputHash.Should().Be(stored.OutputHash);
        v2.Runs.Should().Be(0, "the current engine never stands in for the one that made the calculation");
        seen.Should().ContainSingle().Which.Should().BeSameAs(V1, "rule content is read with the V1 canonical form");
    }

    [Fact]
    public async Task An_unregistered_engine_version_is_reported_as_not_reproducible_without_running_anything()
    {
        var v2 = new FakeEngineV2();
        var registry = new TaxEngineRegistry([v2], FakeEngineV2.VersionId);
        var seen = new List<ITaxEngine>();
        var stored = StoredByV1(out _);

        var result = await TaxReproduction.VerifyAsync(stored, registry, Loader(seen));

        result.Reproducible.Should().BeFalse();
        result.EngineAvailable.Should().BeFalse();
        result.RecomputedOutputHash.Should().BeNull();
        result.Differences.Should().ContainSingle().Which.Should().Contain(TaxEngineV1.VersionId).And.Contain("não pode ser executada");
        v2.Runs.Should().Be(0);
        seen.Should().BeEmpty("nothing is loaded for an engine that is not there");
    }

    [Fact]
    public async Task Reproduction_reports_a_rule_whose_content_changed_since_the_calculation()
    {
        var stored = StoredByV1(out _);
        var tampered = stored with { RulesRun = stored.RulesRun.Select((r, i) => i == 0 ? r with { ContentHash = new string('0', 64) } : r).ToList() };

        var result = await TaxReproduction.VerifyAsync(tampered, TaxEngineRegistry.Default, Loader());

        result.Reproducible.Should().BeFalse();
        result.Differences.Should().Contain(d => d.Contains("mudou desde o cálculo"));
    }

    [Fact]
    public void Rules_as_then_restore_what_ran_as_active_and_keep_the_listed_status_of_the_rest()
    {
        var catalogue = Catalogue();
        var output = V1.Run(Input(), catalogue);
        var executed = output.Executions.Select(e => e.RuleVersionId).Distinct().ToList();
        var revokedToday = catalogue.Select(r => executed.Contains(r.VersionId) ? r with { Status = TaxRuleStatus.Revoked } : r).ToList();

        var asThen = TaxReproduction.RulesAsThen(executed, output, revokedToday);

        asThen.Where(r => executed.Contains(r.VersionId)).Should().OnlyContain(r => r.Status == TaxRuleStatus.Active);
        V1.Canonical.HashOf(V1.Run(Input(), asThen)).Should().Be(V1.Canonical.HashOf(output));
    }

    // ── Canonical form V1 ───────────────────────────────────────────────────────

    // Pinned fingerprints under "orken-canonical/1". If one changes, the frozen V1 chain changed.
    private const string InputHashV1 = "8c5413138fe56582768635d40afadb0fa2e480490b9c471ba1979d00fc381b5f";
    private const string UnconditionalDiscountRuleHashV1 = "fc56d1469478465d8b7f0bd07029f375c2ba3d64596930ba0baf3aa342ab380b";

    [Fact]
    public void Canonical_V1_fingerprints_are_fixed()
    {
        var c = TaxCanonicalV1.Instance;
        c.Id.Should().Be("orken-canonical/1");
        V1.Canonical.Should().BeSameAs(c, "engine V1 → canonical V1 → hashes V1");

        c.HashOf(Input()).Should().Be(InputHashV1);
        c.HashOf(V1.Run(Input(), Catalogue())).Should().Be("27ce3268053a7adce993a985a69c226d9c33a74f762f3697dfd3881b5e677591");
        var rule = TaxCatalogV1.Rules.Single(r => r.Key == "IBSCBS.BASE.UNCONDITIONAL-DISCOUNT");
        c.RuleContentHash(rule.Key, 1, rule.ValidFrom, rule.ValidTo, rule.Priority, rule.Conditions, rule.Result)
            .Should().Be(UnconditionalDiscountRuleHashV1);
    }

    [Fact]
    public void Another_canonical_strategy_gives_other_fingerprints_and_leaves_V1_untouched()
    {
        var other = new IndentedCanonical();
        var input = Input();

        other.HashOf(input).Should().NotBe(TaxCanonicalV1.Instance.HashOf(input));
        other.Serialize(input).Should().NotBe(TaxCanonicalV1.Instance.Serialize(input));
        TaxCanonicalV1.Instance.HashOf(input).Should().Be(InputHashV1, "a second strategy existing changes nothing in V1");
    }

    // ── Self-contained Foundation migration ─────────────────────────────────────

    private static IReadOnlyList<InsertDataOperation> SeedOperations()
        => new TaxIntelligenceFoundation().UpOperations.OfType<InsertDataOperation>().ToList();

    private static IEnumerable<IReadOnlyDictionary<string, object?>> Rows(string table)
    {
        foreach (var op in SeedOperations().Where(o => o.Table == table))
            for (var r = 0; r < op.Values.GetLength(0); r++)
                yield return op.Columns.Select((col, c) => (col, value: op.Values[r, c])).ToDictionary(x => x.col, x => x.value);
    }

    /// <summary>SHA-256 over every seeded value, in order — pins the literal content of the migration.</summary>
    private static string SeedFingerprint()
    {
        var sb = new StringBuilder();
        foreach (var op in SeedOperations())
        {
            sb.Append(op.Table).Append('|').AppendJoin(',', op.Columns).Append('\n');
            for (var r = 0; r < op.Values.GetLength(0); r++)
            {
                for (var c = 0; c < op.Values.GetLength(1); c++)
                    sb.Append(op.Values[r, c] switch
                    {
                        null => "∅",
                        DateTime d => d.ToString("O"),
                        DateOnly d => d.ToString("O"),
                        var v => Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture),
                    }).Append('\u001f');
                sb.Append('\n');
            }
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()))).ToLowerInvariant();
    }

    private const string SeedFingerprintV1 = "0a3b41db0c7eafa7f905e7aa3c2decabbcb5826bbbc8b6a1b13b8fb2afcae7dc";

    [Fact]
    public void The_migration_seeds_exactly_the_catalogue_V1_as_literal_rows()
    {
        var sources = Rows("tax_legal_sources").ToList();
        var rules = Rows("tax_rules").ToList();
        var versions = Rows("tax_rule_versions").ToList();
        var links = Rows("tax_rule_version_sources").ToList();

        sources.Select(s => (Guid)s["id"]!).Should().Equal(TaxCatalogV1.Sources.Select(s => s.Id));
        sources.Select(s => (string)s["excerpt"]!).Should().Equal(TaxCatalogV1.Sources.Select(s => s.Excerpt));
        rules.Select(r => (string)r["key"]!).Should().Equal(TaxCatalogV1.Rules.Select(r => r.Key));
        links.Should().HaveCount(TaxCatalogV1.Rules.Sum(r => r.SourceCodes.Count));

        foreach (var (row, seed) in versions.Zip(TaxCatalogV1.Rules))
        {
            row["id"].Should().Be(seed.VersionId);
            row["status"].Should().Be(seed.Status.ToString(), seed.Key);
            row["conditions"].Should().Be(TaxCanonicalV1.Instance.Serialize(seed.Conditions), seed.Key);
            row["result"].Should().Be(TaxCanonicalV1.Instance.Serialize(seed.Result), seed.Key);
            row["content_hash"].Should().Be(seed.ContentHash, seed.Key);
        }
    }

    [Fact]
    public void The_seeded_hashes_are_the_expected_literals_whatever_the_canonical_code_does()
    {
        // The rows are literals: a different canonical form would serialise and hash the same rules
        // differently, but the migration does not ask it — its content stays byte-identical.
        var other = new IndentedCanonical();
        var versions = Rows("tax_rule_versions").ToList();
        foreach (var (row, seed) in versions.Zip(TaxCatalogV1.Rules))
        {
            if (seed.Conditions.Count > 0) other.Serialize(seed.Conditions).Should().NotBe((string)row["conditions"]!);
            other.Serialize(seed.Result).Should().NotBe((string)row["result"]!);
            other.RuleContentHash(seed.Key, 1, seed.ValidFrom, seed.ValidTo, seed.Priority, seed.Conditions, seed.Result)
                .Should().NotBe((string)row["content_hash"]!);
        }

        versions.Single(v => (Guid)v["id"]! == TaxCatalogV1.Rules.Single(r => r.Key == "IBSCBS.BASE.UNCONDITIONAL-DISCOUNT").VersionId)["content_hash"]
            .Should().Be(UnconditionalDiscountRuleHashV1);
        SeedFingerprint().Should().Be(SeedFingerprintV1, "the literal seed of a released migration never changes");
    }

    [Fact]
    public void The_migration_source_does_not_reference_engine_catalogue_or_domain_code()
    {
        var dir = MigrationsDirectory();
        var files = Directory.GetFiles(dir, "20261027015241_TaxIntelligenceFoundation*.cs").Where(f => !f.EndsWith(".Designer.cs")).ToList();
        files.Should().HaveCount(2, "the schema part and the literal catalogue part");

        foreach (var file in files)
        {
            var code = File.ReadAllText(file);
            foreach (var forbidden in new[] { "TaxCatalogV1", "TaxCanonical", "TaxEngine", "Nexo.Application", "Nexo.Domain", "TaxIntelligence.Catalog", "JsonSerializer" })
                code.Should().NotContain(forbidden, $"{Path.GetFileName(file)} must be self-contained");
        }
        File.ReadAllText(files.Single(f => f.EndsWith(".CatalogV1.cs"))).Should().Contain("private static void SeedCatalogV1");
    }

    private static string MigrationsDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "Nexo.Infrastructure")))
            dir = dir.Parent;
        dir.Should().NotBeNull("the test runs inside the repository");
        return Path.Combine(dir!.FullName, "src", "Nexo.Infrastructure", "Persistence", "Migrations");
    }

    // ── Text verification vs professional review ────────────────────────────────

    [Fact]
    public void The_catalogue_V1_text_is_verified_and_every_rule_awaits_professional_review()
    {
        Rows("tax_legal_sources").Should().OnlyContain(s => (string)s["text_verification"]! == "Verified");
        Rows("tax_rule_versions").Should().OnlyContain(v => (string)v["professional_review"]! == "Pending"
            && v["professional_reviewed_by"] == null && v["professional_reviewed_at"] == null);
    }

    [Fact]
    public void Verifying_the_text_of_a_source_does_not_approve_any_rule()
    {
        var source = TaxLegalSource.Create("X-1", LegalSourceKind.ComplementaryLaw, "Lei", "art. 1", "https://www.planalto.gov.br/x", "Texto.", null, null);
        source.MarkVerified("conferente", DateTime.UtcNow);
        var version = TaxRuleVersion.Create(Guid.NewGuid(), 1, new DateOnly(2026, 1, 1), null, 1, "[]", "{}", "h", "platform", "admin", null, [source.Id]);
        version.ChangeStatus(TaxRuleStatus.Active, DateTime.UtcNow);

        source.IsTextVerified.Should().BeTrue();
        version.Status.Should().Be(TaxRuleStatus.Active);
        version.ProfessionalReview.Should().Be(ProfessionalReviewStatus.Pending, "technical activation is not professional approval");
    }

    [Fact]
    public void A_professional_review_is_recorded_apart_from_the_status_and_must_explain_a_refusal()
    {
        var version = TaxRuleVersion.Create(Guid.NewGuid(), 1, new DateOnly(2026, 1, 1), null, 1, "[]", "{}", "h", "platform", "admin", null, []);
        var at = new DateTime(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);

        static ProfessionalReviewInput Review(ProfessionalReviewStatus s, string reviewer, string? notes)
            => new(s, reviewer, null, null, null, null, null, null, notes, [], null, null, Guid.NewGuid(), "operador", "Platform");
        version.Invoking(v => v.RecordProfessionalReview(Review(ProfessionalReviewStatus.Rejected, "Contadora", null), at)).Should().Throw<DomainException>();
        version.Invoking(v => v.RecordProfessionalReview(Review(ProfessionalReviewStatus.Approved, " ", null), at)).Should().Throw<DomainException>();

        version.RecordProfessionalReview(Review(ProfessionalReviewStatus.ChangesRequired, " Contadora ", " ajustar vigência "), at);
        version.ProfessionalReview.Should().Be(ProfessionalReviewStatus.ChangesRequired);
        version.ProfessionalReviewedBy.Should().Be("Contadora");
        version.ProfessionalReviewedAt.Should().Be(at);
        version.ProfessionalReviewNotes.Should().Be("ajustar vigência");
        version.Status.Should().Be(TaxRuleStatus.Draft, "a review never moves the technical status");
    }

    // ── Ancillary obligations ───────────────────────────────────────────────────

    internal static TaxComplianceAssessment Assessment(ComplianceAssessmentStatus status, string? scope = "estabelecimento",
        string? evidence = null, bool pnct = false, string? notes = null, IReadOnlyList<string>? checkedList = null)
        => TaxComplianceAssessment.Record(Guid.NewGuid(), status, new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 30), scope,
            checkedList ?? [], evidence, "Contabilidade", DateTime.UtcNow, notes, null, pnct, Guid.NewGuid(), "u", Results(status, evidence, checkedList));

    /// <summary>A verification lists each obligation with its evidence (never only free text): one result per obligation checked.</summary>
    internal static IReadOnlyList<TaxObligationResultInput>? Results(ComplianceAssessmentStatus status, string? evidence, IReadOnlyList<string>? checkedList)
        => status is ComplianceAssessmentStatus.Verified or ComplianceAssessmentStatus.RemediationVerified && checkedList is { Count: > 0 }
            ? checkedList.Select(c => new TaxObligationResultInput(c.ToUpperInvariant().Replace(' ', '-'), c, status,
                string.IsNullOrWhiteSpace(evidence) ? null : evidence, null,
                status == ComplianceAssessmentStatus.RemediationVerified ? "campos corrigidos" : null,
                status == ComplianceAssessmentStatus.RemediationVerified ? new DateOnly(2026, 6, 1) : null,
                status == ComplianceAssessmentStatus.RemediationVerified ? "Contabilidade" : null, null)).ToList()
            : null;

    [Fact]
    public void A_declaration_of_compliance_is_not_a_verification()
    {
        var declared = Assessment(ComplianceAssessmentStatus.Declared);
        var verified = Assessment(ComplianceAssessmentStatus.Verified, evidence: "recibos de entrega conferidos", checkedList: ["NF-e com IBS/CBS"]);

        declared.ForRules.Should().BeNull("a declaration alone satisfies no rule that needs verified compliance");
        verified.ForRules.Should().BeTrue();
        Assessment(ComplianceAssessmentStatus.NonCompliant, notes: "NF-e sem os campos").ForRules.Should().BeFalse();

        V1.Run(Input(compliant: declared.ForRules), Catalogue()).Notes.Select(n => n.Code).Should().NotContain(TaxNoteCodes.Waiver2026);
        V1.Run(Input(compliant: verified.ForRules), Catalogue()).Notes.Select(n => n.Code).Should().Contain(TaxNoteCodes.Waiver2026);

        TaxComplianceAdvisories.For(declared).Should().ContainSingle()
            .Which.Should().Contain("não verificadas").And.Contain("revisão profissional");
        TaxComplianceAdvisories.For(verified).Should().ContainSingle()
            .Which.Should().StartWith("Obrigações verificadas no período e no escopo indicados").And.NotContainEquivalentOf("empresa conforme");
    }

    [Fact]
    public void Unknown_compliance_draws_no_conclusion()
    {
        var unknown = Assessment(ComplianceAssessmentStatus.Unknown, scope: null);

        unknown.ForRules.Should().BeNull();
        var output = V1.Run(Input(compliant: unknown.ForRules), Catalogue());
        output.Notes.Select(n => n.Code).Should().NotContain(TaxNoteCodes.Waiver2026);
        TaxComplianceAdvisories.For(null).Should().ContainSingle().Which.Should().Contain("nenhuma conclusão");
    }

    [Fact]
    public void Verified_compliance_requires_the_evidence_that_was_checked()
    {
        FluentActions.Invoking(() => Assessment(ComplianceAssessmentStatus.Verified, evidence: " ", checkedList: ["NF-e"]))
            .Should().Throw<DomainException>().Which.Code.Should().Be("tax_compliance_evidence");
        // Without the obligations checked, a verification is free text only: refused before any evidence is weighed.
        FluentActions.Invoking(() => Assessment(ComplianceAssessmentStatus.Verified, evidence: "recibos"))
            .Should().Throw<DomainException>().Which.Code.Should().Be("tax_compliance_results");
    }

    // ── Calculated-tax reduction ────────────────────────────────────────────────

    [Fact]
    public void A_2026_unconditional_discount_is_a_calculated_tax_reduction_never_a_definitive_saving()
    {
        var candidate = new UnconditionalDiscountDetector().Detect(V1, Input(), Catalogue()).Single();
        var o = TaxOpportunity.Create(Guid.NewGuid(), candidate.Type, candidate.Title, candidate.Summary, candidate.DetectorKey,
            V1.Version, null, "{}", "{}", candidate.EstimatedImpact, candidate.Risk, candidate.Confidence, "", candidate.Caveats);

        o.ImpactNature.Should().Be(TaxImpactNature.CalculatedTaxReduction);
        o.ImpactNature.Should().NotBe(TaxImpactNature.DefinitiveSaving);
        o.EngineVersion.Should().Be(TaxEngineV1.VersionId);
    }

    // ── Segregation of duties ───────────────────────────────────────────────────

    private static TaxOpportunity Reviewed(Guid analyst)
    {
        var o = TaxOpportunity.Create(Guid.NewGuid(), TaxOpportunityType.CalculatedTaxReduction, "t", "s", "d@1", TaxEngineV1.VersionId,
            null, "{}", "{}", 1m, TaxRiskLevel.Low, 90, "", null);
        o.AddRequirement("a", "A", null);
        o.StartReview(analyst, "Ana", DateTime.UtcNow);
        o.AnswerRequirement(o.Requirements.Single().Id, true, null, analyst, "Ana", DateTime.UtcNow);
        return o;
    }

    [Fact]
    public void Whoever_analysed_alone_must_justify_approving_their_own_analysis()
    {
        var ana = Guid.NewGuid();
        var o = Reviewed(ana);

        o.IsSoleAnalyst(ana).Should().BeTrue();
        o.ReviewStartedById.Should().Be(ana);
        o.Invoking(x => x.Approve(false, ana, "Ana", DateTime.UtcNow, ApprovalRequirement.SelfApprovalWithJustification))
            .Should().Throw<DomainException>().Which.Code.Should().Be("tax_self_approval_requires_justification");

        o.Approve(false, ana, "Ana", DateTime.UtcNow, ApprovalRequirement.SelfApprovalWithJustification, "equipe de uma pessoa");
        o.Status.Should().Be(TaxOpportunityStatus.Approved);
    }

    [Fact]
    public void Another_person_approves_without_justification()
    {
        var ana = Guid.NewGuid();
        var bia = Guid.NewGuid();

        var byOther = Reviewed(ana);
        byOther.IsSoleAnalyst(bia).Should().BeFalse();
        byOther.Approve(false, bia, "Bia", DateTime.UtcNow, ApprovalRequirement.IndependentApprover);
        byOther.Status.Should().Be(TaxOpportunityStatus.Approved);
    }

    [Fact]
    public void A_decision_keeps_the_self_approval_justification()
    {
        var d = TaxDecision.Create(Guid.NewGuid(), Guid.NewGuid(), "s", "{}", "h", TaxRiskLevel.Low, false, TaxEngineV1.VersionId,
            Guid.NewGuid(), "Ana", DateTime.UtcNow, "  equipe de uma pessoa ");
        d.SelfApprovalJustification.Should().Be("equipe de uma pessoa");
        TaxDecision.Create(Guid.NewGuid(), Guid.NewGuid(), "s", "{}", "h", TaxRiskLevel.Low, false, TaxEngineV1.VersionId,
            Guid.NewGuid(), "Ana", DateTime.UtcNow).SelfApprovalJustification.Should().BeNull();
    }
}
