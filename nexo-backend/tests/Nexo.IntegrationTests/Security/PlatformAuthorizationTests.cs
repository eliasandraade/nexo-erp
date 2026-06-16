using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Nexo.IntegrationTests.Common;
using Nexo.IntegrationTests.Helpers;

namespace Nexo.IntegrationTests.Security;

/// <summary>
/// Verifies the single [Authorize(Policy="Platform")] gate on the platform-only
/// controllers. One representative GET per controller:
///   - no token        → 401
///   - tenant token     → 403 (authenticated, but no type=platform claim)
///   - platform token   → 200
/// </summary>
[Collection("Integration")]
public class PlatformAuthorizationTests
{
    private readonly TestWebApplicationFactory _factory;
    public PlatformAuthorizationTests(TestWebApplicationFactory factory) => _factory = factory;

    public static IEnumerable<object[]> PlatformEndpoints() => new[]
    {
        new object[] { "/api/platform/stats" },                 // PlatformController
        new object[] { "/api/platform/flags" },                 // PlatformFlagsController
        new object[] { "/api/platform/interpreter/dashboard" }, // InterpreterAdminController
    };

    [Theory]
    [MemberData(nameof(PlatformEndpoints))]
    public async Task NoToken_Returns401(string path)
    {
        var client = _factory.CreateApiClient();
        (await client.GetAsync(path)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [MemberData(nameof(PlatformEndpoints))]
    public async Task TenantToken_Returns403(string path)
    {
        var client = await AuthClientFactory.LoginAsAdminAsync(_factory); // tenant (diretoria) token
        (await client.GetAsync(path)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Theory]
    [MemberData(nameof(PlatformEndpoints))]
    public async Task PlatformToken_Returns200(string path)
    {
        var client = await PlatformClientAsync(_factory);
        (await client.GetAsync(path)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>Logs in as the seeded platform super-admin and returns a Bearer-authed client.</summary>
    internal static async Task<HttpClient> PlatformClientAsync(TestWebApplicationFactory factory)
    {
        var client = factory.CreateApiClient();
        var resp = await client.PostAsJsonAsync("/api/platform/auth/login", new
        {
            email = TestCredentials.PlatformEmail,
            password = TestCredentials.PlatformPassword,
        });
        resp.StatusCode.Should().Be(HttpStatusCode.OK, "platform super-admin must be seeded for these tests");
        var body = await resp.Content.ReadFromJsonAsync<PlatformLoginResponse>();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", body!.AccessToken);
        return client;
    }

    private record PlatformLoginResponse(string AccessToken);
}
