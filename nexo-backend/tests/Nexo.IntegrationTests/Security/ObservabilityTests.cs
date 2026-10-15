using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Nexo.Application.Common.Observability;
using Nexo.IntegrationTests.Common;
using Nexo.IntegrationTests.Helpers;
using Xunit;

namespace Nexo.IntegrationTests.Security;

/// <summary>
/// PR 69 — every response carries a request id (the caller's when it is sane), error bodies repeat
/// it, and unexpected errors are grouped in error_events and listed to the platform team only.
/// </summary>
[Collection("Integration")]
public class ObservabilityTests
{
    private readonly TestWebApplicationFactory _factory;
    public ObservabilityTests(TestWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Every_response_carries_a_request_id_and_errors_repeat_it()
    {
        var client = _factory.CreateApiClient();

        var generated = await client.GetAsync("/health/live");
        generated.Headers.GetValues("X-Request-Id").Single().Should().MatchRegex("^[0-9a-f]{32}$");

        var mine = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        mine.Headers.Add("X-Request-Id", "frontend-req-12345");
        (await client.SendAsync(mine)).Headers.GetValues("X-Request-Id").Single().Should().Be("frontend-req-12345");

        var injected = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        injected.Headers.TryAddWithoutValidation("X-Request-Id", "evil\"value{with}junk");
        (await client.SendAsync(injected)).Headers.GetValues("X-Request-Id").Single().Should().NotContain("evil");

        var admin = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var notFound = await admin.GetAsync($"/api/fiscal/products/{Guid.NewGuid()}");
        notFound.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var body = await notFound.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("requestId").GetString().Should().Be(notFound.Headers.GetValues("X-Request-Id").Single());
    }

    [Fact]
    public async Task Unexpected_errors_are_grouped_and_only_the_platform_sees_them()
    {
        var tracker = _factory.Services.GetRequiredService<IErrorTracker>();
        var route = "api/test/" + Guid.NewGuid().ToString("N")[..8];
        Exception Boom()
        {
            try { throw new InvalidOperationException("Cliente maria@exemplo.com com CPF 52998224725 quebrou"); }
            catch (InvalidOperationException ex) { return ex; }
        }

        await tracker.TrackAsync(new ErrorOccurrence(Boom(), route, "GET", "req-one-000001", null));
        await tracker.TrackAsync(new ErrorOccurrence(Boom(), route, "GET", "req-two-000002", null));

        var platform = await PlatformAuthorizationTests.PlatformClientAsync(_factory);
        var errors = await platform.GetFromJsonAsync<JsonElement>("/api/platform/errors");
        var mine = errors.EnumerateArray().Single(e => e.GetProperty("route").GetString() == route);
        mine.GetProperty("count").GetInt32().Should().Be(2, "the same kind of error is one row");
        mine.GetProperty("lastRequestId").GetString().Should().Be("req-two-000002");
        var summary = mine.GetProperty("summary").GetString();
        summary.Should().StartWith("InvalidOperationException:").And.NotContain("maria").And.NotContain("52998224725");

        var tenantUser = await AuthClientFactory.LoginAsAdminAsync(_factory);
        (await tenantUser.GetAsync("/api/platform/errors")).StatusCode.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden);
    }
}
