using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Nexo.Api.Orken;
using Nexo.IntegrationTests.Helpers;
using Xunit;

namespace Nexo.IntegrationTests.Security;

/// <summary>
/// O caminho de produção do esquema OrkenService: a descoberta OIDC e o JWKS vêm do Orken ID pela rede (aqui, um handler
/// falso no backchannel), e a chave é escolhida pelo <c>kid</c> do token. Os testes do contrato injetam a chave direto em
/// <c>JwtBearerOptions.Configuration</c> e não passam por aqui: foi assim que versões desalinhadas do Microsoft.IdentityModel
/// chegaram à produção com "The signature key was not found".
/// </summary>
[Collection("Integration")]
public sealed class OrkenOperationsDiscoveryTests
{
    private const string Issuer = "https://orken-id.test/";
    private static readonly RsaSecurityKey SigningKey = new(RSA.Create(2048)) { KeyId = "AEE17B4F505BCE2E9B7B627E326912CC8D46F7E2" };

    private readonly TestWebApplicationFactory _factory;

    public OrkenOperationsDiscoveryTests(TestWebApplicationFactory factory) => _factory = factory;

    /// <summary>Serve a descoberta e o JWKS como o Orken ID (chave RSA de assinatura, use=sig, alg=RS256, com kid).</summary>
    private sealed class FakeOrkenId : HttpMessageHandler
    {
        public int Requests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            var path = request.RequestUri!.AbsolutePath;
            string body;
            if (path == "/.well-known/openid-configuration")
            {
                body = JsonSerializer.Serialize(new Dictionary<string, object>
                {
                    ["issuer"] = Issuer,
                    ["jwks_uri"] = Issuer + ".well-known/jwks",
                    ["token_endpoint"] = Issuer + "connect/token",
                    ["id_token_signing_alg_values_supported"] = new[] { "RS256" },
                });
            }
            else if (path == "/.well-known/jwks")
            {
                var jwk = JsonWebKeyConverter.ConvertFromRSASecurityKey(new RsaSecurityKey(SigningKey.Rsa.ExportParameters(false)) { KeyId = SigningKey.KeyId });
                body = JsonSerializer.Serialize(new
                {
                    keys = new[] { new { kid = jwk.Kid, use = "sig", kty = "RSA", alg = "RS256", e = jwk.E, n = jwk.N } },
                });
            }
            else
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }

    private static string Token() => new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
    {
        Issuer = Issuer,
        Audience = OrkenOperationsEndpoints.Audience,
        TokenType = "at+jwt",
        Expires = DateTime.UtcNow.AddMinutes(10),
        Claims = new Dictionary<string, object>
        {
            ["sub"] = OrkenOperationsEndpoints.DefaultAllowedClient,
            ["client_id"] = OrkenOperationsEndpoints.DefaultAllowedClient,
            ["scope"] = OrkenOperationsEndpoints.Scope,
        },
        SigningCredentials = new SigningCredentials(SigningKey, SecurityAlgorithms.RsaSha256),
    });

    [Fact]
    public async Task A_token_signed_with_the_key_published_in_the_jwks_is_accepted_through_oidc_discovery()
    {
        var orkenId = new FakeOrkenId();
        await using var host = _factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("OrkenId:Issuer", Issuer);
            b.ConfigureTestServices(s => s.Configure<JwtBearerOptions>(OrkenOperationsEndpoints.Scheme, o => o.BackchannelHttpHandler = orkenId));
        });
        var client = host.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token());

        var response = await client.GetAsync(OrkenOperationsEndpoints.Route);

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "the WWW-Authenticate was: " + string.Join(" ", response.Headers.WwwAuthenticate.Select(h => h.ToString())));
        orkenId.Requests.Should().BeGreaterThanOrEqualTo(2, "the keys come from discovery and the JWKS, not from a test shortcut");
    }
}
