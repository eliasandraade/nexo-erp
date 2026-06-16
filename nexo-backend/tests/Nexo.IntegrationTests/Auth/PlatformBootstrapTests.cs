using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Nexo.Infrastructure.Persistence.Seed;
using Nexo.IntegrationTests.Common;
using Nexo.IntegrationTests.Helpers;

namespace Nexo.IntegrationTests.Auth;

/// <summary>
/// Validates the production super-admin bootstrap (DataSeeder.SeedPlatformAdminAsync):
/// it is idempotent and provisions/syncs a super-admin that can log in via
/// /api/platform/auth/login with the env-configured credentials.
/// </summary>
[Collection("Integration")]
public class PlatformBootstrapTests
{
    private readonly TestWebApplicationFactory _factory;
    public PlatformBootstrapTests(TestWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task SeedPlatformAdmin_IsIdempotent_AndSyncsLogin()
    {
        using var scope = _factory.Services.CreateScope();
        var seeder = scope.ServiceProvider.GetRequiredService<DataSeeder>();

        // Calling twice must not throw and must not duplicate the super-admin.
        await seeder.SeedPlatformAdminAsync();
        await seeder.SeedPlatformAdminAsync();

        // The synced super-admin can log in with the env-configured credentials.
        var client = _factory.CreateApiClient();
        var resp = await client.PostAsJsonAsync("/api/platform/auth/login", new
        {
            email = TestCredentials.PlatformEmail,
            password = TestCredentials.PlatformPassword,
        });

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task PlatformLogin_WrongPassword_Returns401()
    {
        var client = _factory.CreateApiClient();
        var resp = await client.PostAsJsonAsync("/api/platform/auth/login", new
        {
            email = TestCredentials.PlatformEmail,
            password = "definitely-not-the-password",
        });

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
