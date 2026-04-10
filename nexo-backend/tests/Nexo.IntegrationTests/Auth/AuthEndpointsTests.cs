using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Nexo.Application.Features.Auth;
using Nexo.IntegrationTests.Helpers;

namespace Nexo.IntegrationTests.Auth;

/// <summary>
/// Integration tests for the /api/auth endpoints.
/// Runs against a real PostgreSQL container provisioned by TestWebApplicationFactory.
/// Seed data (admin / nexo@2026) is applied by DataSeeder on startup.
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
            new { login = "admin", password = "nexo@2026" });

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
            new { login = "admin", password = "wrong-password" });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Login_WithNonExistentUser_Returns401()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login",
            new { login = "ghost_user", password = "anypassword" });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Login_WithEmptyCredentials_Returns400()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login",
            new { login = "", password = "" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── GET /api/auth/me ──────────────────────────────────────────────────────

    [Fact]
    public async Task Me_WithValidToken_Returns200WithSession()
    {
        var token = await GetAdminTokenAsync();

        _client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var response = await _client.GetAsync("/api/auth/me");

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
        var token = await GetAdminTokenAsync();
        _client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var response = await _client.PostAsJsonAsync("/api/auth/verify-manager",
            new { login = "admin", password = "nexo@2026" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<VerifyManagerResponse>();
        body!.Authorized.Should().BeTrue();
        body.Role.Should().Be("diretoria");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<string> GetAdminTokenAsync()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login",
            new { login = "admin", password = "nexo@2026" });

        var body = await response.Content.ReadFromJsonAsync<LoginResponse>();
        return body!.AccessToken;
    }
}
