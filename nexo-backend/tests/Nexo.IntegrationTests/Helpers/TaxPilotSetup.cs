using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Nexo.IntegrationTests.Common;

namespace Nexo.IntegrationTests.Helpers;

/// <summary>
/// Sets up the controlled pilot the way an operator does — by explicit, audited platform actions: open the
/// pilot window, admit a tenant, approve rule versions with a decision reference. Nothing is approved by a
/// migration or a seed; every test that needs the pilot does this itself.
/// </summary>
public static class TaxPilotSetup
{
    public const string DecisionRecord = "docs/tax-intelligence/homologation/internal-pilot-approval-2026.md";

    /// <summary>The nine versions active in 2026 (key vN).</summary>
    public static readonly string[] CatalogVersions =
    [
        "CBS.RATE.2026-TEST v1", "IBS.RATE.2026-TEST-STATE v1", "IBSCBS.BASE.CONDITIONAL-DISCOUNT v1", "IBSCBS.BASE.SURCHARGES v1",
        "IBSCBS.BASE.TRANSITION-TAXES v1", "IBSCBS.BASE.UNCONDITIONAL-DISCOUNT v1", "IBSCBS.NOTE.2026-COMPENSATION v1",
        "IBSCBS.NOTE.2026-SIMPLES v1", "IBSCBS.NOTE.2026-WAIVER v2",
    ];

    /// <summary>A host with Tax Intelligence on, the given mode and approval enforced for calculations and decisions.</summary>
    public static Action<IWebHostBuilder> Enforced(string mode) => b => b
        .UseSetting("TaxIntelligence:Policy:Enabled", "true")
        .UseSetting("TaxIntelligence:Policy:Mode", mode)
        .UseSetting("TaxIntelligence:Policy:RequireApprovedRulesForDecisions", "true")
        .UseSetting("TaxIntelligence:Policy:RequireApprovedRulesForCalculations", "true");

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    public static async Task<JsonElement> Json(HttpResponseMessage r, HttpStatusCode expected = HttpStatusCode.OK)
    {
        var text = await r.Content.ReadAsStringAsync();
        r.StatusCode.Should().Be(expected, text);
        return string.IsNullOrEmpty(text) ? default : JsonDocument.Parse(text).RootElement.Clone();
    }

    public static async Task<HttpClient> PlatformAsync(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var body = await Json(await client.PostAsJsonAsync("/api/platform/auth/login",
            new { email = TestCredentials.PlatformEmail, password = TestCredentials.PlatformPassword }));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body.GetProperty("accessToken").GetString());
        return client;
    }

    /// <summary>The tenant and active store of an authenticated client, read from its token.</summary>
    public static (Guid Tenant, Guid Store) Identity(HttpClient c)
    {
        var payload = c.DefaultRequestHeaders.Authorization!.Parameter!.Split('.')[1].Replace('-', '+').Replace('_', '/');
        payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
        var claims = JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(payload))).RootElement;
        return (Guid.Parse(claims.GetProperty("tenantId").GetString()!), Guid.Parse(claims.GetProperty("storeId").GetString()!));
    }

    public static async Task<JsonElement> OpenPilotAsync(HttpClient platform, string from = "2026-01-01", string? to = null, bool enabled = true)
        => await Json(await platform.PutAsJsonAsync("/api/platform/tax/pilot",
            new { enabled, validFrom = from, validTo = to, decisionReference = DecisionRecord }, Web));

    /// <summary>Admits a tenant (or one store); already admitted is fine.</summary>
    public static async Task AdmitAsync(HttpClient platform, Guid tenant, Guid? store = null)
    {
        var r = await platform.PostAsJsonAsync("/api/platform/tax/pilot/participants", new { tenantId = tenant, storeId = store }, Web);
        if (r.StatusCode == HttpStatusCode.UnprocessableEntity
            && (await Json(r, HttpStatusCode.UnprocessableEntity)).GetProperty("code").GetString() == "tax_pilot_participant_duplicate") return;
        await Json(r);
    }

    /// <summary>An ancillary obligation of the platform catalogue (created once; later calls reuse it).</summary>
    public static async Task EnsureObligationAsync(HttpClient platform, string code, string name)
    {
        var r = await platform.PostAsJsonAsync("/api/platform/tax/obligations",
            new { code, name, description = "Obrigação da suíte de testes.", reference = "Fixture de teste" }, Web);
        if (r.StatusCode == HttpStatusCode.Conflict) return;
        await Json(r);
    }

    /// <summary>The slug the suite uses as obligation code for a name.</summary>
    public static string ObligationCode(string name)
        => new string(name.ToUpperInvariant().Select(ch => char.IsAsciiLetterOrDigit(ch) ? ch : '-').ToArray()).Trim('-')[..Math.Min(40, name.Length)].Trim('-');

    /// <summary>Every rule version, by "key vN".</summary>
    public static async Task<Dictionary<string, JsonElement>> VersionsAsync(HttpClient platform)
        => (await Json(await platform.GetAsync("/api/platform/tax/rules"))).EnumerateArray()
            .SelectMany(r => r.GetProperty("versions").EnumerateArray()
                .Select(v => (Label: $"{r.GetProperty("key").GetString()} v{v.GetProperty("version").GetInt32()}", V: v)))
            .ToDictionary(x => x.Label, x => x.V);

    public static async Task<JsonElement> ApproveAsync(HttpClient platform, Guid versionId, string from, string to, string reference,
        string caveats = "Ressalvas aceitas na decisão interna (teste).")
        => await Json(await platform.PostAsJsonAsync($"/api/platform/tax/rule-versions/{versionId}/pilot-approval", new
        {
            scope = "Recorte suportado da v1 em piloto controlado (teste de integração)", validFrom = from, validTo = to, caveats,
            decisionReference = reference,
        }, Web));

    /// <summary>Approves the nine 2026 versions for 2026, explicitly, under the given decision reference.</summary>
    public static async Task ApproveCatalogAsync(HttpClient platform, string reference = DecisionRecord)
    {
        var versions = await VersionsAsync(platform);
        foreach (var label in CatalogVersions)
            await ApproveAsync(platform, versions[label].GetProperty("id").GetGuid(), "2026-01-01", "2026-12-31", reference);
    }

    /// <summary>Pilot open, the client's tenant admitted, the 2026 catalogue approved and the pilot activated by the platform.</summary>
    public static async Task ReadyAsync(TestWebApplicationFactory factory, HttpClient tenantClient)
    {
        var platform = await PlatformAsync(factory.WithWebHostBuilder(Enforced("ControlledPilot")));
        await OpenPilotAsync(platform);
        await AdmitAsync(platform, Identity(tenantClient).Tenant);
        await ApproveCatalogAsync(platform);
        await ActivateAsync(platform);
    }

    /// <summary>Activates the configured mode the way an operator does: checklist satisfied, exact phrase, shadow mode acknowledged.</summary>
    public static async Task<JsonElement> ActivateAsync(HttpClient platform, string mode = "ControlledPilot")
        => await Json(await platform.PostAsJsonAsync("/api/platform/tax/module/activate", new
        {
            mode, decisionReference = DecisionRecord, reason = "Ativação pela suíte de integração.",
            confirmation = mode == "ControlledPilot" ? "ATIVAR PILOTO CONTROLADO" : "ATIVAR MODO DEFINITIVO", acknowledgeShadowMode = true,
        }, Web));

    /// <summary>Back to inactive, so a test that suspended or activated leaves no state for the next one.</summary>
    public static async Task DeactivateAsync(HttpClient platform)
        => await Json(await platform.PostAsJsonAsync("/api/platform/tax/module/deactivate",
            new { reason = "Fim do teste.", confirmation = "DESATIVAR" }, Web));

    /// <summary>
    /// Test fixture: records the module as active in a mode without going through the checklist — a definitive mode activated
    /// earlier, before a rule lost (or never had) its professional review — so the per-rule gate itself is under test.
    /// </summary>
    public static async Task ForceActivateAsync(WebApplicationFactory<Program> factory, Nexo.Domain.Modules.TaxIntelligence.TaxApprovalMode mode)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Nexo.Infrastructure.Persistence.NexoDbContext>();
        db.TaxModuleActivations.Add(Nexo.Domain.Modules.TaxIntelligence.TaxModuleActivation.Activate(mode, DecisionRecord,
            "Fixture da suíte de testes.", Guid.NewGuid(), "suite", "Platform"));
        await db.SaveChangesAsync();
    }

    /// <summary>An active note rule on one day, with a verified source. Returns its key and version id.</summary>
    public static async Task<(string Key, Guid VersionId)> NoteRuleAsync(HttpClient platform, string day)
    {
        var tag = Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();
        var source = $"TEST-PILOT-{tag}";
        await Json(await platform.PostAsJsonAsync("/api/platform/tax/legal-sources", new
        {
            code = source, kind = "Other", title = "Fonte de teste", reference = "art. 1", url = "https://www.planalto.gov.br/teste",
            excerpt = "Texto literal de teste usado apenas pela suíte de integração.", publishedOn = (string?)null, notes = (string?)null,
        }, Web));
        await Json(await platform.PostAsJsonAsync($"/api/platform/tax/legal-sources/{source}/verify", new { verifiedBy = "suite" }, Web));
        var key = $"TEST.PILOT.{tag}";
        await Json(await platform.PostAsJsonAsync("/api/platform/tax/rules", new { key, name = "Nota de teste", description = "Só para testes." }, Web));
        var id = await NewVersionAsync(platform, key, day, source);
        return (key, id);
    }

    public static async Task<Guid> NewVersionAsync(HttpClient platform, string key, string day, string? source = null)
    {
        source ??= (await VersionsAsync(platform)).First(x => x.Key.StartsWith(key + " ")).Value
            .GetProperty("legalSourceCodes")[0].GetString();
        var id = (await Json(await platform.PostAsJsonAsync($"/api/platform/tax/rules/{key}/versions", new
        {
            validFrom = day, validTo = day, priority = 5, conditions = Array.Empty<object>(),
            result = new { kind = "note", code = "TEST-PILOT", text = "Nota de teste." }, legalSourceCodes = new[] { source }, notes = (string?)null,
        }, Web))).GetProperty("id").GetGuid();
        await Json(await platform.PostAsJsonAsync($"/api/platform/tax/rule-versions/{id}/status", new { status = "Active" }, Web));
        return id;
    }
}
