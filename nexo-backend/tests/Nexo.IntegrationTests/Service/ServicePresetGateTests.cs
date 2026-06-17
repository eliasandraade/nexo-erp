using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Nexo.Application.Features.Auth;
using Nexo.IntegrationTests.Common;
using Nexo.IntegrationTests.Helpers;

namespace Nexo.IntegrationTests.Service;

/// <summary>
/// Verifies the family-aware Service module gate (decision D1) and the preset endpoint:
///   - a tenant WITHOUT any service-family key is forbidden (403);
///   - a tenant WITH a service-family key gets its resolved preset (labels + capabilities).
///
/// The seeder grants 'salao-beleza' (service family) to the default dev tenant, mirroring
/// SeedBuildModuleAsync; 'clara.boutique' (varejo only) provides the negative case.
/// </summary>
[Collection("Integration")]
public class ServicePresetGateTests
{
    private readonly TestWebApplicationFactory _factory;

    public ServicePresetGateTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private async Task<HttpClient> LoginAsync(object payload)
    {
        var client = _factory.CreateApiClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", payload);
        response.StatusCode.Should().Be(HttpStatusCode.OK, "login must succeed as a precondition");
        var body = await response.Content.ReadFromJsonAsync<LoginResponse>();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", body!.AccessToken);
        return client;
    }

    [Fact]
    public async Task Preset_is_forbidden_for_a_tenant_without_a_service_module()
    {
        var client = await LoginAsync(TestCredentials.LoginPayload("clara.boutique", "boutique@123"));

        var resp = await client.GetAsync("/api/v1/service/preset");

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Preset_returns_the_resolved_descriptor_for_a_service_tenant()
    {
        var client = await LoginAsync(TestCredentials.AdminLoginPayload());

        var resp = await client.GetAsync("/api/v1/service/preset");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("key").GetString().Should().Be("salao-beleza");
        body.GetProperty("displayName").GetString().Should().NotBeNullOrWhiteSpace();
        body.GetProperty("labels").GetProperty("customer").GetString().Should().Be("Cliente");
        body.GetProperty("capabilities").GetProperty("appointments").GetBoolean().Should().BeTrue();
        body.GetProperty("capabilities").GetProperty("packages").GetBoolean().Should().BeTrue();
    }
}
