using System.Linq;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Nexo.Application.Features.Auth;
using Nexo.IntegrationTests.Common;
using Nexo.IntegrationTests.Helpers;

namespace Nexo.IntegrationTests.Security;

/// <summary>
/// Security headers validation test suite.
/// Validates:
/// - Content-Security-Policy headers
/// - CORS headers
/// - X-Frame-Options
/// - Referrer-Policy
/// - X-Content-Type-Options
/// - Permissions-Policy
/// - HSTS (if on HTTPS)
/// </summary>
[Collection("Integration")]
public class SecurityHeadersTests
{
    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public SecurityHeadersTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateApiClient();
    }

    // ────────────────────────────────────────────────────────────────────────────
    // CONTENT-SECURITY-POLICY
    // ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AllResponses_IncludeContentSecurityPolicy()
    {
        var response = await _client.GetAsync("/health");
        
        response.Headers.Should().Contain(h => h.Key == "Content-Security-Policy",
            "every response should include CSP header");

        var cspHeader = response.Headers.GetValues("Content-Security-Policy").First();
        cspHeader.Should().NotBeNullOrEmpty();

        // a API só responde JSON e arquivos: nenhuma origem é liberada para script, estilo, imagem ou conexão
        cspHeader.Should().Contain("default-src 'none'",
            "CSP must block every resource type on API responses");

        // CSP should restrict frame embedding
        cspHeader.Should().Contain("frame-ancestors 'none'",
            "CSP must prevent clickjacking");
        cspHeader.Should().Contain("base-uri 'none'");
        cspHeader.Should().Contain("form-action 'none'");
    }

    [Fact]
    public async Task CSP_AllowsNoInlineCodeNorExternalSources()
    {
        var response = await _client.GetAsync("/health");
        var cspHeader = response.Headers.GetValues("Content-Security-Policy").First();

        // nada de 'unsafe-inline'/'unsafe-eval' nem de esquemas inteiros (https:, data:) liberados
        cspHeader.Should().NotContain("'unsafe-inline'");
        cspHeader.Should().NotContain("'unsafe-eval'");
        cspHeader.Should().NotContain("https:");
        cspHeader.Should().NotContain("data:");
        cspHeader.Should().NotContain("*");
    }

    [Fact]
    public async Task CSP_DoesNotReachTheSwaggerUi()
    {
        // fora de produção o Swagger responde antes do middleware de cabeçalhos, então a CSP restrita da API
        // não quebra a página dele (que usa estilos e scripts próprios)
        var response = await _client.GetAsync("/swagger/index.html");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.Contains("Content-Security-Policy").Should().BeFalse();
    }

    // ────────────────────────────────────────────────────────────────────────────
    // X-FRAME-OPTIONS / CLICKJACKING
    // ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AllResponses_IncludeStrictTransportSecurity()
    {
        var response = await _client.GetAsync("/health");

        response.Headers.GetValues("Strict-Transport-Security").First()
            .Should().Be("max-age=31536000; includeSubDomains", "browsers must keep using HTTPS for the API host");
    }

    [Fact]
    public async Task AllResponses_IncludeXFrameOptions()
    {
        var response = await _client.GetAsync("/health");
        
        response.Headers.Should().Contain(h => h.Key == "X-Frame-Options",
            "every response should include X-Frame-Options");

        var xFrameHeader = response.Headers.GetValues("X-Frame-Options").First();
        xFrameHeader.Should().Be("DENY",
            "X-Frame-Options must be DENY to prevent clickjacking");
    }

    // ────────────────────────────────────────────────────────────────────────────
    // X-CONTENT-TYPE-OPTIONS
    // ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AllResponses_IncludeXContentTypeOptions()
    {
        var response = await _client.GetAsync("/health");
        
        response.Headers.Should().Contain(h => h.Key == "X-Content-Type-Options",
            "every response should include X-Content-Type-Options");

        var header = response.Headers.GetValues("X-Content-Type-Options").First();
        header.Should().Be("nosniff",
            "X-Content-Type-Options must be nosniff to prevent MIME sniffing");
    }

    // ────────────────────────────────────────────────────────────────────────────
    // REFERRER-POLICY
    // ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AllResponses_IncludeReferrerPolicy()
    {
        var response = await _client.GetAsync("/health");
        
        response.Headers.Should().Contain(h => h.Key == "Referrer-Policy",
            "every response should include Referrer-Policy");

        var header = response.Headers.GetValues("Referrer-Policy").First();
        header.Should().BeOneOf(
            "strict-origin-when-cross-origin",
            "no-referrer",
            "same-origin"
        );
    }

    // ────────────────────────────────────────────────────────────────────────────
    // PERMISSIONS-POLICY
    // ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AllResponses_IncludePermissionsPolicy()
    {
        var response = await _client.GetAsync("/health");
        
        response.Headers.Should().Contain(h => h.Key == "Permissions-Policy",
            "every response should include Permissions-Policy");

        var header = response.Headers.GetValues("Permissions-Policy").First();

        // Permissions-Policy should disable risky APIs
        header.Should().Contain("geolocation=()");
        header.Should().Contain("microphone=()");
        header.Should().Contain("camera=()");
        header.Should().Contain("payment=()");
    }

    // ────────────────────────────────────────────────────────────────────────────
    // CORS
    // ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CORSPreflight_ValidOrigin_AllowsRequest()
    {
        var response = await _client.SendAsync(new HttpRequestMessage(HttpMethod.Options, "/api/auth/me")
        {
            Headers = {
                { "Origin", "https://erp.orken.com.br" },
                { "Access-Control-Request-Method", "GET" }
            }
        });

        // Preflight should succeed. ASP.NET Core's CORS middleware short-circuits the
        // OPTIONS preflight with 204 No Content (the framework default); 200 is also a
        // valid preflight response. Either 2xx is acceptable per the CORS spec.
        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NoContent);

        // CORS headers should be present
        response.Headers.Should().Contain(h => h.Key == "Access-Control-Allow-Origin");
        response.Headers.Should().Contain(h => h.Key == "Access-Control-Allow-Credentials");
    }

    [Fact]
    public async Task CORS_PublicPortalOrigin_CanReadETagAndRetryAfter_AndSendTheSessionHeader()
    {
        // the client portal (www.orken.com.br) revalidates /twin/state with If-None-Match and honours Retry-After
        var actual = await _client.SendAsync(new HttpRequestMessage(HttpMethod.Get, "/api/public/build/nao-existe/obra")
        {
            Headers = { { "Origin", "https://www.orken.com.br" } },
        });
        actual.Headers.GetValues("Access-Control-Allow-Origin").Should().Equal("https://www.orken.com.br");
        var exposed = string.Join(",", actual.Headers.GetValues("Access-Control-Expose-Headers"))
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        exposed.Should().Contain(["ETag", "Retry-After"]);

        var preflight = await _client.SendAsync(new HttpRequestMessage(HttpMethod.Options, "/api/public/build/nao-existe/obra/twin/state")
        {
            Headers = {
                { "Origin", "https://www.orken.com.br" },
                { "Access-Control-Request-Method", "GET" },
                { "Access-Control-Request-Headers", "x-build-portal-session,if-none-match" },
            },
        });
        preflight.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NoContent);
        string.Join(",", preflight.Headers.GetValues("Access-Control-Allow-Headers")).ToLowerInvariant()
            .Should().Contain("x-build-portal-session").And.Contain("if-none-match");

        // an origin outside the allow-list still gets nothing it could read
        var foreign = await _client.SendAsync(new HttpRequestMessage(HttpMethod.Get, "/api/public/build/nao-existe/obra")
        {
            Headers = { { "Origin", "https://outro-site.example" } },
        });
        foreign.Headers.Contains("Access-Control-Allow-Origin").Should().BeFalse();
        foreign.Headers.Contains("Access-Control-Expose-Headers").Should().BeFalse();
    }

    [Fact]
    public async Task CORSPreflight_InvalidOrigin_RejectsByDefault()
    {
        var response = await _client.SendAsync(new HttpRequestMessage(HttpMethod.Options, "/api/auth/me")
        {
            Headers = {
                { "Origin", "http://evil.com" },
                { "Access-Control-Request-Method", "GET" }
            }
        });

        // quem barra é o navegador: sem este cabeçalho, nem a origem exata nem "*" estão autorizadas
        response.Headers.Contains("Access-Control-Allow-Origin").Should().BeFalse("origem fora da lista não é autorizada");
        response.Headers.Contains("Access-Control-Allow-Credentials").Should().BeFalse();
    }

    [Fact]
    public async Task CORSPreflight_RestrictedMethods()
    {
        // A política de CORS vale para a API inteira (não é por rota): a restrição é a lista explícita de métodos.
        var response = await _client.SendAsync(new HttpRequestMessage(HttpMethod.Options, "/api/auth/me")
        {
            Headers = {
                { "Origin", "https://erp.orken.com.br" },
                { "Access-Control-Request-Method", "TRACE" }
            }
        });

        response.Headers.GetValues("Access-Control-Allow-Origin").Should().ContainSingle().Which.Should().Be("https://erp.orken.com.br");
        var methods = response.Headers.GetValues("Access-Control-Allow-Methods")
            .SelectMany(m => m.Split(',').Select(s => s.Trim().ToUpperInvariant())).ToList();
        methods.Should().BeEquivalentTo("GET", "POST", "PUT", "DELETE", "PATCH", "OPTIONS");
    }

    // ────────────────────────────────────────────────────────────────────────────
    // HSTS (HTTP Strict-Transport-Security)
    // ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task HSTS_OnHttpsDeployments_IncludesStrictTransportSecurity()
    {
        var response = await _client.GetAsync("/health");
        
        // HSTS is only applicable on HTTPS
        var isHttps = _client.BaseAddress?.Scheme == "https";
        
        if (isHttps)
        {
            response.Headers.Should().Contain(h => h.Key == "Strict-Transport-Security",
                "HTTPS connections should include HSTS");

            var hstsHeader = response.Headers.GetValues("Strict-Transport-Security").First();
            hstsHeader.Should().Contain("max-age=");
            hstsHeader.Should().Contain("includeSubDomains");
        }
        // Note: Test server runs HTTP, so HSTS not required
    }

    // ────────────────────────────────────────────────────────────────────────────
    // AUTHENTICATED ENDPOINTS: HEADERS STILL PRESENT
    // ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SecureEndpoints_StillIncludeSecurityHeaders()
    {
        // Login to get a token
        var loginResponse = await _client.PostAsJsonAsync("/api/auth/login",
            TestCredentials.PlatformLoginPayload());
        var loginBody = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>();

        // Call protected endpoint
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", loginBody!.AccessToken);
        var meResponse = await _client.GetAsync("/api/auth/me");

        // Security headers should still be present
        meResponse.Headers.Should().Contain(h => h.Key == "Content-Security-Policy");
        meResponse.Headers.Should().Contain(h => h.Key == "X-Frame-Options");
        meResponse.Headers.Should().Contain(h => h.Key == "X-Content-Type-Options");
    }

    // ────────────────────────────────────────────────────────────────────────────
    // NO INFORMATION LEAKAGE
    // ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Errors_DontLeakServerImplementationDetails()
    {
        // 404 on non-existent endpoint
        var notFoundResponse = await _client.GetAsync("/api/this-endpoint-does-not-exist");
        
        var content = await notFoundResponse.Content.ReadAsStringAsync();
        
        // Should not contain .NET stack traces or server version info
        content.Should().NotContain("System.", "error responses should not leak .NET details");
        content.Should().NotContain("Exception", "error responses should not leak exception details");
        content.Should().NotContain("stack trace", 
            "error responses should not include stack traces");
    }

    [Fact]
    public async Task AuthErrors_DontLeakUserInfo()
    {
        // Try login with wrong password
        var loginResponse = await _client.PostAsJsonAsync("/api/auth/login",
            new { login = TestCredentials.PlatformEmail, password = "wrong" });

        var content = await loginResponse.Content.ReadAsStringAsync();

        // Should not reveal whether user exists; response should be a generic message
        // (FluentAssertions string assertions are case-sensitive by default;
        //  use ToLower() to do case-insensitive contains checks)
        content.ToLower().Should().NotContain("user does not exist",
            "error must not confirm whether the user account exists");
        content.ToLower().Should().Contain("login",
            "error should reference 'login' with a generic invalid-credentials message");
        // Generic message like "Invalid login or password"
    }
}
