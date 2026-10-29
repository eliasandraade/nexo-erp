using System.Net;
using System.Text.Json;
using FluentAssertions;
using Nexo.IntegrationTests.Helpers;

namespace Nexo.IntegrationTests.TaxIntelligence;

/// <summary>
/// The definitive mode end to end, with FICTITIOUS professional approvals recorded only in this test database (its own fixture — never a
/// seed, never production). It proves: with every rule professionally approved for the date and regime, work runs and the reviews are
/// frozen with it; one rule pending, asking for changes, rejected, with an expired or future review, revoked, or reviewed for another
/// regime blocks new work; an approval with caveats runs and carries them; and a later review never rewrites a stored calculation or dossier.
/// </summary>
[Collection(TaxIsolatedCollection.Name)]
public sealed class TaxProfessionalFinalTests : TaxIsolatedSuite
{
    private static readonly string[] Catalogue = TaxPilotSetup.CatalogVersions;

    public TaxProfessionalFinalTests(TestWebApplicationFactory factory) : base(factory) { }

    private HttpClient Final(HttpClient c) => Host(c, TaxPilotSetup.Enforced("ProfessionalFinal"));

    /// <summary>Every 2026 version of the catalogue professionally approved (fictitious) and the definitive mode activated.</summary>
    private async Task<HttpClient> ReadyFinalAsync()
    {
        var platform = await PlatformAsync("ProfessionalFinal");
        var versions = await TaxPilotSetup.VersionsAsync(platform);
        foreach (var label in Catalogue)
            if (versions[label].GetProperty("professionalReview").GetString() != "Approved")
                await ReviewAsync(platform, versions[label].GetProperty("id").GetGuid(), "Approved");
        var state = await Json(await platform.GetAsync("/api/platform/tax/module"));
        if (state.GetProperty("newWorkRuns").GetBoolean() is false)
            await TaxPilotSetup.ActivateAsync(platform, "ProfessionalFinal");
        return platform;
    }

    private static async Task<JsonElement> Refused(HttpClient c, object operation)
        => await Json(await Post(c, "/api/tax/calculations", operation), HttpStatusCode.UnprocessableEntity);

    [Fact]
    public async Task The_definitive_mode_cannot_be_activated_while_a_rule_lacks_professional_approval_and_says_which()
    {
        var platform = await PlatformAsync("ProfessionalFinal");
        var checklist = await Json(await platform.GetAsync("/api/platform/tax/module/checklist?mode=ProfessionalFinal"));
        if (checklist.GetProperty("canActivate").GetBoolean()) return;   // another test of this class already approved everything
        checklist.GetProperty("rulesMissingApproval").EnumerateArray().Select(x => x.GetString()).Should().Contain("CBS.RATE.2026-TEST v1");
        var refused = await Json(await Post(platform, "/api/platform/tax/module/activate", new
        {
            mode = "ProfessionalFinal", decisionReference = "DEC-TESTE", reason = "teste", confirmation = "ATIVAR MODO DEFINITIVO", acknowledgeShadowMode = false,
        }), HttpStatusCode.UnprocessableEntity);
        Code(refused).Should().Be("tax_module_preconditions");
        refused.GetProperty("details").EnumerateArray().Select(d => d.GetString()).Should().Contain(d => d!.Contains("CBS.RATE.2026-TEST v1"));
    }

    [Fact]
    public async Task With_every_rule_approved_the_definitive_mode_runs_and_freezes_each_review()
    {
        await ReadyFinalAsync();
        var admin = Final(await AdminWithProfileAsync());
        var calc = await Json(await Post(admin, "/api/tax/calculations", Operation(Unique("Definitivo"), gross: 1201m)));
        var governance = calc.GetProperty("governance");
        governance.GetProperty("mode").GetString().Should().Be("ProfessionalFinal");
        governance.GetProperty("notApproved").GetArrayLength().Should().Be(0);
        var cbs = governance.GetProperty("rules").EnumerateArray().Single(r => r.GetProperty("key").GetString() == "CBS.RATE.2026-TEST");
        cbs.GetProperty("professionallyApproved").GetBoolean().Should().BeTrue();
        cbs.GetProperty("professionalReviewer").GetString().Should().Be("Profissional fictício da suíte de testes");
        cbs.GetProperty("professionalReviewRecordId").GetGuid().Should().NotBeEmpty("the exact review record is frozen with the calculation");
    }

    [Fact]
    public async Task Each_way_a_review_can_fail_blocks_new_work_and_a_caveated_approval_runs_with_its_caveats()
    {
        var platform = await ReadyFinalAsync();
        var admin = Final(await AdminWithProfileAsync());

        // 2. One rule still Pending on a date → blocked, naming it.
        var (pending, _) = await RuleAsync(platform, "2026-03-02", "2026-03-02");
        var r2 = await Refused(admin, Operation(Unique("Pendente"), gross: 1202m, date: "2026-03-02"));
        Code(r2).Should().Be("tax_professional_review_pending");
        r2.GetProperty("details").EnumerateArray().Select(x => x.GetString()).Should().Contain($"{pending} v1");

        // 3. Approved with caveats, valid → runs, caveats frozen.
        var (caveated, caveatedId) = await RuleAsync(platform, "2026-03-03", "2026-03-03");
        await ReviewAsync(platform, caveatedId, "ApprovedWithCaveats", "2026-03-03", "2026-03-03", caveats: "Ressalva fictícia de teste");
        var ok = await Json(await Post(admin, "/api/tax/calculations", Operation(Unique("Ressalva"), gross: 1203m, date: "2026-03-03")));
        ok.GetProperty("governance").GetProperty("rules").EnumerateArray().Single(r => r.GetProperty("key").GetString() == caveated)
            .GetProperty("professionalCaveats").GetString().Should().Be("Ressalva fictícia de teste");

        // 4–5. Changes required / rejected → blocked.
        var (changes, changesId) = await RuleAsync(platform, "2026-03-04", "2026-03-04");
        await ReviewAsync(platform, changesId, "ChangesRequired", null, null, notes: "Ajustar a condição (fictício).");
        Code(await Refused(admin, Operation(Unique("Mudancas"), gross: 1204m, date: "2026-03-04"))).Should().Be("tax_professional_review_pending");
        var (rejected, rejectedId) = await RuleAsync(platform, "2026-03-05", "2026-03-05");
        await ReviewAsync(platform, rejectedId, "Rejected", null, null, notes: "Leitura rejeitada (fictício).");
        (await Refused(admin, Operation(Unique("Rejeitada"), gross: 1205m, date: "2026-03-05"))).GetProperty("details").EnumerateArray()
            .Select(x => x.GetString()).Should().Contain($"{rejected} v1");

        // 6. Expired review → blocked. 7. Review that only starts later → blocked.
        var (_, expiringId) = await RuleAsync(platform, "2026-03-06", "2026-03-10");
        await ReviewAsync(platform, expiringId, "Approved", "2026-03-06", "2026-03-07");
        Code(await Refused(admin, Operation(Unique("Expirada"), gross: 1206m, date: "2026-03-09"))).Should().Be("tax_professional_review_pending");
        var (_, futureId) = await RuleAsync(platform, "2026-03-11", "2026-03-15");
        await ReviewAsync(platform, futureId, "Approved", "2026-03-14", "2026-03-15");
        Code(await Refused(admin, Operation(Unique("Futura"), gross: 1207m, date: "2026-03-12"))).Should().Be("tax_professional_review_pending");

        // 9. Review whose scope covers another regime only → blocked for this Lucro Presumido establishment.
        var (_, otherRegimeId) = await RuleAsync(platform, "2026-03-16", "2026-03-16");
        await ReviewAsync(platform, otherRegimeId, "Approved", "2026-03-16", "2026-03-16", regimes: ["LucroReal"]);
        Code(await Refused(admin, Operation(Unique("Regime"), gross: 1208m, date: "2026-03-16"))).Should().Be("tax_professional_review_pending");

        // A review received but not approving is not an approval.
        var (_, receivedId) = await RuleAsync(platform, "2026-03-17", "2026-03-17");
        await ReviewAsync(platform, receivedId, "ReviewReceived", null, null, notes: "Recebida (fictício).");
        Code(await Refused(admin, Operation(Unique("Recebida"), gross: 1209m, date: "2026-03-17"))).Should().Be("tax_professional_review_pending");
        _ = changes;
    }

    [Fact]
    public async Task A_later_review_or_a_revocation_never_rewrites_history_and_a_revoked_rule_backs_no_decision()
    {
        var platform = await ReadyFinalAsync();
        var admin = await AdminWithProfileAsync();
        var final = Final(admin);
        var (key, id) = await RuleAsync(platform, "2026-04-01", "2026-04-01");
        await ReviewAsync(platform, id, "Approved", "2026-04-01", "2026-04-01");
        var calc = await Json(await Post(final, "/api/tax/calculations", Operation(Unique("Historico"), gross: 1210m, date: "2026-04-01")));
        var calcId = calc.GetProperty("id").GetGuid();

        // Journey C: the definitive flow up to a decision whose dossier freezes the professional reviews.
        var gerente = Final(await UserAsync(admin, "gerente"));
        var oppId = calc.GetProperty("opportunityIds")[0].GetGuid();
        await PrepareScenarioAsync(gerente, oppId);
        var approved = await Json(await Post(final, $"/api/tax/opportunities/{oppId}/approve", new { acknowledgeHighRisk = false }));
        var dossier = approved.GetProperty("decision").GetProperty("dossier");
        var hash = approved.GetProperty("decision").GetProperty("dossierHash").GetString();
        dossier.GetProperty("governance").GetProperty("mode").GetString().Should().Be("ProfessionalFinal");
        dossier.GetProperty("governance").GetProperty("rules").EnumerateArray().Single(r => r.GetProperty("key").GetString() == key)
            .GetProperty("professionalReview").GetProperty("status").GetString().Should().Be("Approved");

        // A later review asking for changes: the stored calculation and the dossier still show what held then.
        await ReviewAsync(platform, id, "ChangesRequired", null, null, notes: "Revisão posterior (fictício).");
        var stored = await Json(await final.GetAsync($"/api/tax/calculations/{calcId}"));
        stored.GetProperty("governance").GetProperty("rules").EnumerateArray().Single(r => r.GetProperty("key").GetString() == key)
            .GetProperty("professionalReview").GetString().Should().Be("Approved");
        var again = await Json(await final.GetAsync($"/api/tax/decisions/{approved.GetProperty("decision").GetProperty("id").GetGuid()}"));
        again.GetProperty("dossierHash").GetString().Should().Be(hash);
        (await Json(await Post(final, $"/api/tax/calculations/{calcId}/verify", new { }))).GetProperty("reproducible").GetBoolean().Should().BeTrue();

        // 8. A revoked rule: the stored calculation stays reproducible, new work on it is refused.
        var history = await Json(await platform.GetAsync($"/api/platform/tax/rule-versions/{id}/professional-reviews"));
        history.GetArrayLength().Should().Be(2, "a later review never erases an earlier one");
        await Json(await Post(platform, $"/api/platform/tax/rule-versions/{id}/status", new { status = "Revoked" }));
        Code(await Json(await Post(final, $"/api/tax/calculations/{calcId}/opportunities/detect", new { }), HttpStatusCode.UnprocessableEntity))
            .Should().Be("tax_professional_review_pending");
        (await Json(await Post(final, $"/api/tax/calculations/{calcId}/verify", new { }))).GetProperty("reproducible").GetBoolean()
            .Should().BeTrue("a revoked rule never makes history unreadable");
    }

    [Fact]
    public async Task The_definitive_mode_needs_a_professionally_approved_classification_code()
    {
        var platform = await ReadyFinalAsync();
        var import = await Json(await Post(platform, "/api/platform/tax/classifications/import", new
        {
            kind = "CClassTrib", source = "Tabela fictícia da suíte", sourceVersion = "t1", sourceDocumentId = (Guid?)null,
            items = new[] { new { code = "TEST02", description = "Código fictício sem revisão", validFrom = "2026-01-01", validTo = (string?)null, notes = (string?)null } },
        }));
        import.GetProperty("imported").GetInt32().Should().Be(1);
        var code = (await Json(await platform.GetAsync("/api/platform/tax/classifications?search=TEST02"))).GetProperty("items")[0].GetProperty("id").GetGuid();
        var admin = Final(await AdminWithProfileAsync());
        // Draft: not in the catalogue for the matrix.
        Code(await Refused(admin, Operation(Unique("Rascunho"), gross: 1211m, cClassTrib: "TEST02"))).Should().Be("tax_operation_not_supported");
        await Json(await Post(platform, $"/api/platform/tax/classifications/{code}/status", new { status = "Active", reason = "teste" }));
        // Active but without a professional review: the definitive mode still refuses it.
        var refused = await Refused(admin, Operation(Unique("SemRevisao"), gross: 1212m, cClassTrib: "TEST02"));
        refused.GetProperty("details").EnumerateArray().Select(d => d.GetString()).Should().Contain(d => d!.Contains("TEST02") && d.Contains("revisão profissional"));
    }
}
