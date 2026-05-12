using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Nexo.Application.Features.Auth;
using Nexo.IntegrationTests.Common;
using Nexo.IntegrationTests.Helpers;

namespace Nexo.IntegrationTests.Auth;

/// <summary>
/// Integration tests for the /api/auth endpoints.
/// Runs against a real PostgreSQL container provisioned by TestWebApplicationFactory.
/// Seed data (admin / IntegrationTestOnly!123) is applied by DataSeeder on startup.
/// </summary>
[Collection("Integration")]
public class AuthEndpointsTests
{
    private readonly HttpClient _client;
    private readonly TestWebApplicationFactory _factory;

    public AuthEndpointsTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateApiClient();
    }

    // ── POST /api/auth/login ──────────────────────────────────────────────────

    [Fact]
    public async Task Login_WithValidAdminCredentials_Returns200WithToken()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login",
            TestCredentials.AdminLoginPayload());

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<LoginResponse>();
        body.Should().NotBeNull();
        body!.AccessToken.Should().NotBeNullOrEmpty();
        body.Session.Login.Should().Be("admin");
        body.Session.Role.Should().Be("diretoria");
    }

    [Fact]
    public async Task Login_WithWrongPassword_Returns401()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login",
            TestCredentials.LoginPayload(TestCredentials.AdminLogin, "wrong-password"));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Login_WithNonExistentUser_Returns401()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login",
            TestCredentials.LoginPayload("ghost_user", "anypassword"));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Login_WithEmptyCredentials_Returns400()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login",
            TestCredentials.LoginPayload("", ""));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── GET /api/auth/me ──────────────────────────────────────────────────────

    [Fact]
    public async Task Me_WithValidToken_Returns200WithSession()
    {
        var client = await AuthClientFactory.LoginAsAdminAsync(_factory);

        var response = await client.GetAsync("/api/auth/me");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var session = await response.Content.ReadFromJsonAsync<SessionDto>();
        session.Should().NotBeNull();
        session!.Login.Should().Be("admin");
    }

    [Fact]
    public async Task Me_WithoutToken_Returns401()
    {
        var client = _factory.CreateApiClient();  // no auth header
        var response = await client.GetAsync("/api/auth/me");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── POST /api/auth/verify-manager ─────────────────────────────────────────

    [Fact]
    public async Task VerifyManager_WithAdminCredentials_ReturnsAuthorized()
    {
        var client = await AuthClientFactory.LoginAsAdminAsync(_factory);

        var response = await client.PostAsJsonAsync("/api/auth/verify-manager",
            TestCredentials.AdminLoginPayload());

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<VerifyManagerResponse>();
        body!.Authorized.Should().BeTrue();
        body.Role.Should().Be("diretoria");
    }
}
