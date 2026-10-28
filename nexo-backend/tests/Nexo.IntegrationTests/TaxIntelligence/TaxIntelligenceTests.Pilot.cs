using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nexo.Infrastructure.Persistence;
using Nexo.IntegrationTests.Common;
using Nexo.IntegrationTests.Helpers;

namespace Nexo.IntegrationTests.TaxIntelligence;

/// <summary>
/// The controlled pilot: the 2026 versions carry an INTERNAL PRODUCT / GOVERNANCE APPROVAL FOR
/// CONTROLLED PILOT while their professional review stays Pending. The pilot runs and decides with the
/// internal approval; the definitive mode still needs a professional one; anything outside the recorte
/// or awaiting regulation stays blocked; and no record pretends a professional opinion exists.
/// </summary>
public partial class TaxIntelligenceTests
{
    /// <summary>The nine versions active in 2026, approved internally for the pilot by the migration.</summary>
    private static readonly string[] PilotVersions =
    [
        "CBS.RATE.2026-TEST v1", "IBS.RATE.2026-TEST-STATE v1", "IBSCBS.BASE.CONDITIONAL-DISCOUNT v1", "IBSCBS.BASE.SURCHARGES v1",
        "IBSCBS.BASE.TRANSITION-TAXES v1", "IBSCBS.BASE.UNCONDITIONAL-DISCOUNT v1", "IBSCBS.NOTE.2026-COMPENSATION v1",
        "IBSCBS.NOTE.2026-SIMPLES v1", "IBSCBS.NOTE.2026-WAIVER v2",
    ];

    private HttpClient Enforced(HttpClient c, string mode) => HostWith(c, host: b => b
        .UseSetting("TaxIntelligence:Policy:Mode", mode)
        .UseSetting("TaxIntelligence:Policy:RequireApprovedRulesForDecisions", "true")
        .UseSetting("TaxIntelligence:Policy:RequireApprovedRulesForCalculations", "true"));

    private HttpClient Pilot(HttpClient c) => Enforced(c, "ControlledPilot");

    /// <summary>Audit metadata (jsonb) of an action, read as text and filtered in memory.</summary>
    private async Task<List<string>> AuditMetadataAsync(string action, string? entityId = null)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        var q = db.AuditRecords.IgnoreQueryFilters().Where(a => a.ActionType == action);
        if (entityId is not null) q = q.Where(a => a.EntityId == entityId);
        return (await q.Select(a => a.MetadataJson).ToListAsync()).Select(m => m ?? string.Empty).ToList();
    }

    private static string Label(JsonElement rule, JsonElement version) => $"{rule.GetProperty("key").GetString()} v{version.GetProperty("version").GetInt32()}";

    [Fact]
    public async Task The_2026_catalogue_is_approved_for_the_controlled_pilot_while_its_professional_review_stays_pending()
    {
        var admin = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var versions = (await Json(await admin.GetAsync("/api/tax/rules"))).EnumerateArray()
            .SelectMany(r => r.GetProperty("versions").EnumerateArray().Select(v => (Label: Label(r, v), V: v)))
            .Where(x => x.V.GetProperty("origin").GetString()!.StartsWith("seed:"))
            .ToDictionary(x => x.Label, x => x.V);

        foreach (var label in PilotVersions)
        {
            var approval = versions[label].GetProperty("internalApproval");
            approval.GetProperty("status").GetString().Should().Be("ApprovedForControlledPilot", label);
            approval.GetProperty("policy").GetString().Should().Be("orken-pilot-approval/1");
            approval.GetProperty("approvedBy").GetString().Should().StartWith("Product Owner — Andrade Systems");
            approval.GetProperty("validFrom").GetString().Should().Be("2026-01-01");
            approval.GetProperty("validTo").GetString().Should().Be("2026-12-31", "the approval covers the 2026 recorte only");
            approval.GetProperty("caveats").GetString().Should().NotBeNullOrWhiteSpace("caveats never disappear because of the approval");
            approval.GetProperty("scope").GetString().Should().Contain("não constitui parecer");
            approval.GetProperty("decisionReference").GetString().Should().Be("docs/tax-intelligence/homologation/internal-pilot-approval-2026.md");
            approval.GetProperty("nature").GetString().Should().Be("INTERNAL PRODUCT / GOVERNANCE APPROVAL FOR CONTROLLED PILOT");
        }
        foreach (var label in new[] { "IBSCBS.NOTE.2026-WAIVER v1", "CBS.RATE.REFERENCE-2027-2033 v1", "IBS.RATE.2027-2028-STATE v1", "IBS.RATE.2027-2028-MUNICIPAL v1" })
            versions[label].GetProperty("internalApproval").GetProperty("status").GetString().Should().Be("NotApproved", $"{label} stays blocked");
        versions["CBS.RATE.REFERENCE-2027-2033 v1"].GetProperty("status").GetString().Should().Be("PendingRegulation");
        versions["IBS.RATE.2027-2028-STATE v1"].GetProperty("status").GetString().Should().Be("Unverified");

        // No seeded version claims a professional review: no reviewer, no registration, no approval.
        versions.Values.Should().OnlyContain(v => v.GetProperty("professionalReview").GetString() == "Pending");
        versions.Values.Should().OnlyContain(v => v.GetProperty("professionalReviewedBy").ValueKind == JsonValueKind.Null);
        versions.Values.Should().OnlyContain(v => v.GetProperty("professionalReference").ValueKind == JsonValueKind.Null);
    }

    [Fact]
    public async Task In_the_controlled_pilot_an_approved_rule_runs_and_the_calculation_records_its_governance()
    {
        var admin = await AdminWithProfileAsync();
        var calc = await Json(await Post(Pilot(admin), "/api/tax/calculations", Operation(Unique("Piloto"), gross: 1021m)));

        calc.GetProperty("totalTax").GetDecimal().Should().BeGreaterThan(0m, "the approved 2026 rules ran");
        var governance = calc.GetProperty("governance");
        governance.GetProperty("mode").GetString().Should().Be("ControlledPilot");
        governance.GetProperty("policy").GetString().Should().Be("orken-pilot-approval/1");
        governance.GetProperty("enforced").GetBoolean().Should().BeTrue();
        governance.GetProperty("nature").GetString().Should().Be("INTERNAL PRODUCT / GOVERNANCE APPROVAL FOR CONTROLLED PILOT");
        governance.GetProperty("notice").GetString().Should().Contain("Não constitui parecer jurídico, contábil ou tributário profissional");
        governance.GetProperty("notApproved").GetArrayLength().Should().Be(0);
        var rules = governance.GetProperty("rules").EnumerateArray().ToList();
        rules.Should().NotBeEmpty().And.OnlyContain(r => r.GetProperty("approvedForPilot").GetBoolean());
        rules.Should().OnlyContain(r => r.GetProperty("professionalReview").GetString() == "Pending", "the professional review stays visible as pending");
        rules.Should().OnlyContain(r => !string.IsNullOrWhiteSpace(r.GetProperty("pilotCaveats").GetString()));
        governance.GetProperty("professionalReviewPending").EnumerateArray().Select(x => x.GetString()).Should().Contain("CBS.RATE.2026-TEST v1");

        var id = calc.GetProperty("id").GetGuid().ToString();
        (await AuditMetadataAsync("tax_calculation_created", id)).Should().ContainSingle()
            .Which.Should().Contain("ControlledPilot").And.Contain("orken-pilot-approval/1").And.Contain("Pending", "every pilot execution is audited as such");
        (await Json(await admin.GetAsync($"/api/tax/calculations/{id}"))).GetProperty("governance").GetProperty("mode").GetString()
            .Should().Be("ControlledPilot", "the mode is recorded with the calculation, not re-derived");
    }

    [Fact]
    public async Task In_the_controlled_pilot_an_active_rule_without_internal_approval_is_refused_until_approved()
    {
        var admin = await AdminWithProfileAsync();
        var platform = await PlatformAsync();
        var tag = Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();
        const string day = "2026-04-07";
        var source = $"TEST-PILOT-{tag}";
        await Json(await Post(platform, "/api/platform/tax/legal-sources", new
        {
            code = source, kind = "Other", title = "Fonte de teste", reference = "art. 1", url = "https://www.planalto.gov.br/teste",
            excerpt = "Texto literal de teste usado apenas pela suíte de integração.", publishedOn = (string?)null, notes = (string?)null,
        }));
        await Json(await Post(platform, $"/api/platform/tax/legal-sources/{source}/verify", new { verifiedBy = "suite" }));
        var key = $"TEST.PILOT.{tag}";
        await Json(await Post(platform, "/api/platform/tax/rules", new { key, name = "Nota de teste", description = "Só para testes." }));
        var versionId = (await Json(await Post(platform, $"/api/platform/tax/rules/{key}/versions", new
        {
            validFrom = day, validTo = day, priority = 5, conditions = Array.Empty<object>(),
            result = new { kind = "note", code = "TEST-PILOT", text = "Nota de teste." }, legalSourceCodes = new[] { source }, notes = (string?)null,
        }))).GetProperty("id").GetGuid();
        await Json(await Post(platform, $"/api/platform/tax/rule-versions/{versionId}/status", new { status = "Active" }));

        var pilot = Pilot(admin);
        var op = Operation(Unique("Regra sem aprovação"), gross: 1022m, date: day);
        var refused = await Json(await Post(pilot, "/api/tax/calculations", op), HttpStatusCode.UnprocessableEntity);
        refused.GetProperty("code").GetString().Should().Be("tax_pilot_approval_missing", "technical activation is no approval");
        refused.GetProperty("details").EnumerateArray().Select(d => d.GetString()).Should().Equal($"{key} v1");
        (await AuditMetadataAsync("tax_calculation_refused")).Count(m => m.Contains(key)).Should().Be(1);

        (await Post(admin, $"/api/platform/tax/rule-versions/{versionId}/pilot-approval", new
        {
            approvedBy = "x", scope = "x", validFrom = day, validTo = day, caveats = "x", decisionReference = "x",
        })).StatusCode.Should().Be(HttpStatusCode.Forbidden, "tenants never approve the shared catalogue");
        (await Post(platform, $"/api/platform/tax/rule-versions/{versionId}/pilot-approval", new
        {
            approvedBy = "Product Owner — teste", scope = "Teste", validFrom = day, validTo = day, caveats = "", decisionReference = "TEST-DECISION",
        })).StatusCode.Should().Be(HttpStatusCode.BadRequest, "the caveats are part of the approval");

        var approved = await Json(await Post(platform, $"/api/platform/tax/rule-versions/{versionId}/pilot-approval", new
        {
            approvedBy = "Product Owner — teste", scope = "Somente o teste de integração", validFrom = day, validTo = day,
            caveats = "Ressalva de teste.", decisionReference = "TEST-DECISION",
        }));
        approved.GetProperty("internalApproval").GetProperty("status").GetString().Should().Be("ApprovedForControlledPilot");
        approved.GetProperty("professionalReview").GetString().Should().Be("Pending", "the internal approval never touches the professional review");
        approved.GetProperty("professionalReviewedBy").ValueKind.Should().Be(JsonValueKind.Null);

        var calc = await Json(await Post(pilot, "/api/tax/calculations", op));
        calc.GetProperty("governance").GetProperty("rules").EnumerateArray()
            .Single(r => r.GetProperty("key").GetString() == key).GetProperty("approvedForPilot").GetBoolean().Should().BeTrue();

        // Withdrawn (e.g. a suspension criterion): the pilot stops running it, fail closed.
        var withdrawn = await Json(await Post(platform, $"/api/platform/tax/rule-versions/{versionId}/pilot-approval/withdraw",
            new { reason = "Critério de suspensão do teste" }));
        withdrawn.GetProperty("internalApproval").GetProperty("status").GetString().Should().Be("Withdrawn");
        (await Json(await Post(pilot, "/api/tax/calculations", Operation(Unique("Após retirada"), gross: 1023m, date: day)),
            HttpStatusCode.UnprocessableEntity)).GetProperty("code").GetString().Should().Be("tax_pilot_approval_missing");
        (await CountAsync(db => db.AuditRecords.IgnoreQueryFilters()
            .Where(a => (a.ActionType == "tax_rule_version_pilot_approval" || a.ActionType == "tax_rule_version_pilot_approval_withdrawn")
                        && a.EntityId == versionId.ToString()))).Should().Be(2);
    }

    [Fact]
    public async Task The_pilot_never_reaches_regulation_pending_or_out_of_scope_operations()
    {
        var platform = await PlatformAsync();
        foreach (var (id, what) in new[]
                 {
                     ("7a9c3e00-0000-4000-8000-000000000010", "PendingRegulation"),
                     ("7a9c3e00-0000-4000-8000-000000000011", "Unverified"),
                     ("7a9c3e00-0000-4000-8000-000000000009", "Superseded"),
                 })
            (await Json(await Post(platform, $"/api/platform/tax/rule-versions/{id}/pilot-approval", new
            {
                approvedBy = "Product Owner — teste", scope = "Teste", validFrom = "2027-01-01", validTo = "2027-12-31",
                caveats = "Ressalva.", decisionReference = "TEST",
            }), HttpStatusCode.UnprocessableEntity)).GetProperty("code").GetString().Should().Be("tax_pilot_approval_status", $"a {what} version stays blocked");

        var pilot = Pilot(await AdminWithProfileAsync());
        var pending = await Json(await Post(pilot, "/api/tax/calculations", Operation(Unique("2027"), gross: 1024m, date: "2027-03-01")),
            HttpStatusCode.UnprocessableEntity);
        pending.GetProperty("code").GetString().Should().Be("tax_operation_not_supported");
        pending.GetProperty("error").GetString().Should().Contain("PendingRegulation", "regulation-pending periods are never executed");
        var unsupported = await Json(await Post(pilot, "/api/tax/calculations", Operation(Unique("Contínuo"), gross: 1025m, supplyPattern: "ContinuedSupply")),
            HttpStatusCode.UnprocessableEntity);
        unsupported.GetProperty("code").GetString().Should().Be("tax_operation_not_supported", "outside the recorte stays fail closed");
        var unclassified = await Json(await Post(pilot, "/api/tax/calculations", Operation(Unique("Sem cClassTrib"), gross: 1026m, cClassTrib: null)),
            HttpStatusCode.UnprocessableEntity);
        unclassified.GetProperty("error").GetString().Should().Contain("RequiresClassification");
    }

    [Fact]
    public async Task A_pilot_decision_keeps_both_approvals_and_every_caveat_in_the_dossier()
    {
        var admin = await AdminWithProfileAsync();
        var pilotAdmin = Pilot(admin);
        var calc = await Json(await Post(pilotAdmin, "/api/tax/calculations", Operation(Unique("Decisão piloto"), gross: 1027m)));
        var oppId = calc.GetProperty("opportunityIds")[0].GetGuid();
        var gerente = await UserAsync(admin, "gerente");
        await Json(await Post(gerente, $"/api/tax/opportunities/{oppId}/review", new { }));
        await PrepareScenarioAsync(gerente, oppId, "PROP-PILOTO");

        var approved = await Json(await Post(pilotAdmin, $"/api/tax/opportunities/{oppId}/approve", new { acknowledgeHighRisk = false }));
        approved.GetProperty("status").GetString().Should().Be("Approved", "in the controlled pilot the internal approval backs the decision");
        approved.GetProperty("professionalReviewPending").GetBoolean().Should().BeTrue("and the pending professional review is still shown");

        var dossier = approved.GetProperty("decision").GetProperty("dossier");
        dossier.GetProperty("nature").GetString().Should().Be("Registro auditável da decisão e das premissas utilizadas pelo motor. Não é prova definitiva de conformidade fiscal.");
        var governance = dossier.GetProperty("governance");
        governance.GetProperty("mode").GetString().Should().Be("ControlledPilot");
        governance.GetProperty("decisionRecord").GetString().Should().Be("docs/tax-intelligence/homologation/internal-pilot-approval-2026.md");
        governance.GetProperty("rules").EnumerateArray().Should().NotBeEmpty()
            .And.OnlyContain(r => r.GetProperty("internalApproval").GetString() == "ApprovedForControlledPilot"
                                  && r.GetProperty("professionalReview").GetString() == "Pending"
                                  && !string.IsNullOrWhiteSpace(r.GetProperty("pilotCaveats").GetString()));
        dossier.GetProperty("professionalReview").GetProperty("requiredByPolicy").GetBoolean().Should().BeFalse();
        dossier.GetProperty("opportunity").GetProperty("caveats").GetString().Should().NotBeNullOrWhiteSpace("the detector's caveats stay");
        dossier.GetProperty("pendingIssues").EnumerateArray().Select(p => p.GetString()).Should().Contain(p => p!.StartsWith("Revisão tributária pendente"));
        dossier.GetProperty("impactAnalysis").GetProperty("financialEffect").GetString().Should().StartWith("Não determinado");

        var text = dossier.GetRawText();
        foreach (var claim in new[] { "parecer profissional aprovado", "Professionally Approved", "OAB n", "CRC n", "economia garantida", "tax saving garantido" })
            text.Should().NotContainEquivalentOf(claim, "no record may suggest a professional opinion, a registration or a guaranteed saving");
        System.Text.RegularExpressions.Regex.IsMatch(text, @"(?<!não )economia definitiva", System.Text.RegularExpressions.RegexOptions.IgnoreCase)
            .Should().BeFalse();
    }

    [Fact]
    public async Task The_controlled_pilot_and_the_definitive_professional_mode_stay_different()
    {
        var admin = await AdminWithProfileAsync();
        var pilotCalc = await Json(await Post(Pilot(admin), "/api/tax/calculations", Operation(Unique("Modo piloto"), gross: 1028m)));
        pilotCalc.GetProperty("governance").GetProperty("mode").GetString().Should().Be("ControlledPilot");

        var definitive = Enforced(admin, "ProfessionalFinal");
        var refused = await Json(await Post(definitive, "/api/tax/calculations", Operation(Unique("Modo definitivo"), gross: 1029m)),
            HttpStatusCode.UnprocessableEntity);
        refused.GetProperty("code").GetString().Should().Be("tax_professional_review_pending",
            "the internal pilot approval is never a professional approval");
        refused.GetProperty("details").EnumerateArray().Select(d => d.GetString()).Should().Contain(PilotVersions.Where(v => v.StartsWith("CBS.RATE")));

        // A stored pilot calculation keeps the mode it ran under, whatever the host's mode today.
        (await Json(await definitive.GetAsync($"/api/tax/calculations/{pilotCalc.GetProperty("id").GetGuid()}")))
            .GetProperty("governance").GetProperty("mode").GetString().Should().Be("ControlledPilot");
    }
}
