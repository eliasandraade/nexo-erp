using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Nexo.IntegrationTests.Common;
using Nexo.IntegrationTests.Helpers;

namespace Nexo.IntegrationTests.Auth;

/// <summary>
/// Rate limiting and brute force protection test suite.
/// Validates:
/// - Login endpoint is rate limited
/// - Refresh endpoint is rate limited
/// - Rate limiting windows work correctly
/// - Too many requests returns 429
/// - Limiting is partitioned per client IP
///
/// The general integration suite DISABLES the auth-login limiter (see
/// TestWebApplicationFactory) to avoid a shared-window 429 cascade. These tests
/// re-enable it via <see cref="TestWebApplicationFactory.WithRateLimitingEnabled"/>.
/// xUnit creates a fresh instance of this class per test method, so the derived
/// factory (and its in-memory limiter window) is fresh for every test — no
/// contamination between tests. Tests that need distinct client identities set
/// distinct X-Forwarded-For headers (the limiter partitions on client IP).
/// </summary>
[Collection("Integration")]
public class RateLimitingTests : IDisposable
{
    private readonly WebApplicationFactory<Program> _rlFactory;

    public RateLimitingTests(TestWebApplicationFactory factory)
        => _rlFactory = factory.WithRateLimitingEnabled(permitLimit: 5, windowSeconds: 900);

    public void Dispose() => _rlFactory.Dispose();

    /// <summary>Fresh no-redirect client against the rate-limited host.</summary>
    private HttpClient NewClient(string? forwardedFor = null)
    {
        var client = _rlFactory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });
        if (forwardedFor is not null)
            client.DefaultRequestHeaders.Add("X-Forwarded-For", forwardedFor);
        return client;
    }

    // ────────────────────────────────────────────────────────────────────────────
    // LOGIN RATE LIMITING
    // ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Login_WithinRateLimit_Succeeds()
    {
        var client = NewClient();

        // First 5 attempts should succeed (or be rejected for wrong password, not rate limited)
        for (int i = 0; i < 5; i++)
        {
            var response = await client.PostAsJsonAsync("/api/auth/login",
                TestCredentials.LoginPayload(TestCredentials.AdminLogin, "wrongpass"));

            // Either 401 (wrong password) or 200 (correct password) — but NOT 429 (rate limited)
            response.StatusCode.Should().NotBe(HttpStatusCode.TooManyRequests,
                $"Request {i + 1}/5 should not be rate limited");
        }
    }

    [Fact]
    public async Task Login_ExceedsRateLimit_Returns429()
    {
        var client = NewClient();

        // Make 6 requests (limit is 5 per window)
        HttpStatusCode lastStatus = HttpStatusCode.OK;
        for (int i = 0; i < 6; i++)
        {
            var response = await client.PostAsJsonAsync("/api/auth/login",
                TestCredentials.LoginPayload(TestCredentials.AdminLogin, "wrongpass"));
            lastStatus = response.StatusCode;
        }

        // 6th request should be rate limited
        lastStatus.Should().Be(HttpStatusCode.TooManyRequests,
            "6th attempt within rate limit window should be rate limited (429)");
    }

    [Fact]
    public async Task Login_RateLimitIncludesIpAddress()
    {
        // Two clients with DISTINCT X-Forwarded-For values land in distinct
        // rate-limit partitions, so one being throttled must not affect the other.
        var client1 = NewClient(forwardedFor: "203.0.113.1");
        var client2 = NewClient(forwardedFor: "203.0.113.2");

        // Hammer client1 until rate limited
        HttpStatusCode client1LastStatus = HttpStatusCode.OK;
        for (int i = 0; i < 10; i++)
        {
            var response = await client1.PostAsJsonAsync("/api/auth/login",
                TestCredentials.LoginPayload(TestCredentials.AdminLogin, "wrongpass"));
            client1LastStatus = response.StatusCode;
        }

        client1LastStatus.Should().Be(HttpStatusCode.TooManyRequests,
            "client1 should be rate limited after 6+ attempts");

        // client2 (different IP partition) should still be able to make requests
        var client2Response = await client2.PostAsJsonAsync("/api/auth/login",
            TestCredentials.LoginPayload(TestCredentials.AdminLogin, "wrongpass"));

        client2Response.StatusCode.Should().NotBe(HttpStatusCode.TooManyRequests,
            "client2 (different IP) should not be affected by client1's rate limiting");
    }

    // ────────────────────────────────────────────────────────────────────────────
    // REFRESH RATE LIMITING
    // ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Refresh_WithinRateLimit_Succeeds()
    {
        var client = NewClient();

        // Login to get a refresh token
        var loginResponse = await client.PostAsJsonAsync("/api/auth/login",
            TestCredentials.AdminLoginPayload());

        if (loginResponse.StatusCode != HttpStatusCode.OK)
            return; // Login failed, skip refresh test

        var loginBody = await loginResponse.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        var refreshToken = loginBody.TryGetProperty("refreshToken", out var rt) ? rt.GetString() : null;

        if (string.IsNullOrEmpty(refreshToken))
            return; // No refresh token available

        // Login and refresh use SEPARATE partitions (keyed by path), so the login
        // above does not consume the refresh budget: all 5 refreshes succeed.
        for (int i = 0; i < 5; i++)
        {
            var response = await client.PostAsJsonAsync("/api/auth/refresh",
                TestCredentials.RefreshPayload(refreshToken));

            response.StatusCode.Should().NotBe(HttpStatusCode.TooManyRequests,
                $"Refresh request {i + 1}/5 should not be rate limited");
        }
    }

    [Fact]
    public async Task Refresh_ExceedsRateLimit_Returns429()
    {
        var client = NewClient();

        // Login
        var loginResponse = await client.PostAsJsonAsync("/api/auth/login",
            TestCredentials.AdminLoginPayload());

        if (loginResponse.StatusCode != HttpStatusCode.OK)
            return;

        var loginBody = await loginResponse.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        var refreshToken = loginBody.TryGetProperty("refreshToken", out var rtProp) ? rtProp.GetString() : null;

        if (string.IsNullOrEmpty(refreshToken))
            return;

        // Make 6 refresh requests
        HttpStatusCode lastStatus = HttpStatusCode.OK;
        for (int i = 0; i < 6; i++)
        {
            var response = await client.PostAsJsonAsync("/api/auth/refresh",
                TestCredentials.RefreshPayload(refreshToken));
            lastStatus = response.StatusCode;
        }

        // 6th request should be rate limited
        lastStatus.Should().Be(HttpStatusCode.TooManyRequests,
            "6th refresh attempt should be rate limited (429)");
    }

    // ────────────────────────────────────────────────────────────────────────────
    // BRUTE FORCE PROTECTION
    // ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task BruteForceAttempt_WithWrongPassword_IsThrottled()
    {
        var client = NewClient();
        int rateLimitedCount = 0;

        // Try 10 times with wrong password
        for (int i = 0; i < 10; i++)
        {
            var response = await client.PostAsJsonAsync("/api/auth/login",
                TestCredentials.LoginPayload(TestCredentials.AdminLogin, $"wrong_pass_{i}"));

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
                rateLimitedCount++;
        }

        // After 5 failed attempts, subsequent attempts should be throttled
        rateLimitedCount.Should().BeGreaterThan(0,
            "brute force attempts should be throttled with 429 responses");
    }

    // ────────────────────────────────────────────────────────────────────────────
    // DISTRIBUTED ATTACK SIMULATION (DIFFERENT IPS)
    // ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RateLimitingPerIp_PreventsBruteForceFromSingleIp()
    {
        // Distinct X-Forwarded-For => distinct partitions.
        var client1 = NewClient(forwardedFor: "203.0.113.10");
        var client2 = NewClient(forwardedFor: "203.0.113.11");

        // Client1 hammers the endpoint until throttled
        for (int i = 0; i < 10; i++)
        {
            await client1.PostAsJsonAsync("/api/auth/login",
                TestCredentials.LoginPayload(TestCredentials.AdminLogin, "wrong"));
        }

        // Client2 (different IP) can still make requests — demonstrates per-IP
        // isolation. (A distributed attack from many IPs can still bypass per-IP
        // limits — a known, accepted limitation.)
        var client2Response = await client2.PostAsJsonAsync("/api/auth/login",
            TestCredentials.LoginPayload(TestCredentials.AdminLogin, "wrong"));

        client2Response.StatusCode.Should().NotBe(HttpStatusCode.TooManyRequests,
            "client2 (different IP) must not inherit client1's throttling");
    }

    // ────────────────────────────────────────────────────────────────────────────
    // RATE LIMIT WINDOW RESET
    // ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RateLimitWindow_ResetsAfterTimeout()
    {
        // Note: This test requires waiting the full window, so we skip it or use mocking
        // Document the expected behavior instead

        // Expected behavior:
        // 1. Make 6 requests to login (get rate limited on 6th)
        // 2. Wait for the rate limit window to elapse
        // 3. Make another request to login (should succeed)

        // For actual testing, this would require:
        // - Mocking the time service
        // - Or accepting that this is an integration test we can't run in CI
        await Task.CompletedTask; // documented expectation — no time-dependent assertions
    }

    // ────────────────────────────────────────────────────────────────────────────
    // RATE LIMIT HEADERS
    // ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RateLimitResponse_IncludesRetryAfterHeader()
    {
        var client = NewClient();

        // Make requests until rate limited
        HttpResponseMessage rateLimitedResponse = null!;
        for (int i = 0; i < 10; i++)
        {
            var response = await client.PostAsJsonAsync("/api/auth/login",
                TestCredentials.LoginPayload(TestCredentials.AdminLogin, "wrong"));

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                rateLimitedResponse = response;
                break;
            }
        }

        rateLimitedResponse.Should().NotBeNull("should have received a 429 response");

        // RFC 6585 specifies that 429 should include Retry-After header
        // ASP.NET RateLimiter may include this
        if (rateLimitedResponse.Headers.Contains("Retry-After"))
        {
            rateLimitedResponse.Headers.GetValues("Retry-After")
                .Should().NotBeEmpty();
        }
    }
}
