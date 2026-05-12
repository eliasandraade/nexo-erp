using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Nexo.Application.Features.Auth;
using Nexo.IntegrationTests.Helpers;

namespace Nexo.IntegrationTests.Common;

/// <summary>
/// Centralized factory for creating pre-authenticated HttpClients in integration tests.
///
/// Design rules:
///   - Every method creates a fresh HttpClient — no shared auth state between callers.
///   - All credential values come from <see cref="TestCredentials"/> — never inline.
///   - Methods that assert on HTTP status throw <see cref="InvalidOperationException"/>
///     with a clear message so test failures are immediately diagnosable.
/// </summary>
public static class AuthClientFactory
{
    // ── Admin (default seeded tenant) ─────────────────────────────────────────

    /// <summary>
    /// Creates an HttpClient authenticated as the default seeded admin user.
    /// Throws if login fails (precondition failure, not test failure).
    /// </summary>
    public static Task<HttpClient> LoginAsAdminAsync(TestWebApplicationFactory factory)
        => LoginAsync(factory, TestCredentials.AdminLogin, TestCredentials.AdminPassword);

    /// <summary>
    /// Creates an HttpClient authenticated as the default seeded admin,
    /// and also returns the full login response body.
    /// </summary>
    public static Task<(HttpClient Client, LoginResponse Body)> LoginAsAdminWithBodyAsync(
        TestWebApplicationFactory factory)
        => LoginWithBodyAsync(factory, TestCredentials.AdminLogin, TestCredentials.AdminPassword);

    // ── Generic helpers ────────────────────────────────────────────────────────

    /// <summary>
    /// Creates an HttpClient and authenticates with the given credentials.
    /// Sets the Authorization: Bearer header automatically.
    /// Throws <see cref="InvalidOperationException"/> if login returns non-200.
    /// </summary>
    public static async Task<HttpClient> LoginAsync(
        TestWebApplicationFactory factory,
        string login,
        string password)
    {
        var (client, _) = await LoginWithBodyAsync(factory, login, password);
        return client;
    }

    /// <summary>
    /// Authenticates with the given credentials and returns both the client
    /// (with Bearer header set) and the parsed <see cref="LoginResponse"/> body.
    /// </summary>
    public static async Task<(HttpClient Client, LoginResponse Body)> LoginWithBodyAsync(
        TestWebApplicationFactory factory,
        string login,
        string password)
    {
        var client = factory.CreateApiClient();
        var response = await client.PostAsJsonAsync("/api/auth/login",
            TestCredentials.LoginPayload(login, password));

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            $"Login as '{login}' failed — ensure the test DB is seeded correctly.");

        var body = await response.Content.ReadFromJsonAsync<LoginResponse>()
            ?? throw new InvalidOperationException($"Login as '{login}' returned null body.");

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", body.AccessToken);

        return (client, body);
    }

    /// <summary>
    /// Returns a fresh, unauthenticated HttpClient (no Authorization header).
    /// </summary>
    public static HttpClient CreateAnonymousClient(TestWebApplicationFactory factory)
        => factory.CreateApiClient();
}
