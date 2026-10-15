using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Nexo.IntegrationTests.Common;
using Nexo.IntegrationTests.Helpers;
using Xunit;

namespace Nexo.IntegrationTests.Security;

/// <summary>
/// PR 68 — liveness never depends on anything; readiness depends on PostgreSQL only; the full
/// report lists every dependency without leaking hosts or exception text.
/// </summary>
[Collection("Integration")]
public class HealthEndpointTests
{
    private readonly TestWebApplicationFactory _factory;
    public HealthEndpointTests(TestWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Liveness_checks_nothing_and_readiness_checks_the_database()
    {
        var client = _factory.CreateApiClient();

        var live = await client.GetAsync("/health/live");
        live.StatusCode.Should().Be(HttpStatusCode.OK);
        var liveBody = await live.Content.ReadFromJsonAsync<JsonElement>();
        liveBody.GetProperty("status").GetString().Should().Be("Healthy");
        liveBody.GetProperty("checks").GetArrayLength().Should().Be(0, "a dependency outage must not restart a healthy process");

        var ready = await client.GetAsync("/health/ready");
        ready.StatusCode.Should().Be(HttpStatusCode.OK);
        ready.Headers.CacheControl!.NoStore.Should().BeTrue();
        var checks = Checks(await ready.Content.ReadFromJsonAsync<JsonElement>());
        checks.Should().ContainKey("postgres").WhoseValue.Should().Be("Healthy");
        checks.Should().ContainKeys("redis", "object-storage");
        checks.Should().NotContainKey("interpreter-storage", "attachments are not a readiness concern");
    }

    [Fact]
    public async Task The_full_report_lists_every_dependency_without_details()
    {
        var response = await _factory.CreateApiClient().GetAsync("/health");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var text = await response.Content.ReadAsStringAsync();
        var checks = Checks(JsonDocument.Parse(text).RootElement);
        checks.Keys.Should().Contain(["postgres", "redis", "object-storage", "interpreter-storage"]);
        text.Should().NotContainAny("Host=", "Password", "Exception", "   at ");
    }

    private static Dictionary<string, string> Checks(JsonElement body)
        => body.GetProperty("checks").EnumerateArray()
            .ToDictionary(c => c.GetProperty("name").GetString()!, c => c.GetProperty("status").GetString()!);
}
