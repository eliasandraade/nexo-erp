using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nexo.Application.Features.Fiscal;
using Nexo.Infrastructure.Persistence;
using Nexo.IntegrationTests.Common;
using Nexo.IntegrationTests.Helpers;
using static Nexo.IntegrationTests.Helpers.TaxPilotSetup;

namespace Nexo.IntegrationTests.TaxIntelligence;

/// <summary>
/// The controlled pilot fails closed. Nothing is approved by a migration; with nothing configured the module
/// runs nothing; the pilot needs the kill switch on, the mode configured explicitly, the tenant/store inside a
/// platform-kept scope and every rule technically active and approved — each approval by an explicit, audited
/// platform action that records who did it and the exact decision reference, frozen into each dossier. The
/// professional review stays Pending and visible; the definitive mode ignores the internal approval; the
/// pilot is a shadow mode with no external fiscal effect; and what was recorded stays readable and reproducible.
/// </summary>
public partial class TaxIntelligenceTests
{
    private HttpClient Pilot(HttpClient c) => HostWith(c, host: Enforced("ControlledPilot"));

    /// <summary>Audit metadata (jsonb) of an action, read as text and filtered in memory.</summary>
    private async Task<List<string>> AuditMetadataAsync(string action, string? entityId = null)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        var q = db.AuditRecords.IgnoreQueryFilters().Where(a => a.ActionType == action);
        if (entityId is not null) q = q.Where(a => a.EntityId == entityId);
        return (await q.Select(a => a.MetadataJson).ToListAsync()).Select(m => m ?? string.Empty).ToList();
    }

    private static string Code(JsonElement error) => error.GetProperty("code").GetString()!;

    private async Task<Guid> PreparedOpportunityAsync(HttpClient admin, JsonElement calc, string reference)
    {
        var oppId = calc.GetProperty("opportunityIds")[0].GetGuid();
        var gerente = await UserAsync(admin, "gerente");
        await Json(await Post(gerente, $"/api/tax/opportunities/{oppId}/review", new { }));
        await PrepareScenarioAsync(gerente, oppId, reference);
        return oppId;
    }

    [Fact]
    public async Task Nothing_is_approved_for_the_pilot_except_by_an_explicit_audited_platform_action()
    {
        var platform = await PlatformAsync();
        var versions = await VersionsAsync(platform);
        var approvalAudits = await AuditMetadataAsync("tax_rule_version_pilot_approval");
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
            var audited = await db.AuditRecords.IgnoreQueryFilters().Where(a => a.ActionType == "tax_rule_version_pilot_approval")
                .Select(a => a.EntityId).Distinct().ToListAsync();
            // Whatever the order the suite ran in: any version not NotApproved got there by an audited platform action.
            foreach (var (label, v) in versions.Where(x => x.Value.GetProperty("internalApproval").GetProperty("status").GetString() != "NotApproved"))
                audited.Should().Contain(v.GetProperty("id").GetGuid().ToString(), $"{label} was approved only by an explicit action");
        }
        // No seeded version claims a professional review: no reviewer, no registration, no approval.
        var seeded = versions.Values.Where(v => v.GetProperty("origin").GetString()!.StartsWith("seed:")).ToList();
        seeded.Should().HaveCount(13);
        seeded.Should().OnlyContain(v => v.GetProperty("professionalReview").GetString() == "Pending"
                                         && v.GetProperty("professionalReviewedBy").ValueKind == JsonValueKind.Null
                                         && v.GetProperty("professionalReference").ValueKind == JsonValueKind.Null);

        // The actor is the authenticated platform user and the server clock — a name in the request is ignored.
        var cbs = versions["CBS.RATE.2026-TEST v1"].GetProperty("id").GetGuid();
        var approved = await Json(await Post(platform, $"/api/platform/tax/rule-versions/{cbs}/pilot-approval", new
        {
            approvedBy = "Nome Inventado", scope = "Recorte 2026", validFrom = "2026-01-01", validTo = "2026-12-31",
            caveats = "Ressalva.", decisionReference = DecisionRecord,
        }));
        var approval = approved.GetProperty("internalApproval");
        approval.GetProperty("approvedBy").GetString().Should().Be(TestCredentials.PlatformEmail, "the authenticated operator, never a typed name");
        approval.GetProperty("approvedById").GetGuid().Should().NotBeEmpty();
        approval.GetProperty("approvedByRole").GetString().Should().NotBeNullOrWhiteSpace();
        approval.GetProperty("approvedAt").GetDateTime().Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(2));
        approved.GetProperty("professionalReview").GetString().Should().Be("Pending");
        (await AuditMetadataAsync("tax_rule_version_pilot_approval", cbs.ToString())).Last()
            .Should().Contain(approval.GetProperty("approvedById").GetGuid().ToString()).And.Contain(DecisionRecord);
        approvalAudits.Count.Should().BeLessThan((await AuditMetadataAsync("tax_rule_version_pilot_approval")).Count);

        var admin = await AuthClientFactory.LoginAsAdminAsync(_factory);
        (await Post(admin, $"/api/platform/tax/rule-versions/{cbs}/pilot-approval", new
        {
            scope = "x", validFrom = "2026-01-01", validTo = "2026-12-31", caveats = "x", decisionReference = "x",
        })).StatusCode.Should().Be(HttpStatusCode.Forbidden, "tenants never approve the shared catalogue");
        (await Post(admin, "/api/platform/tax/pilot/participants", new { tenantId = Identity(admin).Tenant }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden, "tenants never admit themselves to the pilot");
    }

    [Fact]
    public async Task The_kill_switch_stops_every_new_run_and_keeps_history_readable_and_reproducible()
    {
        var admin = await AdminWithProfileAsync();
        var calc = await Json(await Post(admin, "/api/tax/calculations", Operation(Unique("Antes do desligamento"), gross: 1031m)));
        var id = calc.GetProperty("id").GetGuid();
        var oppId = calc.GetProperty("opportunityIds")[0].GetGuid();

        var off = HostWith(admin, host: b => b.UseSetting("TaxIntelligence:Policy:Enabled", "false"));
        Code(await Json(await Post(off, "/api/tax/calculations", Operation(Unique("Desligado"), gross: 1032m)), HttpStatusCode.UnprocessableEntity))
            .Should().Be("tax_intelligence_disabled");
        Code(await Json(await Post(off, "/api/tax/simulations", new
        {
            name = Unique("Sim"), baseline = Operation(Unique("Base"), gross: 1033m),
            scenarios = new object[] { new { name = "x", convertConditionalDiscounts = true, assumeCreditable = false } },
        }), HttpStatusCode.UnprocessableEntity)).Should().Be("tax_intelligence_disabled");
        Code(await Json(await Post(off, $"/api/tax/opportunities/{oppId}/approve", new { acknowledgeHighRisk = false }), HttpStatusCode.UnprocessableEntity))
            .Should().Be("tax_intelligence_disabled");
        Code(await Json(await Post(off, $"/api/tax/calculations/{id}/opportunities/detect", new { }), HttpStatusCode.UnprocessableEntity))
            .Should().Be("tax_intelligence_disabled");

        (await Json(await off.GetAsync($"/api/tax/calculations/{id}"))).GetProperty("id").GetGuid().Should().Be(id);
        (await Json(await Post(off, $"/api/tax/calculations/{id}/verify", new { }))).GetProperty("reproducible").GetBoolean()
            .Should().BeTrue("a suspension never makes history unreadable");
        var status = await Json(await off.GetAsync("/api/tax/governance"));
        status.GetProperty("enabled").GetBoolean().Should().BeFalse();
        status.GetProperty("admitted").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task Without_an_explicitly_configured_mode_nothing_runs_where_approval_is_enforced()
    {
        var admin = await AdminWithProfileAsync();
        var unconfigured = HostWith(admin, host: b => b
            .UseSetting("TaxIntelligence:Policy:RequireApprovedRulesForDecisions", "true")
            .UseSetting("TaxIntelligence:Policy:RequireApprovedRulesForCalculations", "true"));
        Code(await Json(await Post(unconfigured, "/api/tax/calculations", Operation(Unique("Sem modo"), gross: 1034m)), HttpStatusCode.UnprocessableEntity))
            .Should().Be("tax_approval_mode_disabled", "the controlled pilot is never a fallback");
        (await Json(await unconfigured.GetAsync("/api/tax/governance"))).GetProperty("mode").GetString().Should().Be("Disabled");
    }

    [Fact]
    public async Task The_pilot_admits_only_an_open_scope_and_an_allowlisted_tenant()
    {
        var platform = await PlatformAsync();
        await ApproveCatalogAsync(platform);
        await OpenPilotAsync(platform);
        var outsider = await AdminOfNewCompanyAsync();
        var pilot = Pilot(outsider);

        var refused = await Json(await Post(pilot, "/api/tax/calculations", Operation(Unique("Fora do piloto"), gross: 1035m)), HttpStatusCode.UnprocessableEntity);
        Code(refused).Should().Be("tax_pilot_scope_tenant", "no tenant takes part by default");
        (await AuditMetadataAsync("tax_calculation_refused")).Should().Contain(m => m.Contains("tax_pilot_scope_tenant"));
        (await Json(await pilot.GetAsync("/api/tax/governance"))).GetProperty("refusalCode").GetString().Should().Be("tax_pilot_scope_tenant");

        var (tenant, _) = Identity(outsider);
        (await Post(platform, "/api/platform/tax/pilot/participants", new { tenantId = tenant, storeId = Guid.NewGuid() }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound, "a store allowlist names a store of that tenant");
        await AdmitAsync(platform, tenant);
        var calc = await Json(await Post(pilot, "/api/tax/calculations", Operation(Unique("Dentro do piloto"), gross: 1036m)));
        calc.GetProperty("governance").GetProperty("pilotScope").GetProperty("decisionReference").GetString().Should().Be(DecisionRecord);
        calc.GetProperty("governance").GetProperty("effectsNotice").GetString().Should().Contain("Não executa alteração fiscal, contábil ou financeira automaticamente");

        // Expired window → closed. Reopened → admitted again.
        await OpenPilotAsync(platform, from: "2026-01-01", to: "2026-01-31");
        Code(await Json(await Post(pilot, "/api/tax/calculations", Operation(Unique("Piloto vencido"), gross: 1037m)), HttpStatusCode.UnprocessableEntity))
            .Should().Be("tax_pilot_scope_expired");
        await OpenPilotAsync(platform);

        // Removing the tenant blocks new work; what it recorded stays readable and reproducible.
        var scope = await Json(await platform.GetAsync("/api/platform/tax/pilot"));
        var participant = scope.GetProperty("participants").EnumerateArray()
            .Single(p => p.GetProperty("tenantId").GetGuid() == tenant && p.GetProperty("active").GetBoolean()).GetProperty("id").GetGuid();
        await Json(await Post(platform, $"/api/platform/tax/pilot/participants/{participant}/remove", new { reason = "Fim da participação (teste)" }));
        Code(await Json(await Post(pilot, "/api/tax/calculations", Operation(Unique("Após retirada"), gross: 1038m)), HttpStatusCode.UnprocessableEntity))
            .Should().Be("tax_pilot_scope_tenant");
        var id = calc.GetProperty("id").GetGuid();
        (await Json(await pilot.GetAsync($"/api/tax/calculations/{id}"))).GetProperty("governance").GetProperty("mode").GetString().Should().Be("ControlledPilot");
        (await Json(await Post(pilot, $"/api/tax/calculations/{id}/verify", new { }))).GetProperty("reproducible").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task In_the_pilot_an_approved_rule_runs_and_the_calculation_records_its_governance()
    {
        var admin = await AdminWithProfileAsync();
        await ReadyAsync(_factory, admin);
        var calc = await Json(await Post(Pilot(admin), "/api/tax/calculations", Operation(Unique("Piloto"), gross: 1021m)));

        calc.GetProperty("totalTax").GetDecimal().Should().BeGreaterThan(0m);
        var governance = calc.GetProperty("governance");
        governance.GetProperty("mode").GetString().Should().Be("ControlledPilot");
        governance.GetProperty("policy").GetString().Should().Be("orken-pilot-approval/1");
        governance.GetProperty("enforced").GetBoolean().Should().BeTrue();
        governance.GetProperty("notice").GetString().Should().Contain("Não constitui parecer jurídico, contábil ou tributário profissional");
        governance.GetProperty("notApproved").GetArrayLength().Should().Be(0);
        var rules = governance.GetProperty("rules").EnumerateArray().ToList();
        rules.Should().NotBeEmpty().And.OnlyContain(r => r.GetProperty("approvedForPilot").GetBoolean()
                                                         && r.GetProperty("technicalStatus").GetString() == "Active"
                                                         && r.GetProperty("internalApprovalDecisionRef").GetString() == DecisionRecord
                                                         && r.GetProperty("professionalReview").GetString() == "Pending");
        governance.GetProperty("professionalReviewPending").EnumerateArray().Select(x => x.GetString()).Should().Contain("CBS.RATE.2026-TEST v1");

        var id = calc.GetProperty("id").GetGuid().ToString();
        (await AuditMetadataAsync("tax_calculation_created", id)).Should().ContainSingle()
            .Which.Should().Contain("ControlledPilot").And.Contain("orken-pilot-approval/1").And.Contain("Pending");
    }

    [Fact]
    public async Task A_revoked_or_superseded_rule_keeps_its_approval_on_record_but_backs_no_new_decision()
    {
        var admin = await AdminWithProfileAsync();
        await ReadyAsync(_factory, admin);
        var platform = await PlatformAsync();
        const string day = "2026-04-08";
        var (revokedKey, revokedId) = await NoteRuleAsync(platform, day);
        var (supersededKey, supersededId) = await NoteRuleAsync(platform, day);
        await ApproveAsync(platform, revokedId, day, day, "DEC-REVOKED");
        await ApproveAsync(platform, supersededId, day, day, "DEC-SUPERSEDED");

        var pilot = Pilot(admin);
        var calc = await Json(await Post(pilot, "/api/tax/calculations", Operation(Unique("Antes da revogação"), gross: 1041m, date: day)));
        var oppId = await PreparedOpportunityAsync(admin, calc, "PROP-REVOGADA");

        await Json(await Post(platform, $"/api/platform/tax/rule-versions/{revokedId}/status", new { status = "Revoked" }));
        var v2 = await NewVersionAsync(platform, supersededKey, day);
        await ApproveAsync(platform, v2, day, day, "DEC-SUPERSEDED-V2");

        var versions = await VersionsAsync(platform);
        versions[$"{revokedKey} v1"].GetProperty("status").GetString().Should().Be("Revoked");
        versions[$"{revokedKey} v1"].GetProperty("internalApproval").GetProperty("status").GetString().Should().Be("ApprovedForControlledPilot", "history is kept");
        versions[$"{supersededKey} v1"].GetProperty("status").GetString().Should().Be("Superseded");

        var refused = await Json(await Post(pilot, $"/api/tax/opportunities/{oppId}/approve", new { acknowledgeHighRisk = false }), HttpStatusCode.UnprocessableEntity);
        Code(refused).Should().Be("tax_pilot_approval_missing");
        refused.GetProperty("details").EnumerateArray().Select(d => d.GetString()).Should().BeEquivalentTo([$"{revokedKey} v1", $"{supersededKey} v1"]);
        (await AuditMetadataAsync("tax_decision_refused", oppId.ToString())).Should().ContainSingle().Which.Should().Contain(revokedKey);
        (await CountAsync(db => db.TaxDecisions.Where(d => d.OpportunityId == oppId))).Should().Be(0);

        var id = calc.GetProperty("id").GetGuid();
        var detect = await Json(await Post(pilot, $"/api/tax/calculations/{id}/opportunities/detect", new { }), HttpStatusCode.UnprocessableEntity);
        Code(detect).Should().Be("tax_pilot_approval_missing", "a revoked or superseded rule backs no new proposal either");
        detect.GetProperty("details").EnumerateArray().Select(d => d.GetString()).Should().Contain([$"{revokedKey} v1", $"{supersededKey} v1"]);
        (await Json(await Post(pilot, $"/api/tax/calculations/{id}/verify", new { }))).GetProperty("reproducible").GetBoolean()
            .Should().BeTrue("the calculation that used them still reproduces");
        var stored = (await Json(await pilot.GetAsync($"/api/tax/calculations/{id}"))).GetProperty("governance").GetProperty("rules").EnumerateArray();
        stored.Single(r => r.GetProperty("key").GetString() == revokedKey).GetProperty("approvedForPilot").GetBoolean()
            .Should().BeTrue("the snapshot shows the rule as it stood when the calculation ran");

        var now = await Json(await Post(pilot, "/api/tax/calculations", Operation(Unique("Depois da revogação"), gross: 1042m, date: day)));
        var keys = now.GetProperty("governance").GetProperty("rules").EnumerateArray().Select(r => r.GetProperty("key").GetString()).ToList();
        keys.Should().NotContain(revokedKey, "a revoked version never runs again").And.Contain(supersededKey);
        now.GetProperty("governance").GetProperty("rules").EnumerateArray().Single(r => r.GetProperty("key").GetString() == supersededKey)
            .GetProperty("version").GetInt32().Should().Be(2);
    }

    [Fact]
    public async Task Each_dossier_freezes_the_exact_decision_reference_of_every_rule()
    {
        var admin = await AdminWithProfileAsync();
        await ReadyAsync(_factory, admin);
        var platform = await PlatformAsync();
        const string day = "2026-04-09";
        var (key, versionId) = await NoteRuleAsync(platform, day);
        await ApproveAsync(platform, versionId, day, day, "DEC-X");
        var pilot = Pilot(admin);

        var first = await Json(await Post(pilot, "/api/tax/calculations", Operation(Unique("Referência X"), gross: 1043m, date: day)));
        var firstOpp = await PreparedOpportunityAsync(admin, first, "PROP-X");
        var firstDossier = (await Json(await Post(pilot, $"/api/tax/opportunities/{firstOpp}/approve", new { acknowledgeHighRisk = false })))
            .GetProperty("decision").GetProperty("dossier").GetProperty("governance");
        string RefOf(JsonElement g) => g.GetProperty("rules").EnumerateArray().Single(r => r.GetProperty("key").GetString() == key)
            .GetProperty("internalApproval").GetProperty("decisionReference").GetString()!;
        RefOf(firstDossier).Should().Be("DEC-X");
        firstDossier.GetProperty("decisionReferences").EnumerateArray().Select(d => d.GetString()).Should().BeEquivalentTo(["DEC-X", DecisionRecord],
            "each distinct decision behind the rules is identified, none replaced by a global record");

        await ApproveAsync(platform, versionId, day, day, "DEC-Y");   // reapproved under another decision

        RefOf((await Json(await admin.GetAsync($"/api/tax/opportunities/{firstOpp}"))).GetProperty("decision").GetProperty("dossier").GetProperty("governance"))
            .Should().Be("DEC-X", "a reapproval never rewrites a frozen dossier");
        (await Json(await admin.GetAsync($"/api/tax/calculations/{first.GetProperty("id").GetGuid()}"))).GetProperty("governance")
            .GetProperty("rules").EnumerateArray().Single(r => r.GetProperty("key").GetString() == key)
            .GetProperty("internalApprovalDecisionRef").GetString().Should().Be("DEC-X");

        var second = await Json(await Post(pilot, "/api/tax/calculations", Operation(Unique("Referência Y"), gross: 1044m, date: day)));
        var secondOpp = await PreparedOpportunityAsync(admin, second, "PROP-Y");
        RefOf((await Json(await Post(pilot, $"/api/tax/opportunities/{secondOpp}/approve", new { acknowledgeHighRisk = false })))
            .GetProperty("decision").GetProperty("dossier").GetProperty("governance")).Should().Be("DEC-Y");
    }

    [Fact]
    public async Task A_withdrawn_approval_blocks_the_pilot_and_the_definitive_mode_ignores_internal_approval()
    {
        var admin = await AdminWithProfileAsync();
        await ReadyAsync(_factory, admin);
        var platform = await PlatformAsync();
        const string day = "2026-04-07";
        var (key, versionId) = await NoteRuleAsync(platform, day);
        var pilot = Pilot(admin);

        var missing = await Json(await Post(pilot, "/api/tax/calculations", Operation(Unique("Sem aprovação"), gross: 1045m, date: day)), HttpStatusCode.UnprocessableEntity);
        Code(missing).Should().Be("tax_pilot_approval_missing", "technical activation is no approval");
        missing.GetProperty("details").EnumerateArray().Select(d => d.GetString()).Should().Equal($"{key} v1");

        await ApproveAsync(platform, versionId, day, day, "DEC-WITHDRAW");
        var approvedId = (await Json(await Post(pilot, "/api/tax/calculations", Operation(Unique("Aprovada"), gross: 1046m, date: day))))
            .GetProperty("id").GetGuid();
        var withdrawn = await Json(await Post(platform, $"/api/platform/tax/rule-versions/{versionId}/pilot-approval/withdraw",
            new { reason = "Critério de suspensão do teste" }));
        withdrawn.GetProperty("internalApproval").GetProperty("status").GetString().Should().Be("Withdrawn");
        withdrawn.GetProperty("internalApproval").GetProperty("decisionReference").GetString().Should().Be("DEC-WITHDRAW", "the record is kept");
        Code(await Json(await Post(pilot, "/api/tax/calculations", Operation(Unique("Retirada"), gross: 1047m, date: day)), HttpStatusCode.UnprocessableEntity))
            .Should().Be("tax_pilot_approval_missing");

        var detect = await Json(await Post(pilot, $"/api/tax/calculations/{approvedId}/opportunities/detect", new { }), HttpStatusCode.UnprocessableEntity);
        Code(detect).Should().Be("tax_pilot_approval_missing", "new proposals from a stored calculation need its rules approved today");
        detect.GetProperty("details").EnumerateArray().Select(d => d.GetString()).Should().Equal($"{key} v1");
        (await AuditMetadataAsync("tax_detection_refused", approvedId.ToString())).Should().ContainSingle(m => m.Contains($"{key} v1"));
        (await Json(await Post(pilot, $"/api/tax/calculations/{approvedId}/verify", new { }))).GetProperty("reproducible").GetBoolean()
            .Should().BeTrue("a withdrawn approval never makes history unreadable");

        await ForceActivateAsync(_factory, Nexo.Domain.Modules.TaxIntelligence.TaxApprovalMode.ProfessionalFinal);
        var definitive = HostWith(admin, host: Enforced("ProfessionalFinal"));
        var refused = await Json(await Post(definitive, "/api/tax/calculations", Operation(Unique("Definitivo"), gross: 1048m)), HttpStatusCode.UnprocessableEntity);
        Code(refused).Should().Be("tax_professional_review_pending", "the internal pilot approval is never a professional approval");
        refused.GetProperty("details").EnumerateArray().Select(d => d.GetString()).Should().Contain("CBS.RATE.2026-TEST v1");
    }

    [Fact]
    public async Task A_simulation_rests_on_the_same_approvals_as_a_calculation_on_every_date_it_runs()
    {
        var admin = await AdminWithProfileAsync();
        await ReadyAsync(_factory, admin);
        var platform = await PlatformAsync();
        const string day = "2026-04-10";
        var (key, versionId) = await NoteRuleAsync(platform, day);
        var pilot = Pilot(admin);
        object Simulation(string name) => new
        {
            name = Unique(name), baseline = Operation(Unique("Base"), gross: 1051m),
            scenarios = new object[] { new { name = "Na data da nota", referenceDate = day, convertConditionalDiscounts = false, assumeCreditable = false } },
        };
        object Offer(string name, decimal gross) => new
        {
            supplier = name, supplierRegime = "LucroReal",
            lines = new[] { new { itemKind = "Good", description = "Insumo", ncm = (string?)null, quantity = 1m, grossAmount = gross,
                unconditionalDiscount = 0m, conditionalDiscount = 0m, surcharges = 0m, excludedTaxes = (decimal?)0m, specialTreatment = (string?)null } },
        };
        object Comparison() => new { operationDate = day, assumeCreditable = true, offers = new[] { Offer("A", 1000m), Offer("B", 1010m) } };

        var missing = await Json(await Post(pilot, "/api/tax/simulations", Simulation("Sem aprovação")), HttpStatusCode.UnprocessableEntity);
        Code(missing).Should().Be("tax_pilot_approval_missing", "a scenario on another date needs the approvals of that date");
        missing.GetProperty("details").EnumerateArray().Select(d => d.GetString()).Should().Equal($"{key} v1");
        Code(await Json(await Post(pilot, "/api/tax/simulations/supplier-comparison", Comparison()), HttpStatusCode.UnprocessableEntity))
            .Should().Be("tax_pilot_approval_missing");

        await ApproveAsync(platform, versionId, day, day, "DEC-SIMULATION");
        await Json(await Post(pilot, "/api/tax/simulations", Simulation("Aprovada")));
        await Json(await Post(pilot, "/api/tax/simulations/supplier-comparison", Comparison()));

        await Json(await Post(platform, $"/api/platform/tax/rule-versions/{versionId}/pilot-approval/withdraw",
            new { reason = "Critério de suspensão do teste" }));
        Code(await Json(await Post(pilot, "/api/tax/simulations", Simulation("Retirada")), HttpStatusCode.UnprocessableEntity))
            .Should().Be("tax_pilot_approval_missing", "a withdrawn approval stops simulations exactly as it stops calculations");
        Code(await Json(await Post(pilot, "/api/tax/simulations/supplier-comparison", Comparison()), HttpStatusCode.UnprocessableEntity))
            .Should().Be("tax_pilot_approval_missing");
        (await AuditMetadataAsync("tax_simulation_refused")).Should().Contain(m => m.Contains($"{key} v1"), "every refused simulation is audited");

        await ForceActivateAsync(_factory, Nexo.Domain.Modules.TaxIntelligence.TaxApprovalMode.ProfessionalFinal);
        var definitive = HostWith(admin, host: Enforced("ProfessionalFinal"));
        Code(await Json(await Post(definitive, "/api/tax/simulations", Simulation("Definitivo")), HttpStatusCode.UnprocessableEntity))
            .Should().Be("tax_professional_review_pending", "the internal pilot approval is never a professional approval");
    }

    [Fact]
    public async Task The_pilot_is_a_shadow_mode_that_never_reaches_an_external_fiscal_effect()
    {
        var admin = await AdminWithProfileAsync();
        await ReadyAsync(_factory, admin);
        var fiscal = new SpyFiscalProvider();
        var pilot = HostWith(admin, s => s.Replace(ServiceDescriptor.Singleton<IFiscalDocumentProvider>(fiscal)), Enforced("ControlledPilot"));
        var (tenant, _) = Identity(admin);
        async Task<(int Ledger, int Settlements, int Decisions)> CountsAsync()
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
            return (await db.TaxCreditLedgerEntries.IgnoreQueryFilters().CountAsync(e => e.TenantId == tenant),
                await db.TaxSplitSettlements.IgnoreQueryFilters().CountAsync(e => e.TenantId == tenant),
                await db.TaxDecisions.IgnoreQueryFilters().CountAsync(e => e.TenantId == tenant));
        }
        var before = await CountsAsync();

        var calc = await Json(await Post(pilot, "/api/tax/calculations", Operation(Unique("Sombra"), gross: 1049m)));
        var oppId = await PreparedOpportunityAsync(admin, calc, "PROP-SOMBRA");
        await Json(await Post(pilot, $"/api/tax/opportunities/{oppId}/approve", new { acknowledgeHighRisk = false }));
        await Json(await Post(pilot, "/api/tax/simulations", new
        {
            name = Unique("Sim sombra"), baseline = Operation(Unique("Base sombra"), gross: 1050m),
            scenarios = new object[] { new { name = "Incondicional", convertConditionalDiscounts = true, assumeCreditable = false } },
        }));

        var after = await CountsAsync();
        fiscal.Calls.Should().Be(0, "the pilot never issues or changes a fiscal document");
        after.Ledger.Should().Be(before.Ledger, "no credit is appropriated or used by the pilot");
        after.Settlements.Should().Be(before.Settlements, "no split settlement is recorded by the pilot");
        after.Decisions.Should().Be(before.Decisions + 1, "only the internal decision record is written");
        (await Json(await pilot.GetAsync("/api/tax/governance"))).GetProperty("effectsNotice").GetString()
            .Should().Be("Piloto controlado — resultados para análise e validação. Não executa alteração fiscal, contábil ou financeira automaticamente.");
    }

    [Fact]
    public async Task A_pilot_decision_keeps_both_approvals_and_every_caveat_in_the_dossier()
    {
        var admin = await AdminWithProfileAsync();
        await ReadyAsync(_factory, admin);
        var pilotAdmin = Pilot(admin);
        var calc = await Json(await Post(pilotAdmin, "/api/tax/calculations", Operation(Unique("Decisão piloto"), gross: 1027m)));
        var oppId = await PreparedOpportunityAsync(admin, calc, "PROP-PILOTO");

        var approved = await Json(await Post(pilotAdmin, $"/api/tax/opportunities/{oppId}/approve", new { acknowledgeHighRisk = false }));
        approved.GetProperty("status").GetString().Should().Be("Approved");
        approved.GetProperty("professionalReviewPending").GetBoolean().Should().BeTrue();

        var dossier = approved.GetProperty("decision").GetProperty("dossier");
        dossier.GetProperty("nature").GetString().Should().Be("Registro auditável da decisão e das premissas utilizadas pelo motor. Não é prova definitiva de conformidade fiscal.");
        var governance = dossier.GetProperty("governance");
        governance.GetProperty("mode").GetString().Should().Be("ControlledPilot");
        governance.GetProperty("rules").EnumerateArray().Should().NotBeEmpty()
            .And.OnlyContain(r => r.GetProperty("internalApproval").GetProperty("status").GetString() == "ApprovedForControlledPilot"
                                  && !string.IsNullOrWhiteSpace(r.GetProperty("internalApproval").GetProperty("caveats").GetString())
                                  && r.GetProperty("internalApproval").GetProperty("approvedById").GetGuid() != Guid.Empty
                                  && r.GetProperty("professionalReview").GetProperty("status").GetString() == "Pending");
        dossier.GetProperty("professionalReview").GetProperty("requiredByPolicy").GetBoolean().Should().BeFalse();
        dossier.GetProperty("opportunity").GetProperty("caveats").GetString().Should().NotBeNullOrWhiteSpace();
        dossier.GetProperty("pendingIssues").EnumerateArray().Select(p => p.GetString()).Should().Contain(p => p!.StartsWith("Revisão tributária pendente"));
        dossier.GetProperty("impactAnalysis").GetProperty("financialEffect").GetString().Should().StartWith("Não determinado");

        var text = dossier.GetRawText();
        foreach (var claim in new[] { "parecer profissional aprovado", "Professionally Approved", "OAB n", "CRC n", "economia garantida", "tax saving garantido" })
            text.Should().NotContainEquivalentOf(claim);
        System.Text.RegularExpressions.Regex.IsMatch(text, @"(?<!não )economia definitiva", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Should().BeFalse();
    }

    /// <summary>Counts every call to the fiscal document provider (the pilot must make none).</summary>
    private sealed class SpyFiscalProvider : IFiscalDocumentProvider
    {
        public int Calls;
        public string Name => "spy";
        public bool IsConfigured => true;
        public Task<FiscalIssueResult> IssueConsumerNoteAsync(FiscalIssueRequest request, CancellationToken ct)
        {
            Interlocked.Increment(ref Calls);
            throw new InvalidOperationException("The controlled pilot must never issue a fiscal document.");
        }
    }

    private async Task<HttpClient> AdminOfNewCompanyAsync()
    {
        var email = $"pilot-{Guid.NewGuid():N}"[..20] + "@" + TestCredentials.TestDomain;
        var reg = await _factory.CreateApiClient().PostAsJsonAsync("/api/auth/register",
            new { name = "Empresa de Teste Piloto Ltda", email, password = TestCredentials.AdminPassword });
        reg.StatusCode.Should().Be(HttpStatusCode.OK, await reg.Content.ReadAsStringAsync());
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
            (await db.Users.IgnoreQueryFilters().SingleAsync(u => u.Login == email)).Activate();
            await db.SaveChangesAsync();
        }
        var admin = await AuthClientFactory.LoginAsync(_factory, email, TestCredentials.AdminPassword);
        await Json(await Post(admin, "/api/tax/profile/versions", new
        {
            validFrom = "2026-01-01", regime = "LucroPresumido", state = "SP", cityIbgeCode = "3550308", primaryCnae = "4711302",
            secondaryCnaes = Array.Empty<string>(), specialRegimes = Array.Empty<string>(), benefits = Array.Empty<string>(), reason = "Perfil de teste",
        }));
        return admin;
    }
}
