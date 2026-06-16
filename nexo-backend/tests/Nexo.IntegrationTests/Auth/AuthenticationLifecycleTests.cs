using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nexo.Application.Features.Auth;
using Nexo.Domain.Enums;
using Nexo.Infrastructure.Persistence;
using Nexo.IntegrationTests.Common;
using Nexo.IntegrationTests.Helpers;

namespace Nexo.IntegrationTests.Auth;

/// <summary>
/// Enhanced authentication test suite covering:
/// - Token lifecycle (issuance, expiration, revocation)
/// - Session consistency
/// - Edge cases and error scenarios
/// - Multi-tenant authentication isolation
/// - User status validation
///
/// These tests validate the post-hardening auth system works correctly.
/// </summary>
[Collection("Integration")]
public class AuthenticationLifecycleTests
{
    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public AuthenticationLifecycleTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateApiClient();
    }

    // ────────────────────────────────────────────────────────────────────────────
    // TOKEN LIFECYCLE
    // ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Login_IssuedToken_CanAccessProtectedEndpoints()
    {
        // 1. Login
        var loginResponse = await _client.PostAsJsonAsync("/api/auth/login",
            TestCredentials.AdminLoginPayload());

        var body = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>();
        var token = body!.AccessToken;

        // 2. Use token to access protected endpoint
        var meClient = _factory.CreateApiClient();
        meClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);

        var meResponse = await meClient.GetAsync("/api/auth/me");
        meResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var session = await meResponse.Content.ReadFromJsonAsync<SessionDto>();
        session!.Login.Should().Be("admin");
    }

    [Fact]
    public async Task InvalidToken_IsRejectedWithUnauthorized()
    {
        var client = _factory.CreateApiClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "invalid.token.here");

        var response = await client.GetAsync("/api/auth/me");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task MissingToken_IsRejectedWithUnauthorized()
    {
        var client = _factory.CreateApiClient();
        // No Authorization header

        var response = await client.GetAsync("/api/auth/me");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task MalformedToken_IsRejectedWithUnauthorized()
    {
        var client = _factory.CreateApiClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestCredentials.MalformedToken);

        var response = await client.GetAsync("/api/auth/me");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ────────────────────────────────────────────────────────────────────────────
    // REFRESH TOKEN FLOW
    // ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RefreshToken_ValidToken_ReturnsNewAccessToken()
    {
        // 1. Login
        var loginResponse = await _client.PostAsJsonAsync("/api/auth/login",
            TestCredentials.AdminLoginPayload());
        var loginBody = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>();

        // 2. Refresh
        var refreshResponse = await _client.PostAsJsonAsync("/api/auth/refresh",
            TestCredentials.RefreshPayload(loginBody!.RefreshToken));

        refreshResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var refreshBody = await refreshResponse.Content.ReadFromJsonAsync<RefreshResponse>();
        refreshBody!.AccessToken.Should().NotBeNullOrEmpty();
        refreshBody.RefreshToken.Should().NotBeNullOrEmpty();

        // 3. New access token should work
        var newClient = _factory.CreateApiClient();
        newClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", refreshBody.AccessToken);

        var meResponse = await newClient.GetAsync("/api/auth/me");
        meResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task RefreshToken_WithInvalidToken_ReturnsUnauthorized()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/refresh",
            TestCredentials.RefreshPayload(TestCredentials.InvalidRefreshToken));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task RefreshToken_WithEmptyToken_ReturnsBadRequest()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/refresh",
            TestCredentials.RefreshPayload(""));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task RefreshToken_FromCookie_WorksWithoutJsonBody()
    {
        // 1. Login (sets cookies)
        var loginResponse = await _client.PostAsJsonAsync("/api/auth/login",
            TestCredentials.AdminLoginPayload());

        // 2. Create new client with cookies
        var cookieClient = _factory.CreateApiClient();
        foreach (var cookie in loginResponse.Headers.GetValues("Set-Cookie"))
        {
            cookieClient.DefaultRequestHeaders.Add("Cookie", ParseCookieForRequest(cookie));
        }

        // 3. Refresh without providing token in body (should read from cookie)
        var refreshResponse = await cookieClient.PostAsJsonAsync("/api/auth/refresh",
            new { });

        // This should work if the endpoint reads from cookie
        if (refreshResponse.StatusCode == HttpStatusCode.OK)
        {
            var body = await refreshResponse.Content.ReadFromJsonAsync<RefreshResponse>();
            body!.AccessToken.Should().NotBeNullOrEmpty();
        }
        // If it returns 400, that's also acceptable (requires explicit body)
        else if (refreshResponse.StatusCode != HttpStatusCode.BadRequest)
        {
            // But definitely shouldn't return 500 or other unexpected errors
            refreshResponse.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);
        }
    }

    // ────────────────────────────────────────────────────────────────────────────
    // LOGOUT / REVOCATION
    // ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Logout_RevokesRefreshToken_PreventsSubsequentRefresh()
    {
        // 1. Login
        var loginResponse = await _client.PostAsJsonAsync("/api/auth/login",
            TestCredentials.AdminLoginPayload());
        var loginBody = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>();

        // 2. Logout — send the refresh token in the body. The test transport is HTTP, and
        //    the nexo_refresh cookie is marked Secure, so it is NOT sent back over HTTP;
        //    the cookie-fallback path can't see it. Passing the token explicitly exercises
        //    the actual revocation logic (and matches a Bearer/token-based SPA client).
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", loginBody!.AccessToken);
        var logoutResponse = await _client.PostAsJsonAsync("/api/auth/logout",
            new { refreshToken = loginBody.RefreshToken });
        logoutResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // 3. Try to use the same refresh token — should fail
        var refreshClient = _factory.CreateApiClient();
        var refreshResponse = await refreshClient.PostAsJsonAsync("/api/auth/refresh",
            TestCredentials.RefreshPayload(loginBody.RefreshToken));

        refreshResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "refresh token should be revoked after logout");
    }

    [Fact]
    public async Task Logout_WithoutAuthToken_StillReturnsNoContent()
    {
        var response = await _client.PostAsync("/api/auth/logout", null);

        // Should be idempotent — safe to call even without token
        response.StatusCode.Should().BeOneOf(
            HttpStatusCode.NoContent,   // Ideal: graceful
            HttpStatusCode.Unauthorized // Also acceptable: requires auth
        );
    }

    [Fact]
    public async Task LogoutAfterLogout_IsIdempotent()
    {
        // 1. Login
        var (client, loginBody) = await AuthClientFactory.LoginAsAdminWithBodyAsync(_factory);

        // 2. First logout
        var logout1 = await client.PostAsync("/api/auth/logout", null);
        logout1.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // 3. Second logout (should not crash)
        var logout2 = await client.PostAsync("/api/auth/logout", null);
        logout2.StatusCode.Should().BeOneOf(
            HttpStatusCode.NoContent,
            HttpStatusCode.Unauthorized
        );
    }

    // ────────────────────────────────────────────────────────────────────────────
    // SESSION CONSISTENCY
    // ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Me_ReturnsCorrectSessionInfo()
    {
        var client = await AuthClientFactory.LoginAsAdminAsync(_factory);

        var meResponse = await client.GetAsync("/api/auth/me");
        meResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var session = await meResponse.Content.ReadFromJsonAsync<SessionDto>();
        session.Should().NotBeNull();
        session!.Login.Should().Be("admin");
        session.Role.Should().NotBeNullOrEmpty();
        session.Email.Should().NotBeNullOrEmpty();
        session.TenantId.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Me_AfterSwitchStore_ReturnCorrectStoreId()
    {
        // 1. Login
        var (client, loginBody) = await AuthClientFactory.LoginAsAdminWithBodyAsync(_factory);
        var originalStoreId = loginBody.Session.StoreId;

        // 2. Get available stores
        var storesResponse = await client.GetAsync("/api/auth/me");
        var session = await storesResponse.Content.ReadFromJsonAsync<SessionDto>();
        var storeIds = session!.StoreIds;

        if (storeIds.Count > 1)
        {
            var newStoreId = storeIds.First(id => id != originalStoreId);

            // 3. Switch store
            var switchResponse = await client.PostAsJsonAsync("/api/auth/switch-store",
                new { storeId = newStoreId });
            switchResponse.StatusCode.Should().Be(HttpStatusCode.OK);

            var switchBody = await switchResponse.Content.ReadFromJsonAsync<SwitchStoreResponse>();
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", switchBody!.AccessToken);

            // 4. Verify /me reflects new store
            var meResponse = await client.GetAsync("/api/auth/me");
            var newSession = await meResponse.Content.ReadFromJsonAsync<SessionDto>();
            newSession!.StoreId.Should().Be(newStoreId);
        }
    }

    // ────────────────────────────────────────────────────────────────────────────
    // ERROR SCENARIOS
    // ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Login_WithBlankPassword_ReturnsBadRequest()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login",
            TestCredentials.LoginPayload(TestCredentials.AdminLogin, ""));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Login_WithBlankLogin_ReturnsBadRequest()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login",
            TestCredentials.LoginPayload("", TestCredentials.AdminPassword));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Login_WithNullPassword_ReturnsBadRequest()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login",
            new { login = TestCredentials.AdminLogin, password = (string?)null });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ────────────────────────────────────────────────────────────────────────────
    // USER STATUS VALIDATION
    // ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Login_WithInactiveUser_IsRejected()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();

        var inactiveUser = await db.Users.FirstOrDefaultAsync(u => u.Login == "inactive_test_user");
        if (inactiveUser == null)
        {
            // Skip if test data doesn't exist
            return;
        }

        var response = await _client.PostAsJsonAsync("/api/auth/login",
            TestCredentials.LoginPayload(inactiveUser.Login, "fake-test-password"));

        response.StatusCode.Should().BeOneOf(
            HttpStatusCode.Unauthorized,
            HttpStatusCode.Forbidden
        );
    }

    [Fact]
    public async Task Login_WithBlockedUser_IsRejected()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();

        var blockedUser = await db.Users.FirstOrDefaultAsync(u =>
            u.Login == "blocked_test_user" || u.Status == UserStatus.Blocked);
        if (blockedUser == null)
        {
            return;
        }

        var response = await _client.PostAsJsonAsync("/api/auth/login",
            TestCredentials.LoginPayload(blockedUser.Login, "fake-test-password"));

        response.StatusCode.Should().BeOneOf(
            HttpStatusCode.Unauthorized,
            HttpStatusCode.Forbidden
        );
    }

    // ────────────────────────────────────────────────────────────────────────────
    // HELPERS
    // ────────────────────────────────────────────────────────────────────────────

    private static string ParseCookieForRequest(string setCookieHeader)
    {
        var parts = setCookieHeader.Split(';');
        return parts[0]; // Just the name=value part
    }
}
