using System.Net;
using System.Net.Http.Json;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nexo.Application.Features.Users;
using Nexo.Infrastructure.Persistence;
using Nexo.IntegrationTests.Common;
using Nexo.IntegrationTests.Helpers;

namespace Nexo.IntegrationTests.TaxIntelligence;

/// <summary>
/// The homologation example: one operation inside the V1 recorte taken through the whole workflow by
/// the real system — profile, compliance assessment, calculation, detector, requirements, commercial
/// evidence, scenario approval, final fiscal document, execution verification — on a tenant of its own.
/// Every fact is a labelled TEST FIXTURE (company, CNPJ, documents): it proves the workflow, it asserts
/// no legal fact. It runs as the controlled pilot runs — Tax Intelligence enabled, mode ControlledPilot with
/// the approval policy enforced, the pilot opened, the company admitted and the 2026 versions approved, each
/// by an explicit platform action — so the dossier shows the internal pilot approval (with its actor and
/// decision reference) beside the still pending professional review. With TAX_HOMOLOGATION_OUT set, the
/// resulting dossiers are written to that file.
/// </summary>
[Collection("Integration")]
public class TaxHomologationDossierTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    private readonly TestWebApplicationFactory _factory;

    public TaxHomologationDossierTests(TestWebApplicationFactory factory) => _factory = factory;

    private const string Fixture = "FIXTURE DE TESTE — dado controlado, não real";

    private static Task<HttpResponseMessage> Post(HttpClient c, string url, object body) => c.PostAsJsonAsync(url, body, Web);

    private static async Task<JsonElement> Json(HttpResponseMessage r, HttpStatusCode expected = HttpStatusCode.OK)
    {
        var text = await r.Content.ReadAsStringAsync();
        r.StatusCode.Should().Be(expected, text);
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    /// <summary>The same session on a host configured as the controlled pilot, with the approval policy enforced.</summary>
    private HttpClient Pilot(HttpClient authenticated)
    {
        var client = _factory.WithWebHostBuilder(TaxPilotSetup.Enforced("ControlledPilot")).CreateClient();
        client.DefaultRequestHeaders.Authorization = authenticated.DefaultRequestHeaders.Authorization;
        return client;
    }

    private async Task<HttpClient> NewCompanyAsync()
    {
        var email = $"homolog-{Guid.NewGuid():N}"[..20] + "@" + TestCredentials.TestDomain;
        var reg = await _factory.CreateApiClient().PostAsJsonAsync("/api/auth/register",
            new { name = "Empresa de Teste Homologação Ltda", email, password = TestCredentials.AdminPassword });
        reg.StatusCode.Should().Be(HttpStatusCode.OK, await reg.Content.ReadAsStringAsync());
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
            (await db.Users.IgnoreQueryFilters().SingleAsync(u => u.Login == email)).Activate();
            await db.SaveChangesAsync();
        }
        return await AuthClientFactory.LoginAsync(_factory, email, TestCredentials.AdminPassword);
    }

    private async Task<HttpClient> UserAsync(HttpClient admin, string role)
    {
        var login = $"{role[..3]}{Guid.NewGuid():N}"[..20];
        var password = "Tx!" + Guid.NewGuid().ToString("N")[..16];
        var created = await admin.PostAsJsonAsync("/api/users", new CreateUserRequest(
            FullName: "Analista de Teste", Email: $"{login}@{TestCredentials.TestDomain}", Login: login, Password: password,
            Role: role, Phone: null, Notes: null, RequirePasswordChange: false));
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        return await AuthClientFactory.LoginAsync(_factory, login, password);
    }

    [Fact]
    public async Task A_supported_operation_goes_through_the_whole_workflow_into_a_complete_dossier()
    {
        // 1. Company (fiscal issuer) and tax profile — test fixtures.
        var company = await NewCompanyAsync();
        // 0. The operator opens the pilot, admits this company and approves the 2026 versions — explicit, audited actions.
        await TaxPilotSetup.ReadyAsync(_factory, company);
        var director = Pilot(company);
        (await director.PutAsJsonAsync("/api/fiscal/profile", new
        {
            legalName = "Empresa de Teste Homologação Ltda", tradeName = "Loja Teste", cnpj = "11.222.333/0001-81", stateRegistration = "ISENTO",
            municipalRegistration = (string?)null, taxRegime = "LucroPresumido", street = "Rua de Teste", number = "100", complement = (string?)null,
            district = "Centro", cityName = "São Paulo", cityIbgeCode = "3550308", state = "SP", zipCode = "01001000",
        })).StatusCode.Should().Be(HttpStatusCode.OK);
        await Json(await Post(director, "/api/tax/profile/versions", new
        {
            validFrom = "2026-01-01", regime = "LucroPresumido", state = "SP", cityIbgeCode = "3550308", primaryCnae = "4781400",
            secondaryCnaes = Array.Empty<string>(), specialRegimes = Array.Empty<string>(), benefits = Array.Empty<string>(),
            reason = $"Perfil do estabelecimento de teste ({Fixture})",
        }));

        // 2. Ancillary-obligation assessment appropriate to the test scenario (each obligation as a structured result).
        await TaxPilotSetup.EnsureObligationAsync(await TaxPilotSetup.PlatformAsync(_factory), "HOMOLOG-NFE-IBS-CBS",
            "Emissão de NF-e com os campos de IBS/CBS preenchidos (fixture)");
        await Json(await Post(director, "/api/tax/compliance-assessments", new
        {
            obligations = new[]
            {
                new
                {
                    obligationCode = "HOMOLOG-NFE-IBS-CBS", status = "Verified", evidenceReference = $"Relatório HOMOLOG-COMPL-001 ({Fixture})",
                    evidenceFileId = (Guid?)null, remediation = (string?)null, remediationVerifiedOn = (string?)null,
                    remediationVerifiedBy = (string?)null, notes = (string?)null,
                },
            },
            status = "Verified", periodFrom = "2026-06-01", periodTo = "2026-06-30",
            scope = "Estabelecimento de teste — NF-e de saída do período",
            obligationsChecked = new[] { "Emissão de NF-e com os campos de IBS/CBS preenchidos (fixture)" },
            evidence = $"Relatório de conferência HOMOLOG-COMPL-001 ({Fixture})", assessedBy = "Contabilidade de teste", assessedAt = (string?)null,
            notes = (string?)null, limitations = "Dados controlados de teste; não representam uma conferência real.", pnctParticipation = false,
        }));

        // 3. Calculation of a normal sale, single immediate supply, classified, with a conditional discount.
        var calc = await Json(await Post(director, "/api/tax/calculations", new
        {
            operationDate = "2026-06-15", direction = "Outbound", counterpartyRegime = "LucroReal", counterpartyState = "SP",
            description = "Venda de teste — pedido PED-TESTE-001",
            lines = new[]
            {
                new
                {
                    itemKind = "Good", description = "Camiseta de algodão (item de teste)", ncm = "61091000", quantity = 100m, grossAmount = 10000m,
                    unconditionalDiscount = 0m, conditionalDiscount = 500m, surcharges = 0m, excludedTaxes = (decimal?)900m,
                    specialTreatment = (string?)null, nbs = (string?)null, cClassTrib = "TEST01",
                },
            },
            timing = new { supplyPattern = "SingleSupply", documentDate = "2026-06-15", supplyDate = "2026-06-15" },
        }));
        calc.GetProperty("coverage").GetProperty("status").GetString().Should().Be("Supported");
        var calcId = calc.GetProperty("id").GetGuid();

        // 4. Detector.
        var oppId = calc.GetProperty("opportunityIds")[0].GetGuid();

        // 5–6. Analysis by a manager: requirements and the commercial instrument.
        var analyst = Pilot(await UserAsync(director, "gerente"));
        await Json(await Post(analyst, $"/api/tax/opportunities/{oppId}/review", new { }));
        var opp = await Json(await analyst.GetAsync($"/api/tax/opportunities/{oppId}"));
        Guid Req(string code) => opp.GetProperty("requirements").EnumerateArray().Single(r => r.GetProperty("code").GetString() == code).GetProperty("id").GetGuid();
        await Json(await Post(analyst, $"/api/tax/opportunities/{oppId}/evidence", new
        {
            description = "Aditivo ao pedido PED-TESTE-001: preço com desconto fixo, sem condição de pagamento antecipado",
            documentReference = "ADITIVO-TESTE-001", type = "CommercialInstrument", requirementId = Req("commercial-terms"), notes = Fixture,
        }));
        var scenarioNotes = new Dictionary<string, string>
        {
            ["commercial-terms"] = "Aditivo ADITIVO-TESTE-001 assinado antes do fato gerador (fixture).",
            ["condition-removed"] = "Condição removida: desconto dependia de pagamento antecipado (fixture).",
            ["no-later-event"] = "O desconto passa a constar do preço, sem depender de evento posterior (fixture).",
            ["no-recharge"] = "O aditivo não prevê cobrança do valor se a antiga condição não ocorrer (fixture).",
        };
        foreach (var (code, note) in scenarioNotes)
            await Json(await Post(analyst, $"/api/tax/opportunities/{oppId}/requirements/{Req(code)}", new { met = true, note }));

        // 7. Scenario approval by the Diretoria (who did not analyse).
        var approved = await Json(await Post(director, $"/api/tax/opportunities/{oppId}/approve", new { acknowledgeHighRisk = false }));
        approved.GetProperty("status").GetString().Should().Be("Approved");

        // 8. After the operation: final fiscal document (fixture) and final values.
        await Json(await Post(analyst, $"/api/tax/opportunities/{oppId}/evidence", new
        {
            description = "NF-e autorizada com o desconto destacado no documento",
            documentReference = "NFE-TESTE-0001", type = "FiscalDocument", requirementId = Req("on-document"), notes = Fixture,
        }));
        await Json(await Post(analyst, $"/api/tax/opportunities/{oppId}/requirements/{Req("on-document")}",
            new { met = true, note = "Desconto destacado na NF-e NFE-TESTE-0001 (fixture)." }));
        await Json(await Post(analyst, $"/api/tax/opportunities/{oppId}/requirements/{Req("price-reduced")}",
            new { met = true, note = "Valor cobrado conferido com o aditivo (fixture)." }));

        // 9. Execution verification.
        var verified = await Json(await Post(director, $"/api/tax/opportunities/{oppId}/execution", new
        {
            outcome = "Verified", fiscalDocumentReference = "NFE-TESTE-0001", authorizationProtocol = "PROTOCOLO-TESTE-0001",
            notes = $"Execução conferida com documentos de teste ({Fixture}).",
        }));
        verified.GetProperty("status").GetString().Should().Be("ExecutionVerified");

        // 10. The dossiers.
        var decision = verified.GetProperty("decision");
        var scenario = decision.GetProperty("dossier");
        var execution = verified.GetProperty("execution");
        var finalCalc = await Json(await director.GetAsync($"/api/tax/calculations/{calcId}"));

        scenario.GetProperty("taxpayer").GetProperty("cnpj").GetString().Should().Be("11222333000181");
        scenario.GetProperty("establishment").GetProperty("regime").GetString().Should().Be("LucroPresumido");
        scenario.GetProperty("counterparty").GetProperty("regime").GetString().Should().Be("LucroReal");
        scenario.GetProperty("classification")[0].GetProperty("cClassTrib").GetString().Should().Be("TEST01");
        scenario.GetProperty("relevantDates").GetProperty("supplyPattern").GetString().Should().Be("SingleSupply");
        scenario.GetProperty("engineVersion").GetString().Should().Be("orken-tax-engine/1.0.0");
        scenario.GetProperty("canonicalVersion").GetString().Should().Be("orken-canonical/1");
        scenario.GetProperty("facts").GetProperty("inputHash").GetString().Should().HaveLength(64);
        scenario.GetProperty("facts").GetProperty("ruleSetHash").GetString().Should().HaveLength(64);
        scenario.GetProperty("facts").GetProperty("outputHash").GetString().Should().HaveLength(64);
        scenario.GetProperty("rules").GetArrayLength().Should().BeGreaterThan(0);
        scenario.GetProperty("legalSources").EnumerateArray().Should().OnlyContain(s => s.GetProperty("textVerification").GetString() == "Verified");
        scenario.GetProperty("professionalReview").GetProperty("reviews").EnumerateArray()
            .Should().OnlyContain(r => r.GetProperty("status").GetString() == "Pending", "nothing is professionally approved");
        scenario.GetProperty("nature").GetString().Should().StartWith("Registro auditável da decisão e das premissas utilizadas pelo motor");
        var governance = scenario.GetProperty("governance");
        governance.GetProperty("mode").GetString().Should().Be("ControlledPilot");
        governance.GetProperty("enforced").GetBoolean().Should().BeTrue();
        governance.GetProperty("nature").GetString().Should().Be("INTERNAL PRODUCT / GOVERNANCE APPROVAL FOR CONTROLLED PILOT");
        governance.GetProperty("rules").EnumerateArray().Should().OnlyContain(r =>
            r.GetProperty("internalApproval").GetProperty("status").GetString() == "ApprovedForControlledPilot"
            && r.GetProperty("internalApproval").GetProperty("decisionReference").GetString() == TaxPilotSetup.DecisionRecord
            && r.GetProperty("professionalReview").GetProperty("status").GetString() == "Pending");
        governance.GetProperty("effects").GetString().Should().Contain("Não executa alteração fiscal, contábil ou financeira automaticamente");
        execution.GetProperty("dossier").GetProperty("governance").GetProperty("mode").GetString().Should().Be("ControlledPilot");
        scenario.GetProperty("complianceAssessment").GetProperty("status").GetString().Should().Be("Verified");
        scenario.GetProperty("complianceAssessment").GetProperty("scope").GetString().Should().Contain("Estabelecimento de teste");
        var impact = scenario.GetProperty("impactAnalysis");
        impact.GetProperty("nature").GetString().Should().Be("CalculatedTaxReduction");
        impact.GetProperty("baseDifference").GetDecimal().Should().Be(500m);
        impact.GetProperty("calculatedTaxDifference").GetDecimal().Should().Be(5.00m, "1% (CBS 0,9% + IBS 0,1%) of the 500 discount");
        impact.GetProperty("financialEffect").GetString().Should().StartWith("Não determinado");
        scenario.GetProperty("makerChecker").GetProperty("selfApproval").GetBoolean().Should().BeFalse();
        scenario.GetProperty("executionVerification").GetProperty("status").GetString().Should().Be("pending");
        scenario.GetProperty("pendingIssues").GetArrayLength().Should().BeGreaterThan(0);
        execution.GetProperty("dossier").GetProperty("decision").GetProperty("dossierHash").GetString().Should().Be(decision.GetProperty("dossierHash").GetString());
        execution.GetProperty("dossier").GetProperty("canonicalVersion").GetString().Should().Be("orken-canonical/1");

        var artifact = new JsonObject
        {
            ["notice"] = "Exemplo de homologação gerado pelo próprio sistema (teste de integração TaxHomologationDossierTests) sobre dados " +
                         "controlados de teste: empresa, CNPJ, documentos e conferências são fictícios. Prova o fluxo; não afirma fato jurídico. " +
                         "Executado como piloto controlado: as regras têm aprovação interna de produto e governança para o piloto " +
                         "(INTERNAL PRODUCT / GOVERNANCE APPROVAL FOR CONTROLLED PILOT), que não é parecer profissional; a revisão " +
                         "profissional de todas continua pendente. O hash demonstra integridade, não veracidade material.",
            ["calculation"] = new JsonObject
            {
                ["id"] = calcId.ToString(),
                ["engineVersion"] = finalCalc.GetProperty("engineVersion").GetString(),
                ["inputHash"] = finalCalc.GetProperty("inputHash").GetString(),
                ["ruleSetHash"] = finalCalc.GetProperty("ruleSetHash").GetString(),
                ["outputHash"] = finalCalc.GetProperty("outputHash").GetString(),
                ["totalBase"] = finalCalc.GetProperty("totalBase").GetDecimal(),
                ["totalTax"] = finalCalc.GetProperty("totalTax").GetDecimal(),
                ["coverage"] = JsonNode.Parse(finalCalc.GetProperty("coverage").GetRawText()),
                ["governance"] = JsonNode.Parse(finalCalc.GetProperty("governance").GetRawText()),
                ["advisories"] = JsonNode.Parse(finalCalc.GetProperty("advisories").GetRawText()),
                ["notes"] = JsonNode.Parse(finalCalc.GetProperty("output").GetProperty("notes").GetRawText()),
                ["warnings"] = JsonNode.Parse(finalCalc.GetProperty("output").GetProperty("warnings").GetRawText()),
            },
            ["opportunity"] = new JsonObject
            {
                ["title"] = verified.GetProperty("title").GetString(),
                ["type"] = verified.GetProperty("type").GetString(),
                ["impactNature"] = verified.GetProperty("impactNature").GetString(),
                ["status"] = verified.GetProperty("status").GetString(),
                ["requiredApproval"] = verified.GetProperty("requiredApproval").GetString(),
                ["caveats"] = verified.GetProperty("caveats").GetString(),
                ["requirements"] = JsonNode.Parse(verified.GetProperty("requirements").GetRawText()),
                ["evidence"] = JsonNode.Parse(verified.GetProperty("evidence").GetRawText()),
            },
            ["scenarioDecision"] = new JsonObject
            {
                ["dossierHash"] = decision.GetProperty("dossierHash").GetString(),
                ["approvedByName"] = decision.GetProperty("approvedByName").GetString(),
                ["dossier"] = JsonNode.Parse(scenario.GetRawText()),
            },
            ["executionVerification"] = new JsonObject
            {
                ["outcome"] = execution.GetProperty("outcome").GetString(),
                ["dossierHash"] = execution.GetProperty("dossierHash").GetString(),
                ["dossier"] = JsonNode.Parse(execution.GetProperty("dossier").GetRawText()),
            },
        };
        var text = artifact.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        // "economia definitiva" may appear only negated ("não economia definitiva"), never as the name of the reduction.
        System.Text.RegularExpressions.Regex.IsMatch(text, @"(?<!não )economia definitiva", System.Text.RegularExpressions.RegexOptions.IgnoreCase)
            .Should().BeFalse("the reduction is never called a definitive saving");
        text.Should().NotContainEquivalentOf("empresa conforme");

        var output = Environment.GetEnvironmentVariable("TAX_HOMOLOGATION_OUT");
        if (!string.IsNullOrWhiteSpace(output)) await File.WriteAllTextAsync(output, text);
    }
}
