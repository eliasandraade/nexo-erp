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
/// Per-store Service settings (the v1.1 single-module + preset model):
///   - the seeded dev tenant is granted the single "service" module and configured with a
///     sample preset (salao-beleza), so it reports isConfigured = true;
///   - the preset can be changed via PUT and drives GET /preset;
///   - an invalid preset key is rejected (400);
///   - a tenant without the service entitlement is forbidden (403).
///
/// The mutating roundtrip restores the seeded preset in a finally block — the suite shares one
/// seeded database (no Respawn), so state must be left as other tests expect it.
/// </summary>
[Collection("Integration")]
public class ServiceSettingsTests
{
    private readonly TestWebApplicationFactory _factory;

    public ServiceSettingsTests(TestWebApplicationFactory factory) => _factory = factory;

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
    public async Task Settings_report_configured_for_the_seeded_service_tenant()
    {
        var client = await LoginAsync(TestCredentials.AdminLoginPayload());

        var resp = await client.GetAsync("/api/v1/service/settings");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("isConfigured").GetBoolean().Should().BeTrue();
        body.GetProperty("presetKey").GetString().Should().Be("salao-beleza");
    }

    [Fact]
    public async Task Setting_a_preset_roundtrips_and_drives_the_resolved_preset()
    {
        var client = await LoginAsync(TestCredentials.AdminLoginPayload());
        try
        {
            var put = await client.PutAsJsonAsync(
                "/api/v1/service/settings/preset", new { presetKey = "pet-shop" });
            put.StatusCode.Should().Be(HttpStatusCode.OK);

            var settingsResp = await client.GetAsync("/api/v1/service/settings");
            var settings = await settingsResp.Content.ReadFromJsonAsync<JsonElement>();
            settings.GetProperty("isConfigured").GetBoolean().Should().BeTrue();
            settings.GetProperty("presetKey").GetString().Should().Be("pet-shop");

            var presetResp = await client.GetAsync("/api/v1/service/preset");
            presetResp.StatusCode.Should().Be(HttpStatusCode.OK);
            var preset = await presetResp.Content.ReadFromJsonAsync<JsonElement>();
            preset.GetProperty("key").GetString().Should().Be("pet-shop");
            preset.GetProperty("capabilities").GetProperty("appointments").GetBoolean().Should().BeTrue();
        }
        finally
        {
            // Restore the seeded preset so other tests sharing this tenant see salao-beleza.
            await client.PutAsJsonAsync(
                "/api/v1/service/settings/preset", new { presetKey = "salao-beleza" });
        }
    }

    [Fact]
    public async Task Setting_an_invalid_preset_key_is_rejected()
    {
        var client = await LoginAsync(TestCredentials.AdminLoginPayload());

        var resp = await client.PutAsJsonAsync(
            "/api/v1/service/settings/preset", new { presetKey = "not-a-real-vertical" });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Settings_are_forbidden_for_a_tenant_without_the_service_entitlement()
    {
        var client = await LoginAsync(TestCredentials.LoginPayload("clara.boutique", "boutique@123"));

        var resp = await client.GetAsync("/api/v1/service/settings");

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
