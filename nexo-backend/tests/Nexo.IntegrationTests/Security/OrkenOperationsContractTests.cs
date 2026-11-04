using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Nexo.Api.Orken;
using Nexo.Domain.Modules.Interpreter;
using Nexo.Infrastructure.Persistence;
using Nexo.IntegrationTests.Common;
using Nexo.IntegrationTests.Helpers;
using Xunit;

namespace Nexo.IntegrationTests.Security;

/// <summary>
/// GET /api/orken/v1/operations, lido pela Orken Platform com um token de serviço do Orken ID. Os tokens destes testes são
/// assinados com uma chave local e o esquema OrkenService do host de teste aceita só essa chave e o issuer de teste
/// (configuração feita apenas aqui, nunca no código de produção). Sem OrkenId:Issuer a rota não existe.
/// </summary>
[Collection("Integration")]
public sealed class OrkenOperationsContractTests
{
    private const string TestIssuer = "https://orken-id.test/";
    private const string Collector = OrkenOperationsEndpoints.DefaultAllowedClient;

    private static readonly RsaSecurityKey SigningKey = new(RSA.Create(2048)) { KeyId = "orken-id-test" };
    private static readonly RsaSecurityKey OtherKey = new(RSA.Create(2048)) { KeyId = "orken-id-test" };

    private readonly TestWebApplicationFactory _factory;

    public OrkenOperationsContractTests(TestWebApplicationFactory factory) => _factory = factory;

    /// <summary>Host com o contrato ligado: issuer de teste e a chave local no lugar da descoberta OIDC.</summary>
    private WebApplicationFactory<Program> OrkenHost(string? allowedClient = null) => _factory.WithWebHostBuilder(b =>
    {
        b.UseSetting("OrkenId:Issuer", TestIssuer);
        if (allowedClient is not null) b.UseSetting("OrkenId:AllowedClients:0", allowedClient);
        b.ConfigureTestServices(s => s.Configure<JwtBearerOptions>(OrkenOperationsEndpoints.Scheme, o =>
            o.Configuration = new OpenIdConnectConfiguration { Issuer = TestIssuer, SigningKeys = { SigningKey } }));
    });

    private static string Token(
        string audience = OrkenOperationsEndpoints.Audience, string scope = OrkenOperationsEndpoints.Scope, string clientId = Collector,
        string issuer = TestIssuer, string type = "at+jwt", SecurityKey? key = null, DateTime? expires = null)
    {
        var now = DateTime.UtcNow;
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            TokenType = type,
            IssuedAt = (expires ?? now.AddMinutes(10)).AddMinutes(-10),
            NotBefore = (expires ?? now.AddMinutes(10)).AddMinutes(-10),
            Expires = expires ?? now.AddMinutes(10),
            Claims = new Dictionary<string, object>
            {
                ["sub"] = clientId,
                ["client_id"] = clientId,
                ["scope"] = scope,
                ["orken_actor"] = "service",
            },
            SigningCredentials = new SigningCredentials(key ?? SigningKey, SecurityAlgorithms.RsaSha256),
        });
    }

    private static async Task<HttpResponseMessage> GetAsync(WebApplicationFactory<Program> host, string? token, string route = OrkenOperationsEndpoints.Route)
    {
        var client = host.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        if (token is not null) client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client.GetAsync(route);
    }

    [Fact]
    public async Task Without_OrkenId_Issuer_the_route_does_not_exist()
    {
        (await GetAsync(_factory, Token())).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await GetAsync(_factory, null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Without_a_token_or_with_an_invalid_one_the_answer_is_401()
    {
        await using var host = OrkenHost();
        (await GetAsync(host, null)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await GetAsync(host, "not-a-jwt")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await GetAsync(host, Token(key: OtherKey))).StatusCode.Should().Be(HttpStatusCode.Unauthorized, "assinado por outra chave");
        (await GetAsync(host, Token(issuer: "https://outro-issuer.test/"))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await GetAsync(host, Token(type: "JWT"))).StatusCode.Should().Be(HttpStatusCode.Unauthorized, "só access token (at+jwt)");
        (await GetAsync(host, Token(expires: DateTime.UtcNow.AddMinutes(-5)))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        // O token de outro recurso (o da Platform API) não vale aqui: a audiência é conferida junto com o token.
        (await GetAsync(host, Token(audience: "orken-platform-api"))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // Um token de sessão do ERP nunca abre esta rota.
        var (_, login) = await AuthClientFactory.LoginAsAdminWithBodyAsync(_factory);
        (await GetAsync(host, login.AccessToken)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_valid_token_without_the_scope_or_from_a_client_outside_the_list_is_403()
    {
        await using var host = OrkenHost();
        (await GetAsync(host, Token(scope: "platform.tenancy.read platform.events.read"))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await GetAsync(host, Token(clientId: "orken-gov-service"))).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        await using var otherList = OrkenHost(allowedClient: "outro-coletor");
        (await GetAsync(otherList, Token())).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await GetAsync(otherList, Token(clientId: "outro-coletor"))).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task An_Orken_service_token_opens_no_other_ERP_route()
    {
        await using var host = OrkenHost();
        (await GetAsync(host, Token(), "/api/platform/stats")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await GetAsync(host, Token(), "/api/products")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_valid_token_reads_aggregates_only_with_estimates_marked_and_no_personal_data()
    {
        // uma execução com falha do interpretador nas últimas 24 h, para os contadores terem o que contar
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
            var tenantId = await db.Tenants.IgnoreQueryFilters().Select(t => t.Id).FirstAsync();
            db.InterpreterTelemetry.Add(InterpreterTelemetry.Create(tenantId, null, null, "Analyze", "RuleBased", "extraction", "1.0.0", "hash",
                0, 0, 0, 12, false, "falha de teste", false, null, "[]", 0, 0, 0));
            await db.SaveChangesAsync();
        }

        await using var host = OrkenHost();
        var response = await GetAsync(host, Token());
        var text = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, text);
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        var root = JsonDocument.Parse(text).RootElement;

        root.GetProperty("contract").GetString().Should().Be("orken.erp.operations.v1");
        root.GetProperty("generatedAt").GetDateTimeOffset().Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1));

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
            var tenants = await db.Tenants.IgnoreQueryFilters().AsNoTracking().ToListAsync();
            var t = root.GetProperty("tenants");
            t.GetProperty("total").GetInt32().Should().Be(tenants.Count);
            t.GetProperty("byStatus").EnumerateArray().Select(c => c.GetProperty("key").GetString())
                .Should().BeEquivalentTo(["ACTIVE", "SUSPENDED", "CANCELLED"]);
            t.GetProperty("byStatus").EnumerateArray().Sum(c => c.GetProperty("count").GetInt32()).Should().Be(tenants.Count);

            // Nenhum nome, e-mail, CNPJ ou id de tenant em parte alguma da resposta.
            var values = Values(root).ToList();
            foreach (var tenant in tenants)
            {
                foreach (var value in new[] { tenant.CompanyName, tenant.TradeName, tenant.Email, tenant.TaxId, tenant.Id.ToString() })
                    if (!string.IsNullOrWhiteSpace(value))
                        values.Should().NotContain(value);
            }
            text.Should().NotContain("@", "nenhum endereço de e-mail");
            text.Should().NotMatchRegex("[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}", "nenhum identificador");
            text.Should().NotMatchRegex(@"\d{2}\.\d{3}\.\d{3}/\d{4}-\d{2}|(?<!\d)\d{14}(?!\d)", "nenhum CNPJ");
        }

        var i = root.GetProperty("interpreter");
        i.GetProperty("runsLast24Hours").GetInt32().Should().BeGreaterThan(0);
        i.GetProperty("failuresLast24Hours").GetInt32().Should().BeGreaterThan(0);
        i.GetProperty("runsLast7Days").GetInt32().Should().BeGreaterThanOrEqualTo(i.GetProperty("runsLast24Hours").GetInt32());
        i.GetProperty("runsByProviderLast7Days").EnumerateArray().Should().Contain(p => p.GetProperty("key").GetString() == "RuleBased");
        var cost = i.GetProperty("estimatedCostLast7Days");
        cost.GetProperty("kind").GetString().Should().Be("estimate");
        cost.GetProperty("currency").GetString().Should().Be("USD");

        var mrr = root.GetProperty("commercial").GetProperty("estimatedMrr");
        mrr.GetProperty("kind").GetString().Should().Be("estimate");
        mrr.GetProperty("currency").GetString().Should().Be("BRL");
        mrr.GetProperty("method").GetString().Should().Contain("não é faturamento");

        var build = root.GetProperty("build");
        build.GetProperty("jobsLast24Hours").EnumerateArray().Select(c => c.GetProperty("key").GetString())
            .Should().BeEquivalentTo(["PENDING", "RUNNING", "DONE", "FAILED"]);
        root.GetProperty("modules").ValueKind.Should().Be(JsonValueKind.Array);

        // Só nomes de campos agregados: nada que carregue uma pessoa ou uma empresa.
        Names(root).Should().NotContain(n =>
            n.Contains("email", StringComparison.OrdinalIgnoreCase) || n.Contains("cnpj", StringComparison.OrdinalIgnoreCase)
            || n.Contains("taxid", StringComparison.OrdinalIgnoreCase) || n.Contains("name", StringComparison.OrdinalIgnoreCase)
            || n.Contains("phone", StringComparison.OrdinalIgnoreCase) || n.Equals("id", StringComparison.OrdinalIgnoreCase)
            || n.Contains("tenantid", StringComparison.OrdinalIgnoreCase) || n.Contains("userid", StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<string> Values(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => element.EnumerateObject().Where(p => p.Name != "method").SelectMany(p => Values(p.Value)),
        JsonValueKind.Array => element.EnumerateArray().SelectMany(Values),
        JsonValueKind.String => [element.GetString()!],
        _ => [],
    };

    private static IEnumerable<string> Names(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => element.EnumerateObject().SelectMany(p => Names(p.Value).Prepend(p.Name)),
        JsonValueKind.Array => element.EnumerateArray().SelectMany(Names),
        _ => [],
    };
}
