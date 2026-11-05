using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Extensions.DependencyInjection;
using Nexo.Application.Common.Interfaces;
using Nexo.Domain.Entities;
using Nexo.Infrastructure.Persistence;
using Nexo.IntegrationTests.Common;
using Nexo.IntegrationTests.Helpers;

namespace Nexo.IntegrationTests.Security;

/// <summary>
/// Writes on the platform API are administrator acts (policy PlatformAdmin: super_admin and, when required, a
/// two-step sign-in). A support user reads the tax catalogue but never changes it. Own database: platform users
/// and the pilot scope are platform-wide state.
/// </summary>
[Collection(PlatformIsolatedCollection.Name)]
public sealed class PlatformWriteAuthorizationTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public PlatformWriteAuthorizationTests(TestWebApplicationFactory factory) => _factory = factory;

    /// <summary>
    /// Writes under /api/platform that a support user may do on purpose, with the reason. Everything else that is not a
    /// GET must require PlatformAdmin. Adding a name here is a security decision, never a way to make the test pass.
    /// </summary>
    private static readonly Dictionary<string, string> NonAdminWritesByDesign = new()
    {
        ["PlatformAuth.Logout"] = "encerra a própria sessão",
        ["PlatformAuth.StartMfaSetup"] = "verificação em duas etapas da própria conta",
        ["PlatformAuth.EnableMfa"] = "verificação em duas etapas da própria conta",
        ["PlatformAuth.DisableMfa"] = "verificação em duas etapas da própria conta (exige senha e código)",
        ["PlatformAuth.RevokeSession"] = "encerra uma sessão da própria conta",
        ["PlatformAuth.RevokeAll"] = "encerra as sessões da própria conta",
        ["Platform.CreateNote"] = "nota interna de atendimento sobre a empresa, auditada; não muda o que a empresa pode fazer",
        ["Platform.TogglePin"] = "fixa uma nota interna, auditado; não muda o que a empresa pode fazer",
        // Decisão pendente do dono (PR #85): hoje o freio de emergência fica aberto a todo operador da plataforma.
        ["PlatformTaxModule.Suspend"] = "freio de emergência da inteligência tributária: só interrompe trabalho novo na hora (frase SUSPENDER, "
            + "auditado), nunca libera nada; ativar, reativar e desativar continuam exigindo super admin",
    };

    /// <summary>
    /// Every admin write under /api/platform/tax (catalogue, pilot, module, documents, classifications, obligations,
    /// retention), with a body that gets past model binding, so a refusal can only come from the policy.
    /// </summary>
    private static readonly Dictionary<string, Func<Seed, (string Path, object Body)>> CatalogueWrites = new()
    {
        ["POST api/platform/tax/legal-sources"] = s => ("/api/platform/tax/legal-sources", new
        {
            code = $"TEST-AUTHZ-{s.Tag}-NEW", kind = "Other", title = "Fonte de teste", reference = "art. 1", url = "https://www.planalto.gov.br/teste",
            excerpt = "Texto literal de teste usado apenas pela suíte de integração.", publishedOn = (string?)null, notes = (string?)null,
        }),
        ["POST api/platform/tax/legal-sources/{code}/verify"] = s => ($"/api/platform/tax/legal-sources/{s.SourceCode}/verify", new { verifiedBy = "suporte" }),
        ["POST api/platform/tax/rules"] = s => ("/api/platform/tax/rules", new { key = $"TEST.AUTHZ.{s.Tag}.NEW", name = "Regra", description = "Teste." }),
        ["POST api/platform/tax/rules/{key}/versions"] = s => ($"/api/platform/tax/rules/{s.RuleKey}/versions", new
        {
            validFrom = "2026-05-05", validTo = "2026-05-05", priority = 5, conditions = Array.Empty<object>(),
            result = new { kind = "note", code = "TEST-AUTHZ", text = "Nota de teste." }, legalSourceCodes = new[] { s.SourceCode }, notes = (string?)null,
        }),
        ["POST api/platform/tax/rule-versions/{id:guid}/status"] = s => ($"/api/platform/tax/rule-versions/{s.VersionId}/status", new { status = "Retired" }),
        ["POST api/platform/tax/rule-versions/{id:guid}/professional-review"] = s => ($"/api/platform/tax/rule-versions/{s.VersionId}/professional-review",
            new { status = "Approved", reviewer = "Suporte", notes = (string?)null }),
        ["POST api/platform/tax/rule-versions/{id:guid}/pilot-approval"] = s => ($"/api/platform/tax/rule-versions/{s.VersionId}/pilot-approval", new
        {
            scope = "Teste de autorização", validFrom = "2026-01-01", validTo = "2026-12-31", caveats = "Nenhuma.", decisionReference = $"DEC-{s.Tag}",
        }),
        ["POST api/platform/tax/rule-versions/{id:guid}/pilot-approval/withdraw"] = s => ($"/api/platform/tax/rule-versions/{s.VersionId}/pilot-approval/withdraw",
            new { reason = "Teste de autorização" }),
        ["PUT api/platform/tax/pilot"] = s => ("/api/platform/tax/pilot",
            new { enabled = true, validFrom = "2026-01-01", validTo = (string?)null, decisionReference = $"DEC-{s.Tag}" }),
        ["POST api/platform/tax/pilot/participants"] = s => ("/api/platform/tax/pilot/participants", new { tenantId = s.TenantId, storeId = (Guid?)null }),
        ["POST api/platform/tax/pilot/participants/{id:guid}/remove"] = s => ($"/api/platform/tax/pilot/participants/{Guid.NewGuid()}/remove",
            new { reason = "Teste de autorização" }),
        // The module: the host configures no mode, so even an admin activation is refused by the checklist (never by the policy).
        ["POST api/platform/tax/module/activate"] = s => ("/api/platform/tax/module/activate", new
        {
            mode = "ControlledPilot", decisionReference = $"DEC-{s.Tag}", reason = "Teste de autorização",
            confirmation = "ATIVAR PILOTO CONTROLADO", acknowledgeShadowMode = true,
        }),
        ["POST api/platform/tax/module/deactivate"] = s => ("/api/platform/tax/module/deactivate",
            new { reason = "Teste de autorização", confirmation = "DESATIVAR" }),
        // Multipart: the upload only accepts form data, and any other content type would be refused before authorization.
        ["POST api/platform/tax/documents"] = s => ("/api/platform/tax/documents", PdfForm(s.Tag)),
        ["POST api/platform/tax/documents/{id:guid}/revoke"] = s => ($"/api/platform/tax/documents/{Guid.NewGuid()}/revoke",
            new { reason = "Teste de autorização" }),
        ["POST api/platform/tax/classifications/import"] = s => ("/api/platform/tax/classifications/import", new
        {
            kind = "CClassTrib", source = "Fonte fictícia da suíte de testes", sourceVersion = (string?)null, sourceDocumentId = (Guid?)null,
            items = new[] { new { code = $"AZ{s.Tag[..4]}", description = "Código fictício", validFrom = "2026-01-01", validTo = (string?)null, notes = (string?)null } },
        }),
        ["POST api/platform/tax/classifications/{id:guid}/status"] = s => ($"/api/platform/tax/classifications/{Guid.NewGuid()}/status",
            new { status = "Active", reason = "Teste de autorização" }),
        ["POST api/platform/tax/classifications/{id:guid}/professional-review"] = s => ($"/api/platform/tax/classifications/{Guid.NewGuid()}/professional-review",
            new { status = "Approved", notes = "Teste de autorização" }),
        ["POST api/platform/tax/obligations"] = s => ("/api/platform/tax/obligations",
            new { code = $"OBR-{s.Tag}", name = "Obrigação de teste", description = (string?)null, reference = "art. 1" }),
        ["POST api/platform/tax/obligations/{id:guid}/active"] = s => ($"/api/platform/tax/obligations/{Guid.NewGuid()}/active", new { active = false }),
        ["PUT api/platform/tax/retention-policies"] = s => ("/api/platform/tax/retention-policies", new
        {
            evidenceType = (string?)null, retentionMonths = 60, startsAt = "UploadDate", enabled = true, allowTenantOverride = false,
            rationale = "Teste de autorização", reference = $"DEC-{s.Tag}",
        }),
    };

    /// <summary>A minimal PDF as the upload form (a fresh content per request: an HttpContent is sent once).</summary>
    private static MultipartFormDataContent PdfForm(string tag)
    {
        var file = new ByteArrayContent("%PDF-1.4\n%%EOF\n"u8.ToArray());
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        return new MultipartFormDataContent
        {
            { file, "file", $"authz-{tag}.pdf" },
            { new StringContent("Teste de autorização"), "purpose" },
        };
    }

    public static IEnumerable<object[]> CatalogueWriteRoutes => CatalogueWrites.Keys.Select(k => new object[] { k });

    private static readonly string[] CatalogueReads =
    [
        "/api/platform/tax/rules", "/api/platform/tax/legal-sources", "/api/platform/tax/pilot", "/api/platform/tax/module",
        "/api/platform/tax/documents", "/api/platform/tax/classifications", "/api/platform/tax/obligations", "/api/platform/tax/retention-policies",
    ];

    private sealed record Seed(string Tag, string SourceCode, string RuleKey, Guid VersionId, Guid TenantId);

    // ── helpers ─────────────────────────────────────────────────────────────────

    private static async Task<JsonElement> Json(HttpResponseMessage r, HttpStatusCode expected = HttpStatusCode.OK)
    {
        var body = await r.Content.ReadAsStringAsync();
        r.StatusCode.Should().Be(expected, body);
        return body.Length == 0 ? default : JsonDocument.Parse(body).RootElement.Clone();
    }

    private async Task<string> NewPlatformUserAsync(string role, string password)
    {
        var email = $"authz-{Guid.NewGuid():N}"[..16] + "@platform.test";
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        db.PlatformUsers.Add(PlatformUser.Create(email, scope.ServiceProvider.GetRequiredService<IPasswordHasher>().Hash(password), role));
        await db.SaveChangesAsync();
        return email;
    }

    /// <summary>A signed-in platform client with a fresh account of the given role (a new random password on each run).</summary>
    private async Task<HttpClient> PlatformClientAsync(string role, Func<HttpClient>? create = null)
    {
        create ??= _factory.CreateApiClient;
        var password = "Px!" + Guid.NewGuid().ToString("N")[..20];
        var email = await NewPlatformUserAsync(role, password);
        var client = create();
        var login = await Json(await client.PostAsJsonAsync("/api/platform/auth/login", new { email, password }));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.GetProperty("accessToken").GetString());
        return client;
    }

    private static Task<HttpResponseMessage> Send(HttpClient c, string route, Seed seed)
    {
        var (path, body) = CatalogueWrites[route](seed);
        if (body is HttpContent content) return c.PostAsync(path, content);
        return route.StartsWith("PUT ", StringComparison.Ordinal) ? c.PutAsJsonAsync(path, body) : c.PostAsJsonAsync(path, body);
    }

    /// <summary>An unverified source, a rule and a draft version (real records to aim at) and the seeded tenant.</summary>
    private async Task<Seed> SeedAsync(HttpClient admin)
    {
        var tag = Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();
        var source = $"TEST-AUTHZ-{tag}";
        await Json(await admin.PostAsJsonAsync("/api/platform/tax/legal-sources", new
        {
            code = source, kind = "Other", title = "Fonte de teste", reference = "art. 1", url = "https://www.planalto.gov.br/teste",
            excerpt = "Texto literal de teste usado apenas pela suíte de integração.", publishedOn = (string?)null, notes = (string?)null,
        }));
        var key = $"TEST.AUTHZ.{tag}";
        await Json(await admin.PostAsJsonAsync("/api/platform/tax/rules", new { key, name = "Regra de teste", description = "Só para testes." }));
        var version = (await Json(await admin.PostAsJsonAsync($"/api/platform/tax/rules/{key}/versions", new
        {
            validFrom = "2026-04-04", validTo = "2026-04-04", priority = 5, conditions = Array.Empty<object>(),
            result = new { kind = "note", code = "TEST-AUTHZ", text = "Nota de teste." }, legalSourceCodes = new[] { source }, notes = (string?)null,
        }))).GetProperty("id").GetGuid();

        var tenantAdmin = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var tenantId = Guid.Parse((await Json(await tenantAdmin.GetAsync("/api/auth/me"))).GetProperty("tenantId").GetString()!);
        return new Seed(tag, source, key, version, tenantId);
    }

    /// <summary>The catalogue, pilot, module and references as the platform shows them: any change made by a write shows up here.</summary>
    private static async Task<string> SnapshotAsync(HttpClient c)
    {
        var parts = new List<string>();
        foreach (var path in CatalogueReads) parts.Add(await (await c.GetAsync(path)).Content.ReadAsStringAsync());
        return string.Join("\n", parts);
    }

    private static IEnumerable<(string Name, string Route, bool Anonymous, IReadOnlyList<string?> Policies)> PlatformWrites(IServiceProvider services)
        => services.GetRequiredService<IActionDescriptorCollectionProvider>().ActionDescriptors.Items
            .OfType<ControllerActionDescriptor>()
            .Where(d => d.AttributeRouteInfo?.Template?.StartsWith("api/platform", StringComparison.OrdinalIgnoreCase) == true)
            .SelectMany(d => d.MethodInfo.GetCustomAttributes<HttpMethodAttribute>(true).SelectMany(h => h.HttpMethods)
                .Where(m => m != "GET" && m != "HEAD")
                .Select(m =>
                {
                    var attributes = d.ControllerTypeInfo.GetCustomAttributes(true).Concat(d.MethodInfo.GetCustomAttributes(true)).ToList();
                    return ($"{d.ControllerName}.{d.ActionName}", $"{m} {d.AttributeRouteInfo!.Template}",
                        attributes.OfType<AllowAnonymousAttribute>().Any(),
                        (IReadOnlyList<string?>)attributes.OfType<AuthorizeAttribute>().Select(a => a.Policy).ToList());
                }));

    // ── tests ───────────────────────────────────────────────────────────────────

    [Fact]
    public void Every_platform_write_requires_PlatformAdmin_unless_it_is_a_listed_self_service_or_support_act()
    {
        var writes = PlatformWrites(_factory.Services).Where(w => !w.Anonymous).ToList();
        writes.Should().NotBeEmpty();

        writes.Where(w => !w.Policies.Contains("PlatformAdmin") && !NonAdminWritesByDesign.ContainsKey(w.Name))
            .Select(w => $"{w.Name} ({w.Route})")
            .Should().BeEmpty("a platform write changes what tenants can do unless it is deliberately listed as self-service or support");

        writes.Where(w => w.Policies.Contains("PlatformAdmin")).Select(w => w.Name).Should().NotIntersectWith(NonAdminWritesByDesign.Keys,
            "the list of non-admin writes must not hide routes that are already admin-only");
    }

    [Fact]
    public void The_route_list_below_covers_every_write_of_the_tax_catalogue()
    {
        PlatformWrites(_factory.Services)
            .Where(w => w.Route.Split(' ')[1].StartsWith("api/platform/tax", StringComparison.OrdinalIgnoreCase) && !NonAdminWritesByDesign.ContainsKey(w.Name))
            .Select(w => w.Route)
            .Should().BeEquivalentTo(CatalogueWrites.Keys, "each catalogue write is exercised against support below");
    }

    [Theory]
    [MemberData(nameof(CatalogueWriteRoutes))]
    public async Task Support_is_refused_on_every_catalogue_write_and_nothing_changes(string route)
    {
        var admin = await PlatformClientAsync("super_admin");
        var support = await PlatformClientAsync("support");
        var seed = await SeedAsync(admin);

        var before = await SnapshotAsync(admin);
        (await Send(support, route, seed)).StatusCode.Should().Be(HttpStatusCode.Forbidden, $"{route}: support reads the catalogue, it never changes it");
        (await SnapshotAsync(admin)).Should().Be(before, $"{route}: a refused write leaves no trace");

        // The same request from a super administrator gets past the policy (whatever the business answer is).
        (await Send(admin, route, seed)).StatusCode.Should().NotBe(HttpStatusCode.Forbidden, route)
            .And.NotBe(HttpStatusCode.Unauthorized, route);
    }

    [Fact]
    public async Task Support_still_reads_the_catalogue_and_the_pilot()
    {
        var support = await PlatformClientAsync("support");
        foreach (var path in CatalogueReads)
            (await support.GetAsync(path)).StatusCode.Should().Be(HttpStatusCode.OK, path);
    }

    [Fact]
    public async Task Without_two_step_verification_a_super_admin_reads_but_cannot_write_the_catalogue()
    {
        await using var host = _factory.WithWebHostBuilder(b => b.UseSetting("Platform:RequireMfaForAdmin", "true"));
        var admin = await PlatformClientAsync("super_admin", () => host.CreateClient());
        var seed = await SeedAsync(await PlatformClientAsync("super_admin"));

        foreach (var path in CatalogueReads)
            (await admin.GetAsync(path)).StatusCode.Should().Be(HttpStatusCode.OK, path);
        foreach (var route in CatalogueWrites.Keys)
            (await Send(admin, route, seed)).StatusCode.Should().Be(HttpStatusCode.Forbidden, $"{route}: a password alone does not change the catalogue");
    }
}
