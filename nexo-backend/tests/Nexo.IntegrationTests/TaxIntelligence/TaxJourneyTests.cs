using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text;
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
/// Journeys of the finished module on its own database: disabled (A), the whole controlled pilot with private evidence up to the exported
/// dossier (B, H), tenant isolation of every new surface (F), the credit lifecycle (G), split management (I), the emergency suspension (J),
/// and the policies and catalogues behind them (materiality, retention, classification, Simples situation, structured compliance).
/// </summary>
[Collection(TaxIsolatedCollection.Name)]
public sealed class TaxJourneyTests : TaxIsolatedSuite
{
    public TaxJourneyTests(TestWebApplicationFactory factory) : base(factory) { }

    private HttpClient Pilot(HttpClient c) => Host(c, TaxPilotSetup.Enforced("ControlledPilot"));

    private async Task<(HttpClient Admin, HttpClient Platform)> ReadyPilotAsync()
    {
        var admin = await AdminWithProfileAsync();
        await TaxPilotSetup.ReadyAsync(Factory, admin);
        return (admin, await PlatformAsync());
    }

    // ── A: disabled ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_With_the_kill_switch_off_or_the_mode_not_activated_nothing_new_runs()
    {
        var admin = await AdminWithProfileAsync();
        var off = Host(admin, b => b.UseSetting("TaxIntelligence:Policy:Enabled", "false"));
        Code(await Json(await Post(off, "/api/tax/calculations", Operation(Unique("Desligado"), gross: 2001m)), HttpStatusCode.UnprocessableEntity))
            .Should().Be("tax_intelligence_disabled");
        var governance = await Json(await off.GetAsync("/api/tax/governance"));
        governance.GetProperty("enabled").GetBoolean().Should().BeFalse();
        governance.GetProperty("admitted").GetBoolean().Should().BeFalse();

        // Configured for the pilot but never activated by the platform: still nothing.
        var platform = await PlatformAsync();
        await TaxPilotSetup.DeactivateAsync(platform);
        Code(await Json(await Post(Pilot(admin), "/api/tax/calculations", Operation(Unique("NaoAtivado"), gross: 2002m)), HttpStatusCode.UnprocessableEntity))
            .Should().BeOneOf("tax_module_not_activated", "tax_pilot_scope_missing", "tax_pilot_scope_tenant");
        var state = await Json(await platform.GetAsync("/api/platform/tax/module"));
        state.GetProperty("newWorkRuns").GetBoolean().Should().BeFalse();
        state.GetProperty("configEnabled").GetBoolean().Should().BeTrue();

        // Activation never happens by a single click: wrong phrase, no shadow acknowledgement.
        Code(await Json(await Post(platform, "/api/platform/tax/module/activate", new
        {
            mode = "ControlledPilot", decisionReference = "DEC-X", reason = "teste", confirmation = "ativar", acknowledgeShadowMode = true,
        }), HttpStatusCode.UnprocessableEntity)).Should().Be("tax_module_confirmation");
        Code(await Json(await Post(platform, "/api/platform/tax/module/activate", new
        {
            mode = "ControlledPilot", decisionReference = "DEC-X", reason = "teste", confirmation = "ATIVAR PILOTO CONTROLADO", acknowledgeShadowMode = false,
        }), HttpStatusCode.UnprocessableEntity)).Should().Be("tax_module_shadow");
    }

    // ── B + H: controlled pilot with private evidence, up to the exported dossier ─

    [Fact]
    public async Task B_H_The_pilot_runs_end_to_end_with_private_evidence_and_an_exported_dossier()
    {
        var (admin, platform) = await ReadyPilotAsync();
        var pilot = Pilot(admin);
        var gerente = Pilot(await UserAsync(admin, "gerente"));

        var calc = await Json(await Post(pilot, "/api/tax/calculations", Operation(Unique("Piloto"), gross: 2101m)));
        calc.GetProperty("governance").GetProperty("mode").GetString().Should().Be("ControlledPilot");
        var sim = await Json(await Post(pilot, "/api/tax/simulations", new
        {
            name = Unique("Sim"), baseline = Operation(Unique("Base"), gross: 2102m), assumptions = "Preço mantido", caveats = "Hipótese de teste",
            scenarios = new object[] { new { name = "Sem condicional", convertConditionalDiscounts = true, assumeCreditable = false } },
        }));
        sim.GetProperty("governance").GetProperty("mode").GetString().Should().Be("ControlledPilot", "the governance a simulation ran under is frozen");
        sim.GetProperty("assumptions").GetString().Should().Be("Preço mantido");
        var csv = await pilot.GetAsync($"/api/tax/simulations/{sim.GetProperty("id").GetGuid()}/export");
        csv.StatusCode.Should().Be(HttpStatusCode.OK);
        (await csv.Content.ReadAsStringAsync()).Should().Contain("imposto_calculado").And.NotContain("economia");

        var oppId = calc.GetProperty("opportunityIds")[0].GetGuid();
        (await Post(gerente, $"/api/tax/opportunities/{oppId}/assign", new { userId = Guid.NewGuid() })).StatusCode
            .Should().Be(HttpStatusCode.NotFound, "only a user of this company can be made responsible");
        var analyst = (await Json(await gerente.GetAsync("/api/users"))).EnumerateArray().First(u => u.GetProperty("role").GetString() == "gerente");
        var assigned = await Json(await Post(gerente, $"/api/tax/opportunities/{oppId}/assign",
            new { userId = analyst.GetProperty("id").GetGuid(), userName = "Nome inventado" }));
        assigned.GetProperty("assignedToName").GetString().Should().Be(analyst.GetProperty("fullName").GetString(), "the name comes from the user record, not from the client");
        await PrepareScenarioAsync(gerente, oppId);
        var opp = await Json(await gerente.GetAsync($"/api/tax/opportunities/{oppId}"));
        var evidence = opp.GetProperty("evidence").EnumerateArray().Single(e => e.GetProperty("hashKind").GetString() == "sha256-file");
        var bytes = Pdf("proposta");
        evidence.GetProperty("contentHash").GetString().Should().Be(Sha256(bytes), "the evidence hash is the SHA-256 of the file's bytes");
        evidence.GetProperty("file").GetProperty("sha256").GetString().Should().Be(Sha256(bytes));
        var fileId = evidence.GetProperty("file").GetProperty("id").GetGuid();

        // Authorised download: the same bytes, as an attachment, never sniffed.
        var download = await gerente.GetAsync($"/api/tax/evidence-files/{fileId}/content");
        download.StatusCode.Should().Be(HttpStatusCode.OK);
        (await download.Content.ReadAsByteArrayAsync()).Should().Equal(bytes);
        download.Content.Headers.ContentDisposition!.DispositionType.Should().Be("attachment");
        download.Headers.GetValues("X-Content-Type-Options").Should().Contain("nosniff");

        // Scenario approval (board), execution verification with the authorised document, exported dossier.
        var approved = await Json(await Post(pilot, $"/api/tax/opportunities/{oppId}/approve", new { acknowledgeHighRisk = false }));
        var decisionId = approved.GetProperty("decision").GetProperty("id").GetGuid();
        approved.GetProperty("decision").GetProperty("dossier").GetProperty("evidence").EnumerateArray()
            .Should().Contain(e => e.GetProperty("file").ValueKind == JsonValueKind.Object && e.GetProperty("file").GetProperty("sha256").GetString() == Sha256(bytes),
                "the dossier points at the exact version of the evidence used");
        var execution = approved.GetProperty("requirements").EnumerateArray().Where(r => r.GetProperty("phase").GetString() == "ExecutionVerification").ToList();
        var nfe = Encoding.UTF8.GetBytes($"<?xml version=\"1.0\"?><nfeProc><NFe Id=\"{Guid.NewGuid():N}\"/></nfeProc>");
        foreach (var r in execution)
        {
            var reqId = r.GetProperty("id").GetGuid();
            if (r.GetProperty("requiredEvidence").ValueKind == JsonValueKind.String)
                await UploadEvidenceAsync(gerente, oppId, nfe, "nfe.xml", r.GetProperty("requiredEvidence").GetString()!, reqId, "XML autorizado da NF-e");
            await Json(await Post(gerente, $"/api/tax/opportunities/{oppId}/requirements/{reqId}", new { met = true, note = "conferido" }));
        }
        await Json(await Post(pilot, $"/api/tax/opportunities/{oppId}/execution", new
        {
            outcome = "Verified", fiscalDocumentReference = "35261012345678000190550010000099991000099999", authorizationProtocol = "135260000000001", notes = (string?)null,
        }));
        var timeline = await Json(await gerente.GetAsync($"/api/tax/opportunities/{oppId}/timeline"));
        timeline.EnumerateArray().Select(t => t.GetProperty("action").GetString()).Should()
            .Contain(["tax_opportunity_assigned", "tax_opportunity_approved", "tax_opportunity_execution_verified"]);

        // The package carries the decision's file (A) and the execution verification's file (B), each checked against its frozen hash.
        var decisionPath = $"evidencias/decisao/{Sha256(bytes)}.pdf";
        var executionPath = $"evidencias/execucao/{Sha256(nfe)}.xml";
        var (archive, manifest) = await ExportAsync(pilot, decisionId, includeFiles: true);
        using (archive)
        {
            manifest.GetProperty("problems").GetArrayLength().Should().Be(0);
            var listed = manifest.GetProperty("files").EnumerateArray().Select(f => f.GetProperty("path").GetString()).ToList();
            listed.Should().BeEquivalentTo(archive.Entries.Select(e => e.FullName).Where(n => n != "manifesto.json"), "the manifest covers every packaged file");
            foreach (var f in manifest.GetProperty("files").EnumerateArray())
                Sha256(await BytesAsync(archive, f.GetProperty("path").GetString()!)).Should().Be(f.GetProperty("sha256").GetString(), "every packaged file matches its manifest hash");
            (await BytesAsync(archive, decisionPath)).Should().Equal(bytes);
            (await BytesAsync(archive, executionPath)).Should().Equal(nfe, "the execution's fiscal document travels with the dossier");
            archive.GetEntry("evidencias/indice-execucao.json").Should().NotBeNull();
            manifest.GetProperty("evidenceFiles").EnumerateArray().Select(f => f.GetProperty("path").GetString()).Should().Contain([decisionPath, executionPath]);
            archive.Entries.Count(e => e.FullName == executionPath).Should().Be(1, "the same bytes are packaged once");
        }

        // Without files: not one byte of evidence.
        var (bare, bareManifest) = await ExportAsync(pilot, decisionId, includeFiles: false);
        using (bare)
        {
            bare.Entries.Should().NotContain(e => e.FullName.StartsWith("evidencias/decisao/") || e.FullName.StartsWith("evidencias/execucao/"));
            bareManifest.GetProperty("evidenceFiles").GetArrayLength().Should().Be(0);
        }

        // A fiscal file whose stored bytes changed is reported, never packaged; the decision's file still is.
        using (var scope = Factory.Services.CreateScope())
        {
            var nfeSha = Sha256(nfe);
            var keys = await scope.ServiceProvider.GetRequiredService<NexoDbContext>().TaxEvidenceFiles.IgnoreQueryFilters()
                .Where(f => f.Sha256 == nfeSha).Select(f => f.StorageKey).ToListAsync();
            foreach (var key in keys) Factory.TaxFiles.Corrupt(key);
        }
        var (broken, brokenManifest) = await ExportAsync(pilot, decisionId, includeFiles: true);
        using (broken)
        {
            brokenManifest.GetProperty("problems").EnumerateArray().Select(p => p.GetString()).Should().Contain(p => p!.Contains("(execucao)") && p.Contains("SHA-256"));
            broken.GetEntry(executionPath).Should().BeNull();
            broken.GetEntry(decisionPath).Should().NotBeNull();
        }

        (await gerente.GetAsync($"/api/tax/decisions/{decisionId}/export")).StatusCode.Should().Be(HttpStatusCode.Forbidden, "exporting is the board's");
        (await (await OtherCompanyAsync()).GetAsync($"/api/tax/decisions/{decisionId}/export?includeFiles=true")).StatusCode
            .Should().Be(HttpStatusCode.NotFound, "another company never finds this dossier");

        // Shadow mode: the pilot workflow produced nothing fiscal, accounting or financial for this operation.
        var calcId = calc.GetProperty("id").GetGuid();
        (await CountAsync(db => db.TaxCredits.IgnoreQueryFilters().Where(c => c.SourceCalculationId == calcId))).Should().Be(0);
        (await CountAsync(db => db.TaxSplitExpectations.IgnoreQueryFilters().Where(e => e.CalculationId == calcId))).Should().Be(0);
        _ = platform;
    }

    private static async Task<(ZipArchive Archive, JsonElement Manifest)> ExportAsync(HttpClient c, Guid decisionId, bool includeFiles)
    {
        var zip = await c.GetAsync($"/api/tax/decisions/{decisionId}/export?includeFiles={(includeFiles ? "true" : "false")}");
        zip.StatusCode.Should().Be(HttpStatusCode.OK);
        var archive = new ZipArchive(new MemoryStream(await zip.Content.ReadAsByteArrayAsync()), ZipArchiveMode.Read);
        var manifest = JsonDocument.Parse(await BytesAsync(archive, "manifesto.json")).RootElement.Clone();
        return (archive, manifest);
    }

    private static async Task<byte[]> BytesAsync(ZipArchive archive, string path)
    {
        using var s = new MemoryStream();
        await using var entry = archive.GetEntry(path)!.Open();
        await entry.CopyToAsync(s);
        return s.ToArray();
    }

    [Fact]
    public async Task H_Evidence_files_are_checked_by_their_bytes_versioned_revoked_and_never_cross_tenants()
    {
        var (admin, _) = await ReadyPilotAsync();
        var pilot = Pilot(admin);
        var gerente = Pilot(await UserAsync(admin, "gerente"));
        var calc = await Json(await Post(pilot, "/api/tax/calculations", Operation(Unique("Evidencia"), gross: 2201m)));
        var oppId = calc.GetProperty("opportunityIds")[0].GetGuid();
        await Json(await Post(gerente, $"/api/tax/opportunities/{oppId}/review", new { }));

        // Type by bytes: HTML named .pdf, SVG inside XML, an extension that does not match — all refused.
        Code(await UploadEvidenceAsync(gerente, oppId, Encoding.UTF8.GetBytes("<html><script>x</script></html>"), "falso.pdf", "Other", null, "x",
            HttpStatusCode.UnprocessableEntity)).Should().Be("tax_file_type");
        Code(await UploadEvidenceAsync(gerente, oppId, Encoding.UTF8.GetBytes("<?xml version=\"1.0\"?><svg onload=\"x\"/>"), "a.xml", "Other", null, "x",
            HttpStatusCode.UnprocessableEntity)).Should().Be("tax_file_active_content");
        Code(await UploadEvidenceAsync(gerente, oppId, Pdf("x"), "a.png", "Other", null, "x", HttpStatusCode.UnprocessableEntity))
            .Should().Be("tax_file_extension");
        Code(await UploadEvidenceAsync(gerente, oppId, new byte[7 * 1024 * 1024 + 1], "grande.pdf", "Other", null, "x", HttpStatusCode.UnprocessableEntity))
            .Should().BeOneOf("tax_file_too_large", "tax_file_type");

        var first = await UploadEvidenceAsync(gerente, oppId, Pdf("v1"), "../../etc/passwd.pdf", "Other", null, "Contrato v1");
        var e1 = first.GetProperty("evidence").EnumerateArray().Last();
        e1.GetProperty("file").GetProperty("originalFileName").GetString().Should().Be("passwd.pdf", "a name never becomes a path");

        // A newer version supersedes the old one; both stay listed.
        using (var form = new MultipartFormDataContent())
        {
            form.Add(new ByteArrayContent(Pdf("v2")), "file", "contrato-v2.pdf");
            form.Add(new StringContent("Contrato v2 assinado"), "description");
            var replaced = await Json(await gerente.PostAsync($"/api/tax/opportunities/{oppId}/evidence/{e1.GetProperty("id").GetGuid()}/replace", form));
            var versions = replaced.GetProperty("evidence").EnumerateArray().ToList();
            versions.Single(e => e.GetProperty("id").GetGuid() == e1.GetProperty("id").GetGuid()).GetProperty("status").GetString().Should().Be("Superseded");
            var current = versions.Single(e => e.GetProperty("supersedesEvidenceId").ValueKind == JsonValueKind.String);
            current.GetProperty("version").GetInt32().Should().Be(2);
            current.GetProperty("contentHash").GetString().Should().Be(Sha256(Pdf("v2")));

            // Revoked: still on record; downloadable by the board only (audit of what was withdrawn).
            var revoked = await Json(await Post(gerente, $"/api/tax/opportunities/{oppId}/evidence/{current.GetProperty("id").GetGuid()}/revoke",
                new { reason = "Arquivo da operação errada" }));
            var revokedFile = revoked.GetProperty("evidence").EnumerateArray().Single(e => e.GetProperty("id").GetGuid() == current.GetProperty("id").GetGuid())
                .GetProperty("file").GetProperty("id").GetGuid();
            (await gerente.GetAsync($"/api/tax/evidence-files/{revokedFile}/content")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await pilot.GetAsync($"/api/tax/evidence-files/{revokedFile}/content")).StatusCode.Should().Be(HttpStatusCode.OK);

            // Another company never finds it.
            var other = await OtherCompanyAsync();
            (await other.GetAsync($"/api/tax/evidence-files/{revokedFile}/content")).StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await other.GetAsync($"/api/tax/opportunities/{oppId}/timeline")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        }
        (await CountAsync(db => db.AuditRecords.IgnoreQueryFilters().Where(a => a.ActionType == "tax_evidence_file_downloaded"))).Should().BeGreaterThan(0);
    }

    // ── F: tenant isolation of the new surfaces ─────────────────────────────────

    [Fact]
    public async Task F_Another_company_never_sees_or_changes_policies_classifications_files_or_dossiers()
    {
        var admin = await AdminWithProfileAsync();
        await Json(await Put(admin, "/api/tax/materiality", new
        {
            threshold = 5000m, currency = "BRL", effectiveFrom = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)).ToString("yyyy-MM-dd"),
            effectiveTo = (string?)null, rationale = "Materialidade de teste", decisionReference = "ATA-TESTE-1",
        }));
        var credit = await Json(await Post(admin, "/api/tax/credits", new
        {
            tax = "Cbs", sourceDocumentType = "NF-e", sourceDocumentKey = (string?)null, supplierDocument = (string?)null,
            acquiredOn = "2026-06-01", expiresOn = (string?)null, originalAmount = 10m, potentialAmount = 10m, potentialBasis = "teste",
        }));
        using var form = new MultipartFormDataContent
        {
            { new ByteArrayContent(Pdf("credito")), "file", "nfe-entrada.pdf" },
            { new StringContent("Credit"), "owner" },
            { new StringContent(credit.GetProperty("id").GetGuid().ToString()), "ownerId" },
            { new StringContent("FiscalDocument"), "type" },
        };
        var file = await Json(await admin.PostAsync("/api/tax/evidence-files", form));

        var other = await OtherCompanyAsync();
        (await Json(await other.GetAsync("/api/tax/materiality"))).GetProperty("history").GetArrayLength().Should().Be(0);
        (await other.GetAsync($"/api/tax/evidence-files/{file.GetProperty("file").GetProperty("id").GetGuid()}/content")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await Json(await other.GetAsync($"/api/tax/evidence-files?owner=Credit&ownerId={credit.GetProperty("id").GetGuid()}"))).GetArrayLength().Should().Be(0);
        (await Post(other, $"/api/tax/evidence-files/{file.GetProperty("file").GetProperty("id").GetGuid()}/revoke", new { reason = "x" }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await other.GetAsync($"/api/tax/decisions/{Guid.NewGuid()}/export")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await other.GetAsync("/api/platform/tax/module")).StatusCode.Should().Be(HttpStatusCode.Forbidden, "a company token never reaches the platform");
    }

    // ── G: credit lifecycle ─────────────────────────────────────────────────────

    [Fact]
    public async Task G_A_credit_goes_from_potential_to_partial_use_and_reversal_only_through_the_board_and_never_twice()
    {
        var admin = await AdminWithProfileAsync();
        var gerente = await UserAsync(admin, "gerente");
        var credit = await Json(await Post(gerente, "/api/tax/credits", new
        {
            tax = "Ibs", sourceDocumentType = "NF-e", sourceDocumentKey = "35261012345678000190550010000012341000099991", supplierDocument = (string?)null,
            acquiredOn = "2026-06-01", expiresOn = "2026-12-31", originalAmount = 100m, potentialAmount = 90m, potentialBasis = "Estimativa gerencial",
        }));
        var id = credit.GetProperty("id").GetGuid();
        var url = $"/api/tax/credits/{id}/movements";
        await Json(await Post(gerente, url, new { movement = "StartRequirementsReview" }));
        (await Post(gerente, url, new { movement = "ConfirmToBeAppropriated", amount = 90m, reason = "requisitos conferidos" }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden, "writing the ledger is the board's");
        await Json(await Post(admin, url, new { movement = "ConfirmToBeAppropriated", amount = 90m, reason = "requisitos conferidos" }));
        await Json(await Post(admin, url, new { movement = "Appropriate", amount = 90m, reason = "apropriação", idempotencyKey = "aprop-1" }));
        var replay = await Json(await Post(admin, url, new { movement = "Appropriate", amount = 90m, reason = "apropriação", idempotencyKey = "aprop-1" }));
        replay.GetProperty("balance").GetProperty("appropriated").GetDecimal().Should().Be(90m, "a retried request is not written twice");
        await Json(await Post(admin, url, new { movement = "Use", amount = 30m, obligationTax = "Ibs", obligationReference = "APUR-2026-06", onDate = "2026-06-30" }));
        var reversed = await Json(await Post(admin, url, new { movement = "Reverse", amount = 10m, reason = "estorno parcial" }));
        var b = reversed.GetProperty("balance");
        (b.GetProperty("appropriated").GetDecimal(), b.GetProperty("used").GetDecimal(), b.GetProperty("available").GetDecimal()).Should().Be((80m, 30m, 50m));
        (await Json(await Post(admin, url, new { movement = "Use", amount = 1m, obligationTax = "Ibs", obligationReference = "APUR-2026-06", onDate = "2026-06-30" }),
            HttpStatusCode.Conflict)).GetProperty("code").GetString().Should().Be("tax_credit_obligation_used");

        var totals = await Json(await admin.GetAsync("/api/tax/credits/totals"));
        totals.EnumerateArray().Single(t => t.GetProperty("tax").GetString() == "Ibs").GetProperty("available").GetDecimal().Should().BeGreaterThanOrEqualTo(50m);
        (await Json(await admin.GetAsync("/api/tax/credits?stage=ToBeAppropriated&take=10"))).GetProperty("total").GetInt32().Should().BeGreaterThan(0);
    }

    // ── I: split management ─────────────────────────────────────────────────────

    [Fact]
    public async Task I_Split_retentions_reconcile_partially_diverge_reverse_and_export_without_ever_claiming_a_retention()
    {
        var admin = await AdminWithProfileAsync();
        var calc = await Json(await Post(admin, "/api/tax/calculations", Operation(Unique("Split"), gross: 2301m)));
        var expectations = await Json(await Post(admin, $"/api/tax/calculations/{calc.GetProperty("id").GetGuid()}/split-expectations", new { procedure = "Standard" }));
        var cbs = expectations.EnumerateArray().Single(e => e.GetProperty("tax").GetString() == "Cbs");
        cbs.GetProperty("status").GetString().Should().Be("Pending", "an expectation alone is not a retention");
        var id = cbs.GetProperty("id").GetGuid();
        var expected = cbs.GetProperty("expected").GetDecimal();

        var partial = await Json(await Post(admin, $"/api/tax/split/expectations/{id}/settlements",
            new { actualAmount = decimal.Round(expected / 2, 2), externalReference = "PSP-1", settledAt = "2026-06-16T12:00:00Z" }));
        partial.GetProperty("status").GetString().Should().Be("Insufficient");
        (await Json(await admin.GetAsync("/api/tax/split/expectations?status=Insufficient"))).GetProperty("items").EnumerateArray()
            .Should().Contain(e => e.GetProperty("id").GetGuid() == id);
        var rest = expected - decimal.Round(expected / 2, 2);
        var matched = await Json(await Post(admin, $"/api/tax/split/expectations/{id}/settlements",
            new { actualAmount = rest + 5m, externalReference = "PSP-2", settledAt = "2026-06-16T13:00:00Z" }));
        matched.GetProperty("status").GetString().Should().Be("Excess");
        var wrong = matched.GetProperty("settlements").EnumerateArray().Single(s => s.GetProperty("externalReference").GetString() == "PSP-2").GetProperty("id").GetGuid();
        await Json(await Post(admin, $"/api/tax/split/settlements/{wrong}/reverse", new { reason = "valor errado" }));
        var fixedUp = await Json(await Post(admin, $"/api/tax/split/expectations/{id}/settlements",
            new { actualAmount = rest, externalReference = "PSP-3", settledAt = "2026-06-16T14:00:00Z" }));
        fixedUp.GetProperty("status").GetString().Should().Be("Matched");
        (await Json(await Post(admin, $"/api/tax/split/expectations/{id}/settlements",
            new { actualAmount = 1m, externalReference = "PSP-3", settledAt = "2026-06-16T14:00:00Z" }), HttpStatusCode.Conflict))
            .GetProperty("code").GetString().Should().Be("tax_split_duplicate_reference");

        var csv = await admin.GetAsync("/api/tax/split/expectations/export?status=Matched");
        (await csv.Content.ReadAsStringAsync()).Should().Contain("PSP-3");
        Code(await Json(await Post(admin, "/api/tax/split/provider/import?from=2026-06-01&to=2026-06-30", new { }), HttpStatusCode.UnprocessableEntity))
            .Should().Be("tax_split_provider_missing", "with no provider integrated nothing is ever invented as retained");
    }

    [Fact]
    public async Task I_The_split_export_carries_every_filtered_row_past_the_page_size_or_refuses_saying_why()
    {
        var admin = await AdminWithProfileAsync();
        const string day = "2026-09-17";   // only this test operates on this date: the date filter isolates its rows
        var expectationIds = new List<Guid>();
        for (var i = 0; i < 130; i++)
        {
            var calc = await Json(await Post(admin, "/api/tax/calculations", Operation(Unique("SplitLote"), gross: 1000m + i, date: day)));
            var created = await Json(await Post(admin, $"/api/tax/calculations/{calc.GetProperty("id").GetGuid()}/split-expectations", new { procedure = "Standard" }));
            expectationIds.AddRange(created.EnumerateArray().Select(e => e.GetProperty("id").GetGuid()));
        }
        expectationIds.Count.Should().BeGreaterThan(250, "the export must go past the list's page of 200");

        // A settlement matched on one row, and a wrong one reversed on another: the export shows the reconciliation as it stands.
        var first = await Json(await admin.GetAsync($"/api/tax/split/expectations?from={day}&to={day}&take=2"));
        var matched = first.GetProperty("items")[0];
        await Json(await Post(admin, $"/api/tax/split/expectations/{matched.GetProperty("id").GetGuid()}/settlements",
            new { actualAmount = matched.GetProperty("expected").GetDecimal(), externalReference = "LOTE-OK", settledAt = "2026-09-18T12:00:00Z" }));
        var reversedRow = first.GetProperty("items")[1];
        var wrong = await Json(await Post(admin, $"/api/tax/split/expectations/{reversedRow.GetProperty("id").GetGuid()}/settlements",
            new { actualAmount = 1m, externalReference = "LOTE-ERRADO", settledAt = "2026-09-18T12:00:00Z" }));
        await Json(await Post(admin, $"/api/tax/split/settlements/{wrong.GetProperty("settlements")[0].GetProperty("id").GetGuid()}/reverse", new { reason = "valor errado" }));

        async Task<string[]> CsvAsync(HttpClient c, string query)
        {
            var r = await c.GetAsync($"/api/tax/split/expectations/export?{query}");
            r.StatusCode.Should().Be(HttpStatusCode.OK);
            return (await r.Content.ReadAsStringAsync()).TrimStart('\uFEFF').Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.TrimEnd('\r')).ToArray();
        }

        var all = await CsvAsync(admin, $"from={day}&to={day}");
        all.Count(l => l.StartsWith("calculo;")).Should().Be(1, "the header appears once");
        var lines = all.Skip(1).ToList();
        lines.Should().HaveCount(expectationIds.Count, "every row the filters match is exported, not one page");
        lines.Select(l => Col(l, 1)).Should().OnlyContain(d => d == day);
        lines.Should().ContainSingle(l => l.Contains("LOTE-OK") && Col(l, 7) == "Matched");
        lines.Should().ContainSingle(l => l.Contains("LOTE-ERRADO") && Col(l, 7) == "Pending", "a reversed retention leaves the expectation pending");

        // The same filters as the list: by status and by tax.
        var pending = (await CsvAsync(admin, $"from={day}&to={day}&status=Pending")).Skip(1).ToList();
        pending.Should().HaveCount(expectationIds.Count - 1).And.OnlyContain(l => Col(l, 7) == "Pending");
        var cbs = (await CsvAsync(admin, $"from={day}&to={day}&tax=Cbs")).Skip(1).ToList();
        cbs.Should().HaveCount(130).And.OnlyContain(l => Col(l, 2) == "Cbs");

        // Above the configured ceiling the export is refused with the limit stated — never truncated.
        var capped = Host(admin, b => b.UseSetting("TaxIntelligence:Export:MaxSplitRows", "100"));
        var refused = await Json(await capped.GetAsync($"/api/tax/split/expectations/export?from={day}&to={day}"), HttpStatusCode.UnprocessableEntity);
        Code(refused).Should().Be("tax_split_export_too_large");

        // Another company exports nothing of this one.
        var other = await CsvAsync(await OtherCompanyAsync(), $"from={day}&to={day}");
        other.Should().HaveCount(1, "only the header: another company's retentions are never exported");
    }

    private static readonly string[] NfeOnly = ["NF-e"];

    private static string Col(string csvLine, int index) => csvLine.Split(';')[index];

    // ── J: emergency ────────────────────────────────────────────────────────────

    [Fact]
    public async Task J_A_suspension_stops_new_work_at_once_and_keeps_history_readable_and_reproducible()
    {
        var (admin, platform) = await ReadyPilotAsync();
        var pilot = Pilot(admin);
        var calc = await Json(await Post(pilot, "/api/tax/calculations", Operation(Unique("Antes"), gross: 2401m)));
        var calcId = calc.GetProperty("id").GetGuid();
        try
        {
            (await Post(platform, "/api/platform/tax/module/suspend", new { reason = "Incidente de teste", confirmation = "suspender" }))
                .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "the phrase must be typed exactly");
            await Json(await Post(platform, "/api/platform/tax/module/suspend", new { reason = "Incidente de teste", confirmation = "SUSPENDER" }));
            Code(await Json(await Post(pilot, "/api/tax/calculations", Operation(Unique("Durante"), gross: 2402m)), HttpStatusCode.UnprocessableEntity))
                .Should().Be("tax_module_suspended");
            Code(await Json(await Post(pilot, $"/api/tax/calculations/{calcId}/opportunities/detect", new { }), HttpStatusCode.UnprocessableEntity))
                .Should().Be("tax_module_suspended");
            (await Json(await pilot.GetAsync($"/api/tax/calculations/{calcId}"))).GetProperty("id").GetGuid().Should().Be(calcId);
            (await Json(await Post(pilot, $"/api/tax/calculations/{calcId}/verify", new { }))).GetProperty("reproducible").GetBoolean().Should().BeTrue();
            var readiness = await Json(await platform.GetAsync("/api/platform/tax/readiness"));
            readiness.GetProperty("module").GetProperty("status").GetString().Should().Be("Suspended");
            (await Json(await platform.GetAsync("/api/platform/tax/refusals"))).EnumerateArray()
                .Should().Contain(r => r.GetProperty("code").GetString() == "tax_module_suspended");
        }
        finally
        {
            await TaxPilotSetup.DeactivateAsync(platform);
        }
    }

    // ── Policies, catalogues and the matrix ─────────────────────────────────────

    [Fact]
    public async Task Materiality_is_versioned_by_date_and_never_retroactive()
    {
        var admin = await AdminWithProfileAsync();
        var gerente = await UserAsync(admin, "gerente");
        var tomorrow = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(2)).ToString("yyyy-MM-dd");
        var body = new { threshold = 1000m, currency = "BRL", effectiveFrom = tomorrow, effectiveTo = (string?)null, rationale = "teste", decisionReference = "ATA-2" };
        (await Put(gerente, "/api/tax/materiality", body)).StatusCode.Should().Be(HttpStatusCode.Forbidden, "a company policy is the board's");
        Code(await Json(await Put(admin, "/api/tax/materiality", body with { effectiveFrom = "2026-01-01" }), HttpStatusCode.UnprocessableEntity))
            .Should().Be("tax_materiality_retroactive");
        var m = await Json(await Put(admin, "/api/tax/materiality", body));
        m.GetProperty("history").EnumerateArray().Should().Contain(h => h.GetProperty("threshold").GetDecimal() == 1000m);
        (await Put(admin, "/api/tax/materiality", body with { threshold = -1m })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Put(admin, "/api/tax/materiality", body with { threshold = 10.001m })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Retention_only_previews_and_a_legal_hold_always_wins()
    {
        var admin = await AdminWithProfileAsync();
        var platform = await PlatformAsync();
        await Json(await Put(platform, "/api/platform/tax/retention-policies", new
        {
            evidenceType = "Other", retentionMonths = 1, startsAt = "UploadDate", enabled = true, allowTenantOverride = false,
            rationale = "Prazo de teste", reference = "Política de teste",
        }));
        (await Put(admin, "/api/tax/retention-policies", new
        {
            evidenceType = "Other", retentionMonths = 120, startsAt = "UploadDate", enabled = true, allowTenantOverride = false, rationale = "x", reference = "y",
        })).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "the platform default does not allow an override for this type");
        var preview = await Json(await admin.GetAsync("/api/tax/retention-policies/preview"));
        preview.GetProperty("automaticDisposal").GetBoolean().Should().BeFalse();
        preview.GetProperty("notice").GetString().Should().Contain("Nenhum arquivo é descartado automaticamente");
        (await CountAsync(db => db.TaxEvidenceFiles.IgnoreQueryFilters())).Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task Classification_codes_are_imported_as_drafts_and_only_active_ones_reach_the_matrix()
    {
        var admin = await AdminWithProfileAsync();
        var platform = await PlatformAsync();
        var code = "J" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        await Json(await Post(platform, "/api/platform/tax/classifications/import", new
        {
            kind = "CClassTrib", source = "Fonte de teste", sourceVersion = "1", sourceDocumentId = (Guid?)null,
            items = new[] { new { code, description = "Código de teste", validFrom = "2026-01-01", validTo = (string?)null, notes = (string?)null } },
        }));
        var preview = await Json(await Post(admin, "/api/tax/calculations/coverage", Operation(Unique("Rascunho"), cClassTrib: code)));
        preview.GetProperty("status").GetString().Should().Be("RequiresClassification");
        preview.GetProperty("findings").EnumerateArray().Should().Contain(f => f.GetProperty("code").GetString() == "line-classification-not-cataloged");
        var id = (await Json(await platform.GetAsync($"/api/platform/tax/classifications?search={code}"))).GetProperty("items")[0].GetProperty("id").GetGuid();
        await Json(await Post(platform, $"/api/platform/tax/classifications/{id}/status", new { status = "Active", reason = "fonte conferida" }));
        (await Json(await Post(admin, "/api/tax/calculations/coverage", Operation(Unique("Ativo"), cClassTrib: code)))).GetProperty("status").GetString()
            .Should().Be("Supported");
        // Malformed codes never enter the catalogue.
        (await Post(platform, "/api/platform/tax/classifications/import", new
        {
            kind = "Ncm", source = "Fonte", sourceVersion = (string?)null, sourceDocumentId = (Guid?)null,
            items = new[] { new { code = "123", description = "x", validFrom = "2026-01-01", validTo = (string?)null, notes = (string?)null } },
        })).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Simples_MEI_SIMEI_and_an_unknown_option_are_never_read_as_the_regular_regime()
    {
        var admin = await AuthClientFactory.LoginAsAdminAsync(Factory);
        var other = await OtherCompanyAsync();
        await Json(await Post(other, "/api/tax/profile/versions", new
        {
            validFrom = "2026-01-01", regime = "SimplesNacional", state = "SP", cityIbgeCode = "3550308", primaryCnae = "4711302",
            secondaryCnaes = Array.Empty<string>(), specialRegimes = Array.Empty<string>(), benefits = Array.Empty<string>(), reason = "Simples sem a opção informada",
        }));
        var unknown = await Json(await Post(other, "/api/tax/calculations/coverage", Operation(Unique("Simples"))));
        unknown.GetProperty("status").GetString().Should().Be("RequiresProfessionalReview");
        unknown.GetProperty("findings").EnumerateArray().Should().Contain(f => f.GetProperty("code").GetString() == "regime-establishment-option-unknown");
        (await Post(other, "/api/tax/profile/versions", new
        {
            validFrom = "2026-01-01", regime = "LucroReal", state = "SP", cityIbgeCode = "3550308", primaryCnae = "4711302", simplesSituation = "Simei",
            secondaryCnaes = Array.Empty<string>(), specialRegimes = Array.Empty<string>(), benefits = Array.Empty<string>(), reason = "inconsistente",
        })).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "a SIMEI situation does not fit the Lucro Real regime");
        await Json(await Post(other, "/api/tax/profile/versions", new
        {
            validFrom = "2026-01-01", regime = "SimplesNacional", state = "SP", cityIbgeCode = "3550308", primaryCnae = "4711302",
            simplesSituation = "WithinSublimit", ibsCbsRegularRegimeOption = false,
            secondaryCnaes = Array.Empty<string>(), specialRegimes = Array.Empty<string>(), benefits = Array.Empty<string>(), reason = "Simples sem opção pelo regular",
        }));
        (await Json(await Post(other, "/api/tax/calculations/coverage", Operation(Unique("SimplesOk"))))).GetProperty("status").GetString()
            .Should().Be("Supported");
        var supplier = await Json(await Post(admin, "/api/tax/calculations/coverage", new
        {
            operationDate = "2026-06-15", direction = "Inbound", counterpartyRegime = "SimplesNacional", counterpartyState = (string?)null, description = "Fornecedor",
            lines = new[] { new { itemKind = "Good", description = "Item", ncm = "61091000", quantity = 1m, grossAmount = 100m, unconditionalDiscount = 0m,
                conditionalDiscount = 0m, surcharges = 0m, excludedTaxes = (decimal?)null, specialTreatment = (string?)null, nbs = (string?)null, cClassTrib = "TEST01" } },
            timing = new { supplyPattern = "SingleSupply" },
        }));
        supplier.GetProperty("findings").EnumerateArray().Should().Contain(f => f.GetProperty("code").GetString() == "regime-counterparty-option-unknown");
    }

    [Fact]
    public async Task A_verification_lists_each_obligation_from_the_catalogue_and_a_remediation_is_never_a_verification()
    {
        var admin = await AdminWithProfileAsync();
        var platform = await PlatformAsync();
        await TaxPilotSetup.EnsureObligationAsync(platform, "J-NFE-CAMPOS", "Campos de IBS/CBS na NF-e");
        var free = await Json(await Post(admin, "/api/tax/compliance-assessments", new
        {
            status = "Verified", periodFrom = "2026-07-01", periodTo = "2026-07-31", scope = "estabelecimento", obligationsChecked = NfeOnly,
            evidence = "relatório", assessedBy = "Contabilidade", assessedAt = (string?)null, notes = (string?)null, limitations = (string?)null, pnctParticipation = false,
        }), HttpStatusCode.UnprocessableEntity);
        Code(free).Should().Be("tax_compliance_results", "free text alone is not a verification");
        var unknown = await Json(await Post(admin, "/api/tax/compliance-assessments", new
        {
            status = "Verified", periodFrom = "2026-07-01", periodTo = "2026-07-31", scope = "estabelecimento", obligationsChecked = Array.Empty<string>(),
            evidence = (string?)null, assessedBy = "Contabilidade", assessedAt = (string?)null, notes = (string?)null, limitations = (string?)null, pnctParticipation = false,
            obligations = new[] { new { obligationCode = "NAO-EXISTE", status = "Verified", evidenceReference = "rel" } },
        }), HttpStatusCode.UnprocessableEntity);
        Code(unknown).Should().Be("tax_compliance_obligation_unknown");
        var remediated = await Json(await Post(admin, "/api/tax/compliance-assessments", new
        {
            status = "RemediationVerified", periodFrom = "2026-07-01", periodTo = "2026-07-31", scope = "estabelecimento", obligationsChecked = Array.Empty<string>(),
            evidence = (string?)null, assessedBy = "Contabilidade", assessedAt = (string?)null, notes = (string?)null, limitations = (string?)null, pnctParticipation = false,
            obligations = new[]
            {
                new { obligationCode = "J-NFE-CAMPOS", status = "RemediationVerified", evidenceReference = "NF-e reemitidas", remediation = "Campos preenchidos",
                    remediationVerifiedOn = "2026-07-20", remediationVerifiedBy = "Contabilidade" },
            },
        }));
        remediated.GetProperty("results")[0].GetProperty("obligationName").GetString().Should().Be("Campos de IBS/CBS na NF-e");
        var calc = await Json(await Post(admin, "/api/tax/calculations", Operation(Unique("Compliance"), gross: 2501m, date: "2026-07-15")));
        calc.GetProperty("input").GetProperty("profile").GetProperty("accessoryObligationsCompliant").ValueKind
            .Should().Be(JsonValueKind.Null, "a verified remediation never stands for a verification");
    }

    [Fact]
    public async Task Platform_support_reads_and_suspends_but_only_an_administrator_activates_or_approves()
    {
        var platform = await PlatformAsync();
        var email = $"support-{Guid.NewGuid():N}"[..20] + "@nexo.test";
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
            var hasher = scope.ServiceProvider.GetRequiredService<Nexo.Application.Common.Interfaces.IPasswordHasher>();
            db.PlatformUsers.Add(Nexo.Domain.Entities.PlatformUser.Create(email, hasher.Hash("Support!Test123"), "support"));
            await db.SaveChangesAsync();
        }
        var support = Factory.CreateApiClient();
        var body = await Json(await support.PostAsJsonAsync("/api/platform/auth/login", new { email, password = "Support!Test123" }));
        support.DefaultRequestHeaders.Authorization = new("Bearer", body.GetProperty("accessToken").GetString());

        (await support.GetAsync("/api/platform/tax/readiness")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Post(support, "/api/platform/tax/module/activate", new
        {
            mode = "ControlledPilot", decisionReference = "x", reason = "x", confirmation = "ATIVAR PILOTO CONTROLADO", acknowledgeShadowMode = true,
        })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var versions = await TaxPilotSetup.VersionsAsync(platform);
        (await Post(support, $"/api/platform/tax/rule-versions/{versions["CBS.RATE.2026-TEST v1"].GetProperty("id").GetGuid()}/pilot-approval", new
        {
            scope = "x", validFrom = "2026-01-01", validTo = "2026-12-31", caveats = "x", decisionReference = "x",
        })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await Json(await Post(support, "/api/platform/tax/module/suspend", new { reason = "Suporte suspende num incidente", confirmation = "SUSPENDER" }));
        await TaxPilotSetup.DeactivateAsync(platform);
    }
}
