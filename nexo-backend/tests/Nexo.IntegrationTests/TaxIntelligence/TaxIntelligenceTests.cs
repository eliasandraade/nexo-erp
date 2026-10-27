using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nexo.Application.Modules.TaxIntelligence.Engine;
using Nexo.Application.Features.Cash;
using Nexo.Application.Features.Products;
using Nexo.Application.Features.Sales;
using Nexo.Application.Features.Users;
using Nexo.Infrastructure.Persistence;
using Nexo.IntegrationTests.Common;
using Nexo.IntegrationTests.Helpers;

namespace Nexo.IntegrationTests.TaxIntelligence;

/// <summary>
/// Tax Intelligence end to end on a real PostgreSQL: catalogue seeded by the migration, deterministic
/// and idempotent calculations, reproducibility after the catalogue changes, the opportunity workflow
/// up to the decision dossier, guardrails, simulation isolation, credits, split payment, tenant
/// isolation and authorization. Every operation uses unique amounts or descriptions so tests sharing
/// the database never reuse each other's calculations.
/// </summary>
[Collection("Integration")]
public class TaxIntelligenceTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    private readonly TestWebApplicationFactory _factory;

    public TaxIntelligenceTests(TestWebApplicationFactory factory) => _factory = factory;

    // ── helpers ─────────────────────────────────────────────────────────────────

    private static Task<HttpResponseMessage> Post(HttpClient c, string url, object body) => c.PostAsJsonAsync(url, body, Web);

    private static async Task<JsonElement> Json(HttpResponseMessage r, HttpStatusCode expected = HttpStatusCode.OK)
    {
        var text = await r.Content.ReadAsStringAsync();
        r.StatusCode.Should().Be(expected, text);
        return string.IsNullOrEmpty(text) ? default : JsonDocument.Parse(text).RootElement.Clone();
    }

    private async Task<HttpClient> AdminWithProfileAsync()
    {
        var admin = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var profile = await Json(await admin.GetAsync("/api/tax/profile"));
        if (profile.GetProperty("current").ValueKind == JsonValueKind.Null)
            await Json(await Post(admin, "/api/tax/profile/versions", new
            {
                validFrom = "2026-01-01", regime = "LucroPresumido", state = "SP", cityIbgeCode = "3550308", primaryCnae = "4711302",
                secondaryCnaes = Array.Empty<string>(), specialRegimes = Array.Empty<string>(), benefits = Array.Empty<string>(),
                accessoryObligations = "Unknown", accessoryObligationsEvidence = (string?)null, reason = "Perfil de teste",
            }));
        return admin;
    }

    /// <summary>Gross, unconditional 100, conditional 50, surcharges 20, taxes in the price 80.</summary>
    private static object Operation(string description, decimal gross = 1000m, string date = "2026-06-15", decimal conditional = 50m)
        => new
        {
            operationDate = date, direction = "Outbound", counterpartyRegime = (string?)null, counterpartyState = (string?)null, description,
            lines = new[]
            {
                new
                {
                    itemKind = "Good", description = "Produto", ncm = (string?)null, quantity = 1m, grossAmount = gross,
                    unconditionalDiscount = 100m, conditionalDiscount = conditional, surcharges = 20m, excludedTaxes = (decimal?)80m,
                    specialTreatment = (string?)null,
                },
            },
        };

    private static string Unique(string what) => $"{what} {Guid.NewGuid():N}";

    private async Task<HttpClient> UserAsync(HttpClient admin, string role)
    {
        var login = $"{role[..3]}{Guid.NewGuid():N}"[..20];
        var password = "Tx!" + Guid.NewGuid().ToString("N")[..16];
        var created = await admin.PostAsJsonAsync("/api/users", new CreateUserRequest(
            FullName: $"Pessoa {role}", Email: $"{login}@{TestCredentials.TestDomain}", Login: login, Password: password,
            Role: role, Phone: null, Notes: null, RequirePasswordChange: false));
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        return await AuthClientFactory.LoginAsync(_factory, login, password);
    }

    private async Task<HttpClient> PlatformAsync()
    {
        var client = _factory.CreateApiClient();
        var resp = await client.PostAsJsonAsync("/api/platform/auth/login",
            new { email = TestCredentials.PlatformEmail, password = TestCredentials.PlatformPassword });
        var body = await Json(resp);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body.GetProperty("accessToken").GetString());
        return client;
    }

    private async Task<int> CountAsync(Func<NexoDbContext, IQueryable<object>> q)
    {
        using var scope = _factory.Services.CreateScope();
        return await q(scope.ServiceProvider.GetRequiredService<NexoDbContext>()).CountAsync();
    }

    // ── catalogue ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Migration_seeds_the_catalogue_with_verified_literal_sources()
    {
        var admin = await AuthClientFactory.LoginAsAdminAsync(_factory);

        var rules = await Json(await admin.GetAsync("/api/tax/rules"));
        var cbs = rules.EnumerateArray().Single(r => r.GetProperty("key").GetString() == "CBS.RATE.2026-TEST");
        var v1 = cbs.GetProperty("versions").EnumerateArray().Single(v => v.GetProperty("version").GetInt32() == 1);
        v1.GetProperty("status").GetString().Should().Be("Active");
        v1.GetProperty("legalSourceCodes").EnumerateArray().Select(c => c.GetString()).Should().BeEquivalentTo("LC214-2025-ART346", "LC214-2025-ART348");
        v1.GetProperty("result").GetProperty("ratePercent").GetDecimal().Should().Be(0.9m);
        rules.EnumerateArray().Single(r => r.GetProperty("key").GetString() == "CBS.RATE.REFERENCE-2027-2033")
            .GetProperty("versions")[0].GetProperty("status").GetString().Should().Be("PendingRegulation");

        var sources = await Json(await admin.GetAsync("/api/tax/legal-sources"));
        var art12 = sources.EnumerateArray().Single(s => s.GetProperty("code").GetString() == "LC214-2025-ART12-P2-III-P3");
        art12.GetProperty("textVerification").GetString().Should().Be("Verified", "the excerpt matches the official text");
        rules.EnumerateArray().SelectMany(r => r.GetProperty("versions").EnumerateArray())
            .Should().OnlyContain(v => v.GetProperty("professionalReview").GetString() == "Pending"
                || v.GetProperty("origin").GetString() != "seed:tax-catalog-v1", "nothing in the catalogue V1 is professionally approved");
        v1.GetProperty("professionalReview").GetString().Should().Be("Pending", "an active rule is not an approved interpretation");
        art12.GetProperty("excerpt").GetString().Should().Contain("não dependa de evento posterior");
        art12.GetProperty("url").GetString().Should().StartWith("https://www.planalto.gov.br/");
    }

    // ── calculations ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Calculation_follows_article_12_is_idempotent_and_reproducible()
    {
        var admin = await AdminWithProfileAsync();
        var op = Operation(Unique("Proposta"));

        var first = await Json(await Post(admin, "/api/tax/calculations", op));
        first.GetProperty("totalBase").GetDecimal().Should().Be(840m);
        first.GetProperty("totalTax").GetDecimal().Should().Be(8.40m);
        first.GetProperty("reused").GetBoolean().Should().BeFalse();
        first.GetProperty("engineVersion").GetString().Should().Be("orken-tax-engine/1.0.0");
        var executions = first.GetProperty("output").GetProperty("executions").EnumerateArray().ToList();
        executions.Should().Contain(e => e.GetProperty("ruleKey").GetString() == "CBS.RATE.2026-TEST" && e.GetProperty("matched").GetBoolean());

        var again = await Json(await Post(admin, "/api/tax/calculations", op));
        again.GetProperty("id").GetGuid().Should().Be(first.GetProperty("id").GetGuid());
        again.GetProperty("reused").GetBoolean().Should().BeTrue();
        var id = first.GetProperty("id").GetGuid();
        (await CountAsync(db => db.TaxCalculations.IgnoreQueryFilters().Where(c => c.Id == id))).Should().Be(1);

        var verify = await Json(await Post(admin, $"/api/tax/calculations/{id}/verify", new { }));
        verify.GetProperty("reproducible").GetBoolean().Should().BeTrue();
        verify.GetProperty("engineAvailable").GetBoolean().Should().BeTrue();
        verify.GetProperty("recomputedOutputHash").GetString().Should().Be(first.GetProperty("outputHash").GetString());
        first.GetProperty("profileAdvisories")[0].GetString().Should().Contain("não informado", "unknown compliance is said, not assumed");
    }

    [Fact]
    public async Task A_new_rule_version_never_changes_a_past_calculation()
    {
        var admin = await AdminWithProfileAsync();
        var platform = await PlatformAsync();
        var tag = Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();
        const string day = "2031-03-03";

        var source = $"TEST-SRC-{tag}";
        await Json(await Post(platform, "/api/platform/tax/legal-sources", new
        {
            code = source, kind = "Other", title = "Fonte de teste", reference = "art. 1", url = "https://www.planalto.gov.br/teste",
            excerpt = "Texto literal de teste usado apenas pela suíte de integração.", publishedOn = (string?)null, notes = (string?)null,
        }));
        var key = $"TEST.RATE.{tag}";
        await Json(await Post(platform, "/api/platform/tax/rules", new { key, name = "Taxa de teste", description = "Só para testes." }));

        async Task<Guid> VersionAsync(decimal rate) => (await Json(await Post(platform, $"/api/platform/tax/rules/{key}/versions", new
        {
            validFrom = day, validTo = day, priority = 5,
            conditions = Array.Empty<object>(), result = new { kind = "rate", tax = "Cbs", ratePercent = rate },
            legalSourceCodes = new[] { source }, notes = (string?)null,
        }))).GetProperty("id").GetGuid();

        var v1 = await VersionAsync(1.5m);
        var blocked = await Post(platform, $"/api/platform/tax/rule-versions/{v1}/status", new { status = "Active" });
        (await Json(blocked, HttpStatusCode.UnprocessableEntity)).GetProperty("code").GetString().Should().Be("tax_rule_unverified_source");
        await Json(await Post(platform, $"/api/platform/tax/legal-sources/{source}/verify", new { verifiedBy = "suite" }));
        await Json(await Post(platform, $"/api/platform/tax/rule-versions/{v1}/status", new { status = "Active" }));

        var op = Operation(Unique("Histórico"), date: day);
        var past = await Json(await Post(admin, "/api/tax/calculations", op));
        past.GetProperty("totalTax").GetDecimal().Should().Be(12.60m, "1,5% de 840");

        var v2 = await VersionAsync(2m);
        await Json(await Post(platform, $"/api/platform/tax/rule-versions/{v2}/status", new { status = "Active" }));

        var now = await Json(await Post(admin, "/api/tax/calculations", op));
        now.GetProperty("id").GetGuid().Should().NotBe(past.GetProperty("id").GetGuid(), "different rules → a new calculation");
        now.GetProperty("totalTax").GetDecimal().Should().Be(16.80m);

        var pastId = past.GetProperty("id").GetGuid();
        (await Json(await admin.GetAsync($"/api/tax/calculations/{pastId}"))).GetProperty("totalTax").GetDecimal().Should().Be(12.60m);
        (await Json(await Post(admin, $"/api/tax/calculations/{pastId}/verify", new { }))).GetProperty("reproducible").GetBoolean()
            .Should().BeTrue("rebuilt with the superseded version it used");

        var rule = (await Json(await platform.GetAsync("/api/platform/tax/rules"))).EnumerateArray().Single(r => r.GetProperty("key").GetString() == key);
        rule.GetProperty("versions").EnumerateArray().Single(v => v.GetProperty("version").GetInt32() == 1)
            .GetProperty("status").GetString().Should().Be("Superseded");
    }

    // ── opportunities ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Opportunity_goes_from_detection_to_an_approved_decision_without_changing_the_operation()
    {
        var admin = await AdminWithProfileAsync();
        var calc = await Json(await Post(admin, "/api/tax/calculations", Operation(Unique("Workflow"), gross: 1001m)));
        var oppId = calc.GetProperty("opportunityIds")[0].GetGuid();

        var opp = await Json(await admin.GetAsync($"/api/tax/opportunities/{oppId}"));
        opp.GetProperty("status").GetString().Should().Be("Identified", "detection only proposes");
        opp.GetProperty("type").GetString().Should().Be("CalculatedTaxReduction");
        opp.GetProperty("impactNature").GetString().Should().Be("CalculatedTaxReduction", "a lower IBS/CBS on the document is not a definitive saving");
        opp.GetProperty("summary").GetString().Should().Contain("redução do IBS/CBS calculado");
        opp.GetProperty("engineVersion").GetString().Should().Be("orken-tax-engine/1.0.0");
        opp.GetProperty("professionalReviewPending").GetBoolean().Should().BeTrue();
        opp.GetProperty("estimatedImpact").GetDecimal().Should().Be(0.50m);

        var gerente = await UserAsync(admin, "gerente");
        (await Post(gerente, $"/api/tax/opportunities/{oppId}/review", new { })).StatusCode.Should().Be(HttpStatusCode.OK, "review is management");
        foreach (var r in (await Json(await admin.GetAsync($"/api/tax/opportunities/{oppId}"))).GetProperty("requirements").EnumerateArray())
            await Json(await Post(gerente, $"/api/tax/opportunities/{oppId}/requirements/{r.GetProperty("id").GetGuid()}", new { met = true, note = "conferido" }));
        await Json(await Post(gerente, $"/api/tax/opportunities/{oppId}/evidence", new { description = "Nova proposta comercial sem condição", documentReference = "PROP-123" }));

        (await Post(gerente, $"/api/tax/opportunities/{oppId}/approve", new { acknowledgeHighRisk = false }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden, "approval is Diretoria only");

        var approved = await Json(await Post(admin, $"/api/tax/opportunities/{oppId}/approve", new { acknowledgeHighRisk = false }));
        approved.GetProperty("status").GetString().Should().Be("Approved");
        var decision = approved.GetProperty("decision");
        decision.GetProperty("dossierHash").GetString().Should().HaveLength(64);
        var dossier = decision.GetProperty("dossier");
        dossier.GetProperty("legalSources").EnumerateArray().Select(s => s.GetProperty("code").GetString())
            .Should().Contain("LC214-2025-ART12-P2-III-P3");
        dossier.GetProperty("evidence")[0].GetProperty("documentReference").GetString().Should().Be("PROP-123");
        dossier.GetProperty("engineVersion").GetString().Should().Be("orken-tax-engine/1.0.0");
        dossier.GetProperty("legalSources")[0].GetProperty("textVerification").GetString().Should().Be("Verified");
        dossier.GetProperty("professionalReview").GetProperty("pendingRules").GetArrayLength().Should().BeGreaterThan(0);
        dossier.GetProperty("segregationOfDuties").GetProperty("selfApproval").GetBoolean().Should().BeFalse("the gerente analysed, the Diretoria approved");
        dossier.GetProperty("reviewStartedBy").GetString().Should().Be("Pessoa gerente");
        decision.GetProperty("selfApprovalJustification").ValueKind.Should().Be(JsonValueKind.Null);

        var after = await Json(await admin.GetAsync($"/api/tax/calculations/{calc.GetProperty("id").GetGuid()}"));
        after.GetProperty("input").GetProperty("lines")[0].GetProperty("conditionalDiscount").GetDecimal()
            .Should().Be(50m, "approving a strategy never rewrites the operation");
    }

    [Fact]
    public async Task A_discount_that_still_depends_on_a_later_event_is_blocked_and_cannot_be_approved()
    {
        var admin = await AdminWithProfileAsync();
        var calc = await Json(await Post(admin, "/api/tax/calculations", Operation(Unique("Proibida"), gross: 1002m)));
        var oppId = calc.GetProperty("opportunityIds")[0].GetGuid();
        await Json(await Post(admin, $"/api/tax/opportunities/{oppId}/review", new { }));
        var requirement = (await Json(await admin.GetAsync($"/api/tax/opportunities/{oppId}"))).GetProperty("requirements").EnumerateArray()
            .Single(r => r.GetProperty("code").GetString() == "no-later-event");

        var blocked = await Json(await Post(admin, $"/api/tax/opportunities/{oppId}/requirements/{requirement.GetProperty("id").GetGuid()}",
            new { met = false, note = "o desconto só vale se pagar em 10 dias" }));

        blocked.GetProperty("status").GetString().Should().Be("Blocked");
        blocked.GetProperty("blockedAs").GetString().Should().Be("DocumentMismatch");
        blocked.GetProperty("estimatedImpact").ValueKind.Should().Be(JsonValueKind.Null, "no saving is offered for a prohibited structure");
        blocked.GetProperty("proposed").ValueKind.Should().Be(JsonValueKind.Null);
        blocked.GetProperty("summary").GetString().Should().NotContain("a menos de tributo", "the saving is not advertised, even in the summary");
        var approve = await Post(admin, $"/api/tax/opportunities/{oppId}/approve", new { acknowledgeHighRisk = true });
        (await Json(approve, HttpStatusCode.UnprocessableEntity)).GetProperty("code").GetString().Should().Be("tax_opportunity_blocked");
    }

    // ── simulations ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Simulation_never_touches_official_data()
    {
        var admin = await AdminWithProfileAsync();
        async Task<int[]> Snapshot() =>
        [
            await CountAsync(db => db.TaxCalculations.IgnoreQueryFilters()),
            await CountAsync(db => db.TaxTransactions.IgnoreQueryFilters()),
            await CountAsync(db => db.TaxOpportunities.IgnoreQueryFilters()),
            await CountAsync(db => db.TaxCredits.IgnoreQueryFilters()),
            await CountAsync(db => db.TaxCreditLedgerEntries.IgnoreQueryFilters()),
            await CountAsync(db => db.TaxSplitExpectations.IgnoreQueryFilters()),
            await CountAsync(db => db.FinancialTransactions.IgnoreQueryFilters()),
            await CountAsync(db => db.Sales.IgnoreQueryFilters()),
        ];
        var before = await Snapshot();

        var sim = await Json(await Post(admin, "/api/tax/simulations", new
        {
            name = Unique("Simulação"),
            baseline = Operation(Unique("Base")),
            scenarios = new object[]
            {
                new { name = "Incondicional", regime = (string?)null, referenceDate = (string?)null, direction = (string?)null,
                      counterpartyRegime = (string?)null, lines = (object?)null, convertConditionalDiscounts = true, rateHypotheses = (object?)null, assumeCreditable = false },
                new { name = "Hipótese 2027", regime = (string?)null, referenceDate = "2027-06-15", direction = (string?)null,
                      counterpartyRegime = (string?)null, lines = (object?)null, convertConditionalDiscounts = false,
                      rateHypotheses = new[] { new { tax = "Cbs", ratePercent = 8.8m, label = "estimativa" } }, assumeCreditable = false },
            },
        }));

        (await Snapshot()).Should().Equal(before);
        var scenarios = sim.GetProperty("scenarios").EnumerateArray().ToList();
        scenarios.Should().HaveCount(3);
        scenarios[1].GetProperty("taxDeltaVsBaseline").GetDecimal().Should().Be(-0.50m);
        scenarios[2].GetProperty("hypotheses").EnumerateArray().Should().NotBeEmpty();
        (await Json(await admin.GetAsync($"/api/tax/simulations/{sim.GetProperty("id").GetGuid()}"))).GetProperty("scenarios").GetArrayLength().Should().Be(3);
    }

    [Fact]
    public async Task Supplier_comparison_ranks_by_effective_cost()
    {
        var admin = await AdminWithProfileAsync();
        object Offer(string name, decimal gross) => new
        {
            supplier = name, supplierRegime = "LucroReal",
            lines = new[] { new { itemKind = "Good", description = "Insumo", ncm = (string?)null, quantity = 1m, grossAmount = gross,
                unconditionalDiscount = 0m, conditionalDiscount = 0m, surcharges = 0m, excludedTaxes = (decimal?)0m, specialTreatment = (string?)null } },
        };

        var result = await Json(await Post(admin, "/api/tax/simulations/supplier-comparison", new
        {
            operationDate = "2027-06-15", offers = new[] { Offer("A", 98000m), Offer("B", 101000m) },
            rateHypotheses = new[] { new { tax = "Cbs", ratePercent = 10m, label = "hipótese" } }, assumeCreditable = true,
        }));

        var offers = result.EnumerateArray().ToList();
        offers[0].GetProperty("creditEstimate").GetDecimal().Should().Be(9800m);
        offers[0].GetProperty("effectiveCost").GetDecimal().Should().Be(88200m);
        offers[1].GetProperty("effectiveCost").GetDecimal().Should().Be(90900m);
        offers[0].GetProperty("cheapest").GetBoolean().Should().BeTrue();
    }

    // ── credits and split ───────────────────────────────────────────────────────

    [Fact]
    public async Task Credit_balance_is_never_used_twice_even_under_concurrency()
    {
        var admin = await AdminWithProfileAsync();
        var credit = await Json(await Post(admin, "/api/tax/credits", new
        {
            tax = "Cbs", sourceDocumentType = "NF-e", sourceDocumentKey = "35261012345678000190550010000012341000012345",
            supplierDocument = "12345678000190", acquiredOn = "2026-06-01", expiresOn = (string?)null,
            originalAmount = 100m, eligibleAmount = 100m, eligibilityBasis = "declarado pelo contador",
        }));
        var id = credit.GetProperty("id").GetGuid();
        await Json(await Post(admin, $"/api/tax/credits/{id}/movements", new { movement = "Appropriate", amount = 25m }));

        var ok = await Post(admin, $"/api/tax/credits/{id}/movements", new { movement = "Use", amount = 1m, obligationTax = "Cbs", obligationReference = "obr-dup" });
        await Json(ok);
        (await Post(admin, $"/api/tax/credits/{id}/movements", new { movement = "Use", amount = 1m, obligationTax = "Cbs", obligationReference = "obr-dup" }))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await Json(await Post(admin, $"/api/tax/credits/{id}/movements", new { movement = "Use", amount = 1m, obligationTax = "Ibs", obligationReference = "obr-ibs" }),
            HttpStatusCode.UnprocessableEntity)).GetProperty("code").GetString().Should().Be("tax_credit_cross_tax");

        var uses = await Task.WhenAll(Enumerable.Range(0, 5).Select(async i =>
        {
            var c = await AuthClientFactory.LoginAsAdminAsync(_factory);
            return await Post(c, $"/api/tax/credits/{id}/movements", new { movement = "Use", amount = 10m, obligationTax = "Cbs", obligationReference = $"obr-par-{i}" });
        }));
        uses.Count(u => u.StatusCode == HttpStatusCode.OK).Should().Be(2, "24 available pays only two uses of 10");

        var duplicate = await Post(admin, "/api/tax/credits", new
        {
            tax = "Cbs", sourceDocumentType = "NF-e", sourceDocumentKey = "35261012345678000190550010000012341000012345",
            supplierDocument = "12345678000190", acquiredOn = "2026-06-01", expiresOn = (string?)null,
            originalAmount = 100m, eligibleAmount = 100m, eligibilityBasis = "repetido",
        });
        duplicate.StatusCode.Should().Be(HttpStatusCode.Conflict, "the same document cannot give the same credit twice");

        var final = await Json(await admin.GetAsync($"/api/tax/credits/{id}"));
        final.GetProperty("balance").GetProperty("used").GetDecimal().Should().Be(21m);
        final.GetProperty("balance").GetProperty("available").GetDecimal().Should().Be(4m);
    }

    [Fact]
    public async Task Split_expectations_reconcile_with_recorded_settlements()
    {
        var admin = await AdminWithProfileAsync();
        (await Json(await admin.GetAsync("/api/tax/split/provider"))).GetProperty("configured").GetBoolean().Should().BeFalse();
        var calc = await Json(await Post(admin, "/api/tax/calculations", Operation(Unique("Split"), conditional: 0m)));
        var id = calc.GetProperty("id").GetGuid();

        var expectations = (await Json(await Post(admin, $"/api/tax/calculations/{id}/split-expectations", new { procedure = "Standard" }))).EnumerateArray().ToList();
        expectations.Should().HaveCount(2);
        var cbs = expectations.Single(e => e.GetProperty("tax").GetString() == "Cbs");
        cbs.GetProperty("expected").GetDecimal().Should().Be(7.56m);
        cbs.GetProperty("status").GetString().Should().Be("Pending");

        var cbsId = cbs.GetProperty("id").GetGuid();
        (await Json(await Post(admin, $"/api/tax/split/expectations/{cbsId}/settlements", new { actualAmount = 7.56m, externalReference = "psp-1", settledAt = DateTime.UtcNow })))
            .GetProperty("status").GetString().Should().Be("Matched");
        (await Json(await Post(admin, $"/api/tax/split/expectations/{cbsId}/settlements", new { actualAmount = 1m, externalReference = "psp-2", settledAt = DateTime.UtcNow })))
            .GetProperty("status").GetString().Should().Be("Excess");
        (await Post(admin, $"/api/tax/split/expectations/{cbsId}/settlements", new { actualAmount = 1m, externalReference = "psp-2", settledAt = DateTime.UtcNow }))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);

        // The wrong entry is reversed (append-only) and the reconciliation is right again.
        var listed = (await Json(await admin.GetAsync($"/api/tax/split/expectations?calculationId={id}"))).EnumerateArray()
            .Single(e => e.GetProperty("id").GetGuid() == cbsId);
        var wrong = listed.GetProperty("settlements").EnumerateArray().Single(s => s.GetProperty("externalReference").GetString() == "psp-2");
        var fixedUp = await Json(await Post(admin, $"/api/tax/split/settlements/{wrong.GetProperty("id").GetGuid()}/reverse", new { reason = "digitado em duplicidade" }));
        fixedUp.GetProperty("status").GetString().Should().Be("Matched");
        fixedUp.GetProperty("settlements").GetArrayLength().Should().Be(3, "the original and its reversal stay on record");
        (await Post(admin, $"/api/tax/split/settlements/{wrong.GetProperty("id").GetGuid()}/reverse", new { reason = "de novo" }))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    // ── isolation and authorization ─────────────────────────────────────────────

    [Fact]
    public async Task Another_tenant_never_sees_this_tenants_tax_data()
    {
        var admin = await AdminWithProfileAsync();
        var calc = await Json(await Post(admin, "/api/tax/calculations", Operation(Unique("Isolamento"), gross: 1003m)));
        var calcId = calc.GetProperty("id").GetGuid();
        var oppId = calc.GetProperty("opportunityIds")[0].GetGuid();
        var credit = await Json(await Post(admin, "/api/tax/credits", new
        {
            tax = "Ibs", sourceDocumentType = "NF-e", sourceDocumentKey = (string?)null, supplierDocument = (string?)null,
            acquiredOn = "2026-06-01", expiresOn = (string?)null, originalAmount = 10m, eligibleAmount = 10m, eligibilityBasis = "teste",
        }));

        var email = $"tax-{Guid.NewGuid():N}"[..16] + "@" + TestCredentials.TestDomain;
        var reg = await _factory.CreateApiClient().PostAsJsonAsync("/api/auth/register",
            new { name = "Outra Empresa", email, password = TestCredentials.AdminPassword });
        reg.StatusCode.Should().Be(HttpStatusCode.OK, await reg.Content.ReadAsStringAsync());
        using (var scope = _factory.Services.CreateScope())
        {
            // Stand-in for clicking the e-mail confirmation link.
            var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
            var user = await db.Users.IgnoreQueryFilters().SingleAsync(u => u.Login == email);
            user.Activate();
            await db.SaveChangesAsync();
        }
        var other = await AuthClientFactory.LoginAsync(_factory, email, TestCredentials.AdminPassword);

        (await other.GetAsync($"/api/tax/calculations/{calcId}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await Post(other, $"/api/tax/calculations/{calcId}/verify", new { })).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await other.GetAsync($"/api/tax/opportunities/{oppId}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await Post(other, $"/api/tax/opportunities/{oppId}/review", new { })).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await other.GetAsync($"/api/tax/credits/{credit.GetProperty("id").GetGuid()}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await Json(await other.GetAsync("/api/tax/calculations"))).GetArrayLength().Should().Be(0);
        (await Json(await other.GetAsync("/api/tax/opportunities"))).GetArrayLength().Should().Be(0);
        (await Json(await other.GetAsync("/api/tax/credits"))).GetArrayLength().Should().Be(0);
        (await Json(await other.GetAsync("/api/tax/profile"))).GetProperty("versions").GetArrayLength().Should().Be(0);

        var noProfile = await Post(other, "/api/tax/calculations", Operation(Unique("Sem perfil")));
        (await Json(noProfile, HttpStatusCode.UnprocessableEntity)).GetProperty("code").GetString().Should().Be("tax_profile_missing");
        (await other.GetAsync("/api/tax/rules")).StatusCode.Should().Be(HttpStatusCode.OK, "the catalogue is shared");
    }

    [Fact]
    public async Task Roles_and_the_platform_boundary_are_enforced()
    {
        var admin = await AdminWithProfileAsync();
        var vendedor = await UserAsync(admin, "vendedor");
        var gerente = await UserAsync(admin, "gerente");

        (await _factory.CreateApiClient().GetAsync("/api/tax/overview")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await vendedor.GetAsync("/api/tax/overview")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await Post(vendedor, "/api/tax/calculations", Operation(Unique("Vendedor")))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await gerente.GetAsync("/api/tax/overview")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Post(gerente, "/api/tax/profile/versions", new
        {
            validFrom = "2026-01-01", regime = "LucroReal", state = "SP", cityIbgeCode = "3550308", primaryCnae = "4711302",
            secondaryCnaes = Array.Empty<string>(), specialRegimes = Array.Empty<string>(), benefits = Array.Empty<string>(),
            accessoryObligations = "Unknown", accessoryObligationsEvidence = (string?)null, reason = "tentativa",
        })).StatusCode.Should().Be(HttpStatusCode.Forbidden, "a change of regime is Diretoria only");
        (await Post(admin, "/api/platform/tax/rules", new { key = "X.Y", name = "x", description = "x" }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden, "tenant users never edit the catalogue");
        (await admin.GetAsync("/api/platform/tax/rules")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Overview_reports_savings_and_credits_apart()
    {
        var admin = await AdminWithProfileAsync();
        await Json(await Post(admin, "/api/tax/calculations", Operation(Unique("Painel"), gross: 1004m)));

        var o = await Json(await admin.GetAsync("/api/tax/overview"));
        o.GetProperty("profileConfigured").GetBoolean().Should().BeTrue();
        o.GetProperty("potentialCalculatedTaxReductions").GetDecimal().Should().BeGreaterThan(0m);
        o.GetProperty("potentialSavings").GetDecimal().Should().Be(0m, "a calculated-tax reduction is never added to savings");
        o.GetProperty("potentialDeferrals").GetDecimal().Should().Be(0m, "no deferral detector exists — nothing is invented");
        o.GetProperty("activeRulesToday").GetInt32().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task OpenApi_document_describes_the_tax_api()
    {
        var doc = await Json(await _factory.CreateApiClient().GetAsync("/swagger/v1/swagger.json"));

        var paths = doc.GetProperty("paths").EnumerateObject().Select(p => p.Name).ToList();
        paths.Should().Contain(["/api/tax/calculations", "/api/tax/calculations/{id}/verify", "/api/tax/simulations",
            "/api/tax/opportunities/{id}/approve", "/api/tax/credits/{id}/movements", "/api/tax/rules", "/api/platform/tax/rules/{key}/versions",
            "/api/platform/tax/rule-versions/{id}/professional-review"]);
        var schemas = doc.GetProperty("components").GetProperty("schemas");
        schemas.TryGetProperty("RecordProfessionalReviewRequest", out _).Should().BeTrue();
        schemas.GetProperty("TaxImpactNature").GetProperty("enum").EnumerateArray().Select(e => e.GetString()).Should().Contain("CalculatedTaxReduction");
        doc.GetProperty("components").GetProperty("schemas").TryGetProperty("TaxCalculationDto", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Review_answers_and_credit_dates_are_never_guessed()
    {
        var admin = await AdminWithProfileAsync();
        var calc = await Json(await Post(admin, "/api/tax/calculations", Operation(Unique("Resposta"), gross: 1005m)));
        var oppId = calc.GetProperty("opportunityIds")[0].GetGuid();
        await Json(await Post(admin, $"/api/tax/opportunities/{oppId}/review", new { }));
        var requirement = (await Json(await admin.GetAsync($"/api/tax/opportunities/{oppId}"))).GetProperty("requirements").EnumerateArray()
            .Single(r => r.GetProperty("code").GetString() == "no-later-event");

        (await Post(admin, $"/api/tax/opportunities/{oppId}/requirements/{requirement.GetProperty("id").GetGuid()}", new { note = "sem resposta" }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest, "a missing answer is not a 'no' (which would block the opportunity for good)");
        (await Json(await admin.GetAsync($"/api/tax/opportunities/{oppId}"))).GetProperty("status").GetString().Should().Be("InReview");

        var credit = await Json(await Post(admin, "/api/tax/credits", new
        {
            tax = "Cbs", sourceDocumentType = "NF-e", sourceDocumentKey = "35261012345678000190550010000012341000012346",
            supplierDocument = (string?)null, acquiredOn = "2026-06-01", expiresOn = "2026-06-30",
            originalAmount = 10m, eligibleAmount = 10m, eligibilityBasis = "teste",
        }));
        var id = credit.GetProperty("id").GetGuid();
        await Json(await Post(admin, $"/api/tax/credits/{id}/movements", new { movement = "Appropriate", amount = 10m }));
        var future = await Post(admin, $"/api/tax/credits/{id}/movements",
            new { movement = "Use", amount = 1m, obligationTax = "Cbs", obligationReference = "obr-fut", onDate = "2099-01-01" });
        (await Json(future, HttpStatusCode.UnprocessableEntity)).GetProperty("code").GetString().Should().Be("tax_credit_future_date");
        var expired = await Json(await Post(admin, $"/api/tax/credits/{id}/movements", new { movement = "Expire" }));
        expired.GetProperty("balance").GetProperty("available").GetDecimal().Should().Be(0m, "expiry is dated by the server, after the term");
    }

    [Fact]
    public async Task A_confirmed_sale_is_calculated_from_its_facts_without_touching_it()
    {
        var admin = await AdminWithProfileAsync();
        var open = await admin.PostAsJsonAsync("/api/cash/sessions/open", new OpenCashSessionRequest(0m, "tax"));
        var session = open.StatusCode == HttpStatusCode.Created
            ? (await open.Content.ReadFromJsonAsync<CashSessionDto>())!
            : (await admin.GetFromJsonAsync<CashSessionDto>("/api/cash/sessions/open"))!;
        async Task<ProductDto> ProductAsync(decimal price)
        {
            var code = $"TAX-{Guid.NewGuid():N}"[..20];
            var r = await admin.PostAsJsonAsync("/api/products", new CreateProductRequest(Code: code, Name: code, Unit: "Un", SalePrice: price, TrackStock: false));
            r.StatusCode.Should().Be(HttpStatusCode.Created, await r.Content.ReadAsStringAsync());
            return (await r.Content.ReadFromJsonAsync<ProductDto>())!;
        }
        var a = await ProductAsync(100m);
        var b = await ProductAsync(50m);
        var draft = (await (await admin.PostAsJsonAsync("/api/sales", new CreateSaleRequest(CashSessionId: session.Id)))
            .Content.ReadFromJsonAsync<SaleDto>())!;
        (await admin.PostAsJsonAsync($"/api/sales/{draft.Id}/items", new AddSaleItemRequest(a.Id, 1, 100m, DiscountAmount: 5m))).EnsureSuccessStatusCode();
        (await admin.PostAsJsonAsync($"/api/sales/{draft.Id}/items", new AddSaleItemRequest(b.Id, 2, 50m))).EnsureSuccessStatusCode();

        var notYet = await Post(admin, $"/api/tax/calculations/from-sale/{draft.Id}", new { });
        (await Json(notYet, HttpStatusCode.UnprocessableEntity)).GetProperty("code").GetString().Should().Be("tax_sale_not_confirmed");

        var confirmed = await admin.PostAsJsonAsync($"/api/sales/{draft.Id}/confirm",
            new ConfirmSaleRequest([new PaymentInput("Pix", "Cash", 198m)], SurchargesAmount: 3m));
        confirmed.StatusCode.Should().Be(HttpStatusCode.OK, await confirmed.Content.ReadAsStringAsync());
        var before = await admin.GetStringAsync($"/api/sales/{draft.Id}");

        var calc = await Json(await Post(admin, $"/api/tax/calculations/from-sale/{draft.Id}", new { }));
        calc.GetProperty("source").GetString().Should().Be("Sale");
        calc.GetProperty("sourceId").GetGuid().Should().Be(draft.Id);
        // 100 − 5 + 1,46 (surcharge share) and 100 + 1,54: the sale's 3,00 split by net value, to the cent
        calc.GetProperty("totalBase").GetDecimal().Should().Be(198.00m);
        calc.GetProperty("totalTax").GetDecimal().Should().Be(1.98m);
        calc.GetProperty("output").GetProperty("warnings").EnumerateArray().Select(w => w.GetString())
            .Should().Contain(w => w!.Contains("não foram informados"), "the sale does not know the taxes inside the price");

        var again = await Json(await Post(admin, $"/api/tax/calculations/from-sale/{draft.Id}", new { }));
        again.GetProperty("reused").GetBoolean().Should().BeTrue();
        again.GetProperty("id").GetGuid().Should().Be(calc.GetProperty("id").GetGuid());
        (await admin.GetStringAsync($"/api/sales/{draft.Id}")).Should().Be(before, "Sales provides facts; the tax domain never writes to it");
    }

    [Fact]
    public async Task Rejected_and_manually_blocked_opportunities_are_closed_and_listed_by_status()
    {
        var admin = await AdminWithProfileAsync();
        var first = await Json(await Post(admin, "/api/tax/calculations", Operation(Unique("Rejeição"), gross: 1006m)));
        var rejectedId = first.GetProperty("opportunityIds")[0].GetGuid();
        var second = await Json(await Post(admin, "/api/tax/calculations", Operation(Unique("Bloqueio manual"), gross: 1007m)));
        var blockedId = second.GetProperty("opportunityIds")[0].GetGuid();

        (await Json(await Post(admin, $"/api/tax/opportunities/{rejectedId}/reject", new { reason = "Cliente não aceita mudar a condição" })))
            .GetProperty("status").GetString().Should().Be("Rejected");
        var approveRejected = await Post(admin, $"/api/tax/opportunities/{rejectedId}/approve", new { acknowledgeHighRisk = false });
        (await Json(approveRejected, HttpStatusCode.UnprocessableEntity)).GetProperty("code").GetString().Should().Be("tax_opportunity_state");
        (await Post(admin, $"/api/tax/opportunities/{rejectedId}/reject", new { reason = "" })).StatusCode
            .Should().Be(HttpStatusCode.BadRequest, "a rejection always says why");

        var blocked = await Json(await Post(admin, $"/api/tax/opportunities/{blockedId}/block",
            new { structure = "ArtificialTransaction", reason = "Operação montada só para o desconto" }));
        blocked.GetProperty("status").GetString().Should().Be("Blocked");
        blocked.GetProperty("blockedAs").GetString().Should().Be("ArtificialTransaction");
        blocked.GetProperty("estimatedImpact").ValueKind.Should().Be(JsonValueKind.Null);

        var rejectedList = (await Json(await admin.GetAsync("/api/tax/opportunities?status=Rejected&take=200"))).EnumerateArray().ToList();
        rejectedList.Should().Contain(o => o.GetProperty("id").GetGuid() == rejectedId);
        rejectedList.Should().OnlyContain(o => o.GetProperty("status").GetString() == "Rejected");
        var blockedRow = (await Json(await admin.GetAsync("/api/tax/opportunities?status=Blocked&take=200"))).EnumerateArray()
            .Single(o => o.GetProperty("id").GetGuid() == blockedId);
        blockedRow.GetProperty("estimatedImpact").ValueKind.Should().Be(JsonValueKind.Null, "the list never shows a blocked saving either");

        // Running the detectors again finds the same opportunity and creates nothing new.
        var redetect = await Json(await Post(admin, $"/api/tax/calculations/{first.GetProperty("id").GetGuid()}/opportunities/detect", new { }));
        redetect.GetArrayLength().Should().Be(0);
        var calcRow = (await Json(await admin.GetAsync("/api/tax/calculations?take=200"))).EnumerateArray()
            .Single(c => c.GetProperty("id").GetGuid() == first.GetProperty("id").GetGuid());
        calcRow.GetProperty("openOpportunities").GetInt32().Should().Be(0, "the only opportunity was rejected");
    }

    [Fact]
    public async Task The_decision_dossier_can_be_read_back_unchanged()
    {
        var admin = await AdminWithProfileAsync();
        var calc = await Json(await Post(admin, "/api/tax/calculations", Operation(Unique("Dossiê"), gross: 1008m)));
        var oppId = calc.GetProperty("opportunityIds")[0].GetGuid();
        await Json(await Post(admin, $"/api/tax/opportunities/{oppId}/review", new { }));
        foreach (var r in (await Json(await admin.GetAsync($"/api/tax/opportunities/{oppId}"))).GetProperty("requirements").EnumerateArray())
            await Json(await Post(admin, $"/api/tax/opportunities/{oppId}/requirements/{r.GetProperty("id").GetGuid()}", new { met = true }));
        var alone = await Post(admin, $"/api/tax/opportunities/{oppId}/approve", new { acknowledgeHighRisk = false });
        (await Json(alone, HttpStatusCode.UnprocessableEntity)).GetProperty("code").GetString()
            .Should().Be("tax_self_approval_requires_justification", "whoever analysed alone does not silently approve it");
        var approved = await Json(await Post(admin, $"/api/tax/opportunities/{oppId}/approve",
            new { acknowledgeHighRisk = false, selfApprovalJustification = "Empresa com uma só pessoa na Diretoria." }));
        var decision = approved.GetProperty("decision");
        decision.GetProperty("selfApprovalJustification").GetString().Should().Be("Empresa com uma só pessoa na Diretoria.");
        decision.GetProperty("dossier").GetProperty("segregationOfDuties").GetProperty("justification").GetString()
            .Should().Be("Empresa com uma só pessoa na Diretoria.");

        var read = await Json(await admin.GetAsync($"/api/tax/decisions/{decision.GetProperty("id").GetGuid()}"));
        read.GetProperty("dossierHash").GetString().Should().Be(decision.GetProperty("dossierHash").GetString());
        read.GetProperty("dossier").GetRawText().Should().Be(decision.GetProperty("dossier").GetRawText());
        (await admin.GetAsync($"/api/tax/decisions/{Guid.NewGuid()}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await Post(admin, $"/api/tax/opportunities/{oppId}/reject", new { reason = "tarde demais" })).StatusCode
            .Should().Be(HttpStatusCode.UnprocessableEntity, "an approved decision is final");
    }

    [Fact]
    public async Task Credit_blocks_unblocks_and_reversals_keep_the_balance_and_the_history()
    {
        var admin = await AdminWithProfileAsync();
        var credit = await Json(await Post(admin, "/api/tax/credits", new
        {
            tax = "Ibs", sourceDocumentType = "NF-e", sourceDocumentKey = "35261012345678000190550010000012341000099999",
            supplierDocument = (string?)null, acquiredOn = "2026-06-01", expiresOn = (string?)null,
            originalAmount = 60m, eligibleAmount = 50m, eligibilityBasis = "declarado",
        }));
        var id = credit.GetProperty("id").GetGuid();
        Task<HttpResponseMessage> Move(object body) => Post(admin, $"/api/tax/credits/{id}/movements", body);

        await Json(await Move(new { movement = "Appropriate", amount = 50m }));
        var blocked = await Json(await Move(new { movement = "Block", amount = 20m, reason = "em fiscalização" }));
        (blocked.GetProperty("balance").GetProperty("available").GetDecimal(), blocked.GetProperty("balance").GetProperty("blocked").GetDecimal())
            .Should().Be((30m, 20m));
        (await Move(new { movement = "Block", amount = 1m })).StatusCode.Should().Be(HttpStatusCode.BadRequest, "a block says why");
        (await Json(await Move(new { movement = "Unblock", amount = 25m }), HttpStatusCode.UnprocessableEntity))
            .GetProperty("code").GetString().Should().Be("tax_credit_amount");
        await Json(await Move(new { movement = "Unblock", amount = 20m }));
        var reversed = await Json(await Move(new { movement = "Reverse", amount = 10m, reason = "nota de compra cancelada" }));
        reversed.GetProperty("balance").GetProperty("available").GetDecimal().Should().Be(40m);
        reversed.GetProperty("balance").GetProperty("notAppropriated").GetDecimal().Should().Be(0m, "a reversed appropriation is not appropriable again");
        reversed.GetProperty("entries").EnumerateArray().Select(e => e.GetProperty("type").GetString())
            .Should().Equal("Recognized", "Appropriated", "Blocked", "Unblocked", "Reversed");

        var totals = (await Json(await admin.GetAsync("/api/tax/credits/totals"))).EnumerateArray().Single(t => t.GetProperty("tax").GetString() == "Ibs");
        totals.GetProperty("available").GetDecimal().Should().BeGreaterThanOrEqualTo(40m);
        (await admin.GetAsync($"/api/tax/credits/{Guid.NewGuid()}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Saved_simulations_are_listed_and_unknown_ids_are_not_found()
    {
        var admin = await AdminWithProfileAsync();
        var name = Unique("Lista");
        await Json(await Post(admin, "/api/tax/simulations", new
        {
            name, baseline = Operation(Unique("Base lista")),
            scenarios = new object[] { new { name = "Simples", regime = "SimplesNacional", convertConditionalDiscounts = false, assumeCreditable = false } },
        }));

        var row = (await Json(await admin.GetAsync("/api/tax/simulations?take=100"))).EnumerateArray().Single(s => s.GetProperty("name").GetString() == name);
        row.GetProperty("scenarios").GetInt32().Should().Be(2, "baseline + one scenario");
        (await admin.GetAsync($"/api/tax/simulations/{Guid.NewGuid()}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await admin.GetAsync($"/api/tax/calculations/{Guid.NewGuid()}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await Post(admin, $"/api/tax/calculations/{Guid.NewGuid()}/verify", new { })).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task The_profile_offers_the_fiscal_issuer_data_as_a_starting_point()
    {
        var admin = await AdminWithProfileAsync();
        var saved = await admin.PutAsJsonAsync("/api/fiscal/profile", new
        {
            legalName = "Orken Testes Ltda", tradeName = (string?)null, cnpj = "11222333000181", stateRegistration = "ISENTO",
            municipalRegistration = (string?)null, taxRegime = "LucroPresumido", street = "Rua A", number = "1", complement = (string?)null,
            district = "Centro", cityName = "São Paulo", cityIbgeCode = "3550308", state = "SP", zipCode = "01001000",
            operationNature = (string?)null, nfceSeries = (int?)null, nfceNextNumber = (long?)null, nfeSeries = (int?)null, nfeNextNumber = (long?)null,
        }, Web);
        saved.StatusCode.Should().Be(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());

        var suggestion = (await Json(await admin.GetAsync("/api/tax/profile"))).GetProperty("suggestion");
        (suggestion.GetProperty("regime").GetString(), suggestion.GetProperty("state").GetString(), suggestion.GetProperty("cityIbgeCode").GetString())
            .Should().Be(("LucroPresumido", "SP", "3550308"));
    }

    [Fact]
    public async Task Bad_input_is_refused_before_the_engine()
    {
        var admin = await AdminWithProfileAsync();
        var r = await Post(admin, "/api/tax/calculations", new
        {
            operationDate = "2026-06-15", direction = "Outbound", description = "x",
            lines = new[] { new { itemKind = "Good", description = "x", quantity = 1m, grossAmount = 10.001m,
                unconditionalDiscount = 20m, conditionalDiscount = 0m, surcharges = 0m } },
        });
        r.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── pre-merge hardening ─────────────────────────────────────────────────────

    /// <summary>A stand-in for a future engine: another version id; must never run a V1 calculation.</summary>
    private sealed class FakeEngineV2 : ITaxEngine
    {
        public const string VersionId = "orken-tax-engine/2.0.0-test";
        private static readonly TaxEngineV1 Inner = new();
        public static int Runs;
        public string Version => VersionId;
        public ITaxCanonicalizer Canonical => TaxCanonicalV1.Instance;
        public IReadOnlyList<TaxEngineRule> SelectActive(IEnumerable<TaxEngineRule> rules, DateOnly date) => Inner.SelectActive(rules, date);
        public string RuleSetHash(IEnumerable<TaxEngineRule> selected) => Inner.RuleSetHash(selected);
        public decimal RoundMoney(decimal value) => Inner.RoundMoney(value);

        public TaxEngineOutput Run(TaxEngineInput input, IReadOnlyCollection<TaxEngineRule> catalogue)
        {
            Interlocked.Increment(ref Runs);
            return Inner.Run(input, catalogue) with { EngineVersion = VersionId };
        }
    }

    private HttpClient HostWith(HttpClient authenticated, Action<IServiceCollection>? services = null, Action<IWebHostBuilder>? host = null)
    {
        var client = _factory.WithWebHostBuilder(b =>
        {
            host?.Invoke(b);
            if (services is not null) b.ConfigureTestServices(services);
        }).CreateClient();
        client.DefaultRequestHeaders.Authorization = authenticated.DefaultRequestHeaders.Authorization;
        return client;
    }

    private static Action<IServiceCollection> Engines(params ITaxEngine[] engines) => s =>
    {
        s.RemoveAll<TaxEngineRegistry>();
        s.AddSingleton(new TaxEngineRegistry(engines, FakeEngineV2.VersionId));
    };

    [Fact]
    public async Task A_V1_calculation_is_verified_by_V1_after_a_V2_becomes_current()
    {
        var admin = await AdminWithProfileAsync();
        var v1Calc = await Json(await Post(admin, "/api/tax/calculations", Operation(Unique("Antes da V2"), gross: 1011m)));
        var id = v1Calc.GetProperty("id").GetGuid();

        var withV2 = HostWith(admin, Engines(new TaxEngineV1(), new FakeEngineV2()));
        var runsBefore = FakeEngineV2.Runs;
        var verify = await Json(await Post(withV2, $"/api/tax/calculations/{id}/verify", new { }));

        verify.GetProperty("reproducible").GetBoolean().Should().BeTrue(verify.GetRawText());
        verify.GetProperty("engineAvailable").GetBoolean().Should().BeTrue();
        verify.GetProperty("engineVersionMatches").GetBoolean().Should().BeFalse("the current engine is V2");
        verify.GetProperty("storedEngineVersion").GetString().Should().Be("orken-tax-engine/1.0.0");
        verify.GetProperty("currentEngineVersion").GetString().Should().Be(FakeEngineV2.VersionId);
        FakeEngineV2.Runs.Should().Be(runsBefore, "V2 never re-runs a V1 calculation");

        var newCalc = await Json(await Post(withV2, "/api/tax/calculations", Operation(Unique("Depois da V2"), gross: 1012m)));
        newCalc.GetProperty("engineVersion").GetString().Should().Be(FakeEngineV2.VersionId, "new calculations use the current engine");
        (await Json(await admin.GetAsync($"/api/tax/calculations/{id}"))).GetProperty("engineVersion").GetString()
            .Should().Be("orken-tax-engine/1.0.0", "a stored calculation keeps the engine that made it");
    }

    [Fact]
    public async Task A_calculation_whose_engine_is_gone_is_not_reproduced_by_the_current_one()
    {
        var admin = await AdminWithProfileAsync();
        var v1Calc = await Json(await Post(admin, "/api/tax/calculations", Operation(Unique("Engine ausente"), gross: 1013m)));
        var id = v1Calc.GetProperty("id").GetGuid();

        var withoutV1 = HostWith(admin, Engines(new FakeEngineV2()));
        var runsBefore = FakeEngineV2.Runs;
        var verify = await Json(await Post(withoutV1, $"/api/tax/calculations/{id}/verify", new { }));

        verify.GetProperty("reproducible").GetBoolean().Should().BeFalse();
        verify.GetProperty("engineAvailable").GetBoolean().Should().BeFalse();
        verify.GetProperty("recomputedOutputHash").ValueKind.Should().Be(JsonValueKind.Null);
        verify.GetProperty("differences")[0].GetString().Should().Contain("não pode ser executada");
        FakeEngineV2.Runs.Should().Be(runsBefore);
        (await Json(await withoutV1.GetAsync($"/api/tax/calculations/{id}"), HttpStatusCode.UnprocessableEntity))
            .GetProperty("code").GetString().Should().Be("tax_engine_unavailable", "stored data is never read with another engine's canonical form");
    }

    [Fact]
    public async Task With_the_production_policy_rules_awaiting_professional_review_cannot_back_a_decision()
    {
        var admin = await AdminWithProfileAsync();
        var calc = await Json(await Post(admin, "/api/tax/calculations", Operation(Unique("Política"), gross: 1014m)));
        var oppId = calc.GetProperty("opportunityIds")[0].GetGuid();
        var gerente = await UserAsync(admin, "gerente");
        await Json(await Post(gerente, $"/api/tax/opportunities/{oppId}/review", new { }));
        foreach (var r in (await Json(await admin.GetAsync($"/api/tax/opportunities/{oppId}"))).GetProperty("requirements").EnumerateArray())
            await Json(await Post(gerente, $"/api/tax/opportunities/{oppId}/requirements/{r.GetProperty("id").GetGuid()}", new { met = true }));

        var production = HostWith(admin, host: b => b.UseSetting("TaxIntelligence:Policy:RequireApprovedRulesForDecisions", "true"));
        var refused = await Json(await Post(production, $"/api/tax/opportunities/{oppId}/approve", new { acknowledgeHighRisk = false }),
            HttpStatusCode.UnprocessableEntity);

        refused.GetProperty("code").GetString().Should().Be("tax_professional_review_pending");
        refused.GetProperty("error").GetString().Should().Contain("CBS.RATE.2026-TEST v1").And.Contain("não é aprovação profissional");
        (await Json(await admin.GetAsync($"/api/tax/opportunities/{oppId}"))).GetProperty("status").GetString()
            .Should().Be("InReview", "nothing was recorded");
        (await CountAsync(db => db.TaxDecisions.Where(d => d.OpportunityId == oppId))).Should().Be(0);
    }

    [Fact]
    public async Task A_professional_review_is_recorded_by_the_platform_only_and_audited()
    {
        var platform = await PlatformAsync();
        await Json(await Post(platform, "/api/platform/tax/legal-sources", new
        {
            code = "TEST-REVIEW-" + Guid.NewGuid().ToString("N")[..8], kind = "Other", title = "Fonte de teste", reference = "art. 1",
            url = "https://www.planalto.gov.br/teste", excerpt = "Texto de teste.", publishedOn = (string?)null, notes = (string?)null,
        }));
        var key = "TEST.REVIEW." + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        await Json(await Post(platform, "/api/platform/tax/rules", new { key, name = "Regra de teste", description = "Só para o teste." }));
        var sourceCode = (await Json(await platform.GetAsync("/api/platform/tax/legal-sources"))).EnumerateArray()
            .Select(s => s.GetProperty("code").GetString()!).Last(c => c.StartsWith("TEST-REVIEW-"));
        var version = await Json(await Post(platform, $"/api/platform/tax/rules/{key}/versions", new
        {
            validFrom = "2040-01-01", validTo = (string?)null, priority = 1, conditions = Array.Empty<object>(),
            result = new { kind = "note", code = "test", text = "nota" }, legalSourceCodes = new[] { sourceCode }, notes = (string?)null,
        }));
        var versionId = version.GetProperty("id").GetGuid();
        version.GetProperty("professionalReview").GetString().Should().Be("Pending");

        var admin = await AuthClientFactory.LoginAsAdminAsync(_factory);
        (await Post(admin, $"/api/platform/tax/rule-versions/{versionId}/professional-review", new { status = "Approved", reviewer = "x" }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden, "tenants never review the shared catalogue");
        (await Post(platform, $"/api/platform/tax/rule-versions/{versionId}/professional-review", new { status = "Rejected", reviewer = "Contadora" }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest, "a rejection must say why");

        var reviewed = await Json(await Post(platform, $"/api/platform/tax/rule-versions/{versionId}/professional-review",
            new { status = "ChangesRequired", reviewer = "Contadora de teste", notes = "Ajustar a vigência." }));
        reviewed.GetProperty("professionalReview").GetString().Should().Be("ChangesRequired");
        reviewed.GetProperty("professionalReviewedBy").GetString().Should().Be("Contadora de teste");
        reviewed.GetProperty("status").GetString().Should().Be("Draft", "a professional review never moves the technical status");
        (await CountAsync(db => db.AuditRecords.IgnoreQueryFilters()
            .Where(a => a.ActionType == "tax_rule_version_professional_review" && a.EntityId == versionId.ToString()))).Should().Be(1);
    }

    [Fact]
    public async Task Declared_compliance_is_not_treated_as_verified_compliance()
    {
        var admin = await AdminWithProfileAsync();
        async Task AddVersion(string from, string status, string? evidence) => await Json(await Post(admin, "/api/tax/profile/versions", new
        {
            validFrom = from, regime = "LucroPresumido", state = "SP", cityIbgeCode = "3550308", primaryCnae = "4711302",
            secondaryCnaes = Array.Empty<string>(), specialRegimes = Array.Empty<string>(), benefits = Array.Empty<string>(),
            accessoryObligations = status, accessoryObligationsEvidence = evidence, reason = "Teste de obrigações acessórias",
        }));

        (await Post(admin, "/api/tax/profile/versions", new
        {
            validFrom = "2026-11-01", regime = "LucroPresumido", state = "SP", cityIbgeCode = "3550308", primaryCnae = "4711302",
            secondaryCnaes = Array.Empty<string>(), specialRegimes = Array.Empty<string>(), benefits = Array.Empty<string>(),
            accessoryObligations = "VerifiedCompliant", accessoryObligationsEvidence = (string?)null, reason = "sem evidência",
        })).StatusCode.Should().Be(HttpStatusCode.BadRequest, "verified compliance needs the evidence that was checked");

        await AddVersion("2026-11-01", "DeclaredCompliant", null);
        await AddVersion("2026-12-01", "VerifiedCompliant", "Recibos de entrega conferidos pela contabilidade");
        await AddVersion("2026-12-20", "Unknown", null);

        static IEnumerable<string?> Notes(JsonElement c) => c.GetProperty("output").GetProperty("notes").EnumerateArray().Select(n => n.GetProperty("code").GetString());

        var declared = await Json(await Post(admin, "/api/tax/calculations", Operation(Unique("Declarado"), gross: 1015m, date: "2026-11-15")));
        Notes(declared).Should().NotContain(TaxNoteCodes.Waiver2026, "a declaration does not satisfy a rule that needs verified compliance");
        declared.GetProperty("input").GetProperty("profile").GetProperty("accessoryObligationsCompliant").ValueKind.Should().Be(JsonValueKind.Null);
        declared.GetProperty("profileAdvisories")[0].GetString().Should().Contain("não verificadas");

        var verified = await Json(await Post(admin, "/api/tax/calculations", Operation(Unique("Verificado"), gross: 1016m, date: "2026-12-10")));
        Notes(verified).Should().Contain(TaxNoteCodes.Waiver2026);
        verified.GetProperty("profileAdvisories").GetArrayLength().Should().Be(0);
    }
}
