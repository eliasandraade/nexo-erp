using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
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
/// The tax suites with their own database run alone (never beside another host being built): the API's bootstrap logger is
/// process-wide, and two hosts starting at once would both try to freeze it.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class TaxIsolatedCollection
{
    public const string Name = "TaxIsolated";
}

/// <summary>
/// Base of the tax suites that change platform-wide state (module activation and suspension, fictitious professional approvals of the
/// catalogue): each derived class gets its OWN database (class fixture), so nothing it does leaks into the shared suite, which checks
/// that the seeded catalogue stays Pending and the module inactive.
/// </summary>
public abstract class TaxIsolatedSuite : IClassFixture<TestWebApplicationFactory>
{
    protected static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    protected readonly TestWebApplicationFactory Factory;

    protected TaxIsolatedSuite(TestWebApplicationFactory factory) => Factory = factory;

    protected static Task<HttpResponseMessage> Post(HttpClient c, string url, object body) => c.PostAsJsonAsync(url, body, Web);
    protected static Task<HttpResponseMessage> Put(HttpClient c, string url, object body) => c.PutAsJsonAsync(url, body, Web);

    protected static async Task<JsonElement> Json(HttpResponseMessage r, HttpStatusCode expected = HttpStatusCode.OK)
    {
        var text = await r.Content.ReadAsStringAsync();
        r.StatusCode.Should().Be(expected, text);
        return string.IsNullOrEmpty(text) ? default : JsonDocument.Parse(text).RootElement.Clone();
    }

    protected static string Code(JsonElement error) => error.GetProperty("code").GetString()!;

    protected static string Unique(string prefix) => $"{prefix} {Guid.NewGuid():N}"[..Math.Min(prefix.Length + 13, 60)];

    protected static string Sha256(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    /// <summary>A host with the given configuration and the token of an authenticated client.</summary>
    protected HttpClient Host(HttpClient authenticated, Action<IWebHostBuilder> host)
    {
        var client = Factory.WithWebHostBuilder(host).CreateClient();
        client.DefaultRequestHeaders.Authorization = authenticated.DefaultRequestHeaders.Authorization;
        return client;
    }

    protected Task<HttpClient> PlatformAsync(string mode = "ControlledPilot")
        => TaxPilotSetup.PlatformAsync(Factory.WithWebHostBuilder(TaxPilotSetup.Enforced(mode)));

    /// <summary>The company's director with a Lucro Presumido profile (regular regime — inside the matrix).</summary>
    protected async Task<HttpClient> AdminWithProfileAsync(string regime = "LucroPresumido")
    {
        var admin = await AuthClientFactory.LoginAsAdminAsync(Factory);
        var profile = await Json(await admin.GetAsync("/api/tax/profile"));
        if (profile.GetProperty("current").ValueKind == JsonValueKind.Null)
            await Json(await Post(admin, "/api/tax/profile/versions", new
            {
                validFrom = "2026-01-01", regime, state = "SP", cityIbgeCode = "3550308", primaryCnae = "4711302",
                secondaryCnaes = Array.Empty<string>(), specialRegimes = Array.Empty<string>(), benefits = Array.Empty<string>(),
                reason = "Perfil de teste",
            }));
        return admin;
    }

    protected async Task<HttpClient> UserAsync(HttpClient admin, string role)
    {
        var login = $"{role[..3]}{Guid.NewGuid():N}"[..20];
        var password = "Tx!" + Guid.NewGuid().ToString("N")[..16];
        var created = await admin.PostAsJsonAsync("/api/users", new CreateUserRequest(
            FullName: $"Pessoa {role}", Email: $"{login}@{TestCredentials.TestDomain}", Login: login, Password: password,
            Role: role, Phone: null, Notes: null, RequirePasswordChange: false));
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        return await AuthClientFactory.LoginAsync(Factory, login, password);
    }

    /// <summary>A second company (its own tenant) with a director already signed in.</summary>
    protected async Task<HttpClient> OtherCompanyAsync()
    {
        var email = $"tax-{Guid.NewGuid():N}"[..16] + "@" + TestCredentials.TestDomain;
        var reg = await Factory.CreateApiClient().PostAsJsonAsync("/api/auth/register",
            new { name = "Outra Empresa", email, password = TestCredentials.AdminPassword });
        reg.StatusCode.Should().Be(HttpStatusCode.OK, await reg.Content.ReadAsStringAsync());
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
            var user = await db.Users.IgnoreQueryFilters().SingleAsync(u => u.Login == email);
            user.Activate();
            await db.SaveChangesAsync();
        }
        return await AuthClientFactory.LoginAsync(Factory, email, TestCredentials.AdminPassword);
    }

    /// <summary>A single immediate supply of a classified good (fictitious cClassTrib TEST01).</summary>
    protected static object Operation(string description, decimal gross = 1000m, string date = "2026-06-15", decimal conditional = 50m,
        string? cClassTrib = TestWebApplicationFactory.TestCClassTrib, string direction = "Outbound", string? counterpartyRegime = null)
        => new
        {
            operationDate = date, direction, counterpartyRegime, counterpartyState = (string?)null, description,
            counterpartyRegularRegimeOption = counterpartyRegime == "SimplesNacional" ? false : (bool?)null,
            lines = new[]
            {
                new
                {
                    itemKind = "Good", description = "Produto", ncm = "61091000", quantity = 1m, grossAmount = gross,
                    unconditionalDiscount = 100m, conditionalDiscount = conditional, surcharges = 20m, excludedTaxes = (decimal?)80m,
                    specialTreatment = (string?)null, nbs = (string?)null, cClassTrib,
                },
            },
            timing = new { supplyPattern = "SingleSupply" },
        };

    /// <summary>A verified legal source and an active rule (a note) valid from <paramref name="from"/> to <paramref name="to"/>.</summary>
    protected static async Task<(string Key, Guid VersionId)> RuleAsync(HttpClient platform, string from, string to)
    {
        var tag = Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();
        var source = $"TEST-FIN-{tag}";
        await Json(await Post(platform, "/api/platform/tax/legal-sources", new
        {
            code = source, kind = "Other", title = "Fonte de teste", reference = "art. 1", url = "https://www.planalto.gov.br/teste",
            excerpt = "Texto literal de teste usado apenas pela suíte de integração.", publishedOn = (string?)null, notes = (string?)null,
        }));
        await Json(await Post(platform, $"/api/platform/tax/legal-sources/{source}/verify", new { verifiedBy = "suite" }));
        var key = $"TEST.FIN.{tag}";
        await Json(await Post(platform, "/api/platform/tax/rules", new { key, name = "Nota de teste", description = "Só para testes." }));
        var id = (await Json(await Post(platform, $"/api/platform/tax/rules/{key}/versions", new
        {
            validFrom = from, validTo = to, priority = 5, conditions = Array.Empty<object>(),
            result = new { kind = "note", code = "TEST-FIN", text = "Nota de teste." }, legalSourceCodes = new[] { source }, notes = (string?)null,
        }))).GetProperty("id").GetGuid();
        await Json(await Post(platform, $"/api/platform/tax/rule-versions/{id}/status", new { status = "Active" }));
        return (key, id);
    }

    /// <summary>
    /// A FICTITIOUS professional review, recorded only in this test database to exercise the definitive mode. The professional is a
    /// test placeholder; no such review exists outside the suite.
    /// </summary>
    protected static async Task<JsonElement> ReviewAsync(HttpClient platform, Guid versionId, string status, string? from = "2026-01-01",
        string? to = "2026-12-31", string[]? regimes = null, string? caveats = null, string? notes = null)
        => await Json(await Post(platform, $"/api/platform/tax/rule-versions/{versionId}/professional-review", new
        {
            status, reviewer = "Profissional fictício da suíte de testes", notes = notes ?? "Revisão fictícia de teste.", professionalReference = (string?)null,
            scope = "Recorte de teste", validFrom = from, validTo = to, assumptions = (string?)null, caveats,
            coveredRegimes = regimes ?? ["LucroPresumido", "LucroReal"], reviewedOn = "2026-01-02", supportingDocumentId = (Guid?)null,
        }));

    /// <summary>Uploads evidence with a file to an opportunity.</summary>
    protected static async Task<JsonElement> UploadEvidenceAsync(HttpClient c, Guid oppId, byte[] bytes, string fileName, string type, Guid? requirementId,
        string description, HttpStatusCode expected = HttpStatusCode.OK)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(file, "file", fileName);
        form.Add(new StringContent(type), "type");
        if (requirementId is not null) form.Add(new StringContent(requirementId.Value.ToString()), "requirementId");
        form.Add(new StringContent(description), "description");
        form.Add(new StringContent("REF-" + Guid.NewGuid().ToString("N")[..8]), "documentReference");
        return await Json(await c.PostAsync($"/api/tax/opportunities/{oppId}/evidence/files", form), expected);
    }

    /// <summary>A minimal well-formed PDF (its bytes start with %PDF-).</summary>
    protected static byte[] Pdf(string marker) => System.Text.Encoding.ASCII.GetBytes(
        $"%PDF-1.4\n% {marker}\n1 0 obj<</Type/Catalog/Pages 2 0 R>>endobj\n2 0 obj<</Type/Pages/Count 0/Kids[]>>endobj\ntrailer<</Root 1 0 R>>\n%%EOF\n");

    /// <summary>Opens the review, answers every scenario requirement and links a commercial instrument (with a file) where asked.</summary>
    protected static async Task PrepareScenarioAsync(HttpClient c, Guid oppId)
    {
        await Json(await Post(c, $"/api/tax/opportunities/{oppId}/review", new { }));
        var opp = await Json(await c.GetAsync($"/api/tax/opportunities/{oppId}"));
        var reqs = opp.GetProperty("requirements").EnumerateArray().Where(r => r.GetProperty("phase").GetString() == "ScenarioApproval").ToList();
        var commercial = reqs.Single(r => r.GetProperty("requiredEvidence").GetString() == "CommercialInstrument").GetProperty("id").GetGuid();
        await UploadEvidenceAsync(c, oppId, Pdf("proposta"), "proposta-comercial.pdf", "CommercialInstrument", commercial, "Proposta comercial sem condição");
        foreach (var r in reqs)
            await Json(await Post(c, $"/api/tax/opportunities/{oppId}/requirements/{r.GetProperty("id").GetGuid()}", new { met = true, note = "conferido" }));
    }

    protected async Task<int> CountAsync(Func<NexoDbContext, IQueryable<object>> query)
    {
        using var scope = Factory.Services.CreateScope();
        return await query(scope.ServiceProvider.GetRequiredService<NexoDbContext>()).CountAsync();
    }
}
