using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Nexo.Application.Features.Auth;
using Nexo.IntegrationTests.Common;
using Nexo.IntegrationTests.Helpers;

namespace Nexo.IntegrationTests.Auth;

/// <summary>
/// Rate limiting and brute force protection test suite.
/// Validates:
/// - Login endpoint is rate limited
/// - Refresh endpoint has its own ceiling (the rest of its policy: RefreshRateLimitingRedisTests)
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
    private readonly TestWebApplicationFactory _factory;
    private readonly WebApplicationFactory<Program> _rlFactory;

    public RateLimitingTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _rlFactory = factory.WithRateLimitingEnabled(permitLimit: 5, windowSeconds: 900);
    }

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
    // CREDENTIAL CHECK: one budget per signed-in user
    // ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CredentialCheck_SpentByOneUser_DoesNotThrottleAnotherUser()
    {
        await using var host = CredentialCheckHost(permitLimit: 2);
        var spender = await SignedInClientAsync(host);
        var other = await SignedInClientAsync(host);

        (await CheckCredentialAsync(spender)).StatusCode.Should().NotBe(HttpStatusCode.TooManyRequests);
        (await CheckCredentialAsync(spender)).StatusCode.Should().NotBe(HttpStatusCode.TooManyRequests);
        (await CheckCredentialAsync(spender)).StatusCode.Should().Be(HttpStatusCode.TooManyRequests,
            "the third attempt is over this user's own budget");

        (await CheckCredentialAsync(other)).StatusCode.Should().NotBe(HttpStatusCode.TooManyRequests,
            "another user, in another company, has not attempted anything yet");
    }

    [Fact]
    public async Task CredentialCheck_AnonymousCalls_DoNotThrottleASignedInUser()
    {
        await using var host = CredentialCheckHost(permitLimit: 2);
        var anonymous = host.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var signedIn = await SignedInClientAsync(host);

        for (var i = 0; i < 3; i++)
            await CheckCredentialAsync(anonymous);

        (await CheckCredentialAsync(signedIn)).StatusCode.Should().NotBe(HttpStatusCode.TooManyRequests,
            "calls without a session must not spend the budget of someone who has one");
    }

    private WebApplicationFactory<Program> CredentialCheckHost(int permitLimit)
        => _factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("RateLimiting:CredentialCheck:Enabled", "true");
            b.UseSetting("RateLimiting:CredentialCheck:PermitLimit", permitLimit.ToString());
            b.UseSetting("RateLimiting:CredentialCheck:WindowSeconds", "900");
        });

    private async Task<HttpClient> SignedInClientAsync(WebApplicationFactory<Program> host)
    {
        var account = await SyntheticTenant.CreateAsync(_factory, "varejo");
        var (_, login) = await AuthClientFactory.LoginWithBodyAsync(_factory, account.Login, account.Password);
        var client = host.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
        return client;
    }

    private static Task<HttpResponseMessage> CheckCredentialAsync(HttpClient client, string path = "/api/auth/verify-manager")
        => client.PostAsJsonAsync(path, new VerifyManagerRequest("ninguem", "senha-errada"));

    // Routing ignores letter case and a trailing slash: every spelling reaches the same action, so every spelling spends the same budget.
    [Theory]
    [InlineData("/api/auth/Verify-Manager")]
    [InlineData("/API/AUTH/VERIFY-MANAGER")]
    [InlineData("/api/auth/verify-manager/")]
    public async Task CredentialCheck_AnotherSpellingOfTheRoute_SpendsTheSameBudget(string otherSpelling)
    {
        await using var host = CredentialCheckHost(permitLimit: 2);
        var user = await SignedInClientAsync(host);

        (await CheckCredentialAsync(user)).StatusCode.Should().NotBe(HttpStatusCode.TooManyRequests);
        (await CheckCredentialAsync(user)).StatusCode.Should().NotBe(HttpStatusCode.TooManyRequests);

        var response = await CheckCredentialAsync(user, otherSpelling);
        response.StatusCode.Should().NotBe(HttpStatusCode.NotFound, "the spelling must reach the same action, or this test proves nothing");
        response.StatusCode.Should().Be(HttpStatusCode.TooManyRequests, "another spelling of the route is the same route");
    }

    [Theory]
    [InlineData("/api/auth/Login")]
    [InlineData("/API/AUTH/LOGIN")]
    [InlineData("/api/auth/login/")]
    public async Task Login_AnotherSpellingOfTheRoute_SpendsTheSameBudget(string otherSpelling)
    {
        var client = NewClient(forwardedFor: "203.0.113.40");
        var payload = TestCredentials.LoginPayload(TestCredentials.AdminLogin, "wrongpass");

        for (var i = 0; i < 5; i++)
            (await client.PostAsJsonAsync("/api/auth/login", payload)).StatusCode.Should().NotBe(HttpStatusCode.TooManyRequests);

        var response = await client.PostAsJsonAsync(otherSpelling, payload);
        response.StatusCode.Should().NotBe(HttpStatusCode.NotFound, "the spelling must reach the same action, or this test proves nothing");
        response.StatusCode.Should().Be(HttpStatusCode.TooManyRequests, "another spelling of the route is the same route");
    }

    // ────────────────────────────────────────────────────────────────────────────
    // PUBLIC PORTALS: the same reading of the client address as every other limit
    // ────────────────────────────────────────────────────────────────────────────

    private WebApplicationFactory<Program> PublicPortalsHost(int permitLimit)
        => _factory.WithWebHostBuilder(b =>
        {
            foreach (var section in new[] { "PublicOrders", "PublicBooking" })
            {
                b.UseSetting($"RateLimiting:{section}:Enabled", "true");
                b.UseSetting($"RateLimiting:{section}:PermitLimit", permitLimit.ToString());
                b.UseSetting($"RateLimiting:{section}:WindowSeconds", "900");
            }
        });

    private static HttpClient PortalVisitor(WebApplicationFactory<Program> host, string? forwardedFor)
    {
        var client = host.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        if (forwardedFor is not null)
            client.DefaultRequestHeaders.TryAddWithoutValidation("X-Forwarded-For", forwardedFor);
        return client;
    }

    // Two anonymous routes that answer without touching anything: a coupon of a shop that does not exist, an appointment that does not exist.
    private static Task<HttpResponseMessage> AskPublicOrders(HttpClient client)
        => client.PostAsJsonAsync("/api/public/coupons/validate", new { publicSlug = "loja-que-nao-existe", code = "NADA", subtotal = 10m });

    private static Task<HttpResponseMessage> AskPublicBooking(HttpClient client)
        => client.GetAsync($"/api/public/service/loja-que-nao-existe/appointments/{new string('a', 32)}");

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PublicPortals_LimitByClientAddress_AndAnotherAddressKeepsItsOwnBudget(bool orders)
    {
        await using var host = PublicPortalsHost(permitLimit: 2);
        Func<HttpClient, Task<HttpResponseMessage>> ask = orders ? AskPublicOrders : AskPublicBooking;
        var visitor = PortalVisitor(host, "203.0.113.60");

        (await ask(visitor)).StatusCode.Should().NotBe(HttpStatusCode.TooManyRequests);
        (await ask(visitor)).StatusCode.Should().NotBe(HttpStatusCode.TooManyRequests);
        (await ask(visitor)).StatusCode.Should().Be(HttpStatusCode.TooManyRequests, "the third call is over this address's budget");

        (await ask(PortalVisitor(host, "203.0.113.61"))).StatusCode.Should().NotBe(HttpStatusCode.TooManyRequests,
            "another address has not called yet");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PublicPortals_AHeaderThatIsNotAnAddress_BuysNoBudgetOfItsOwn(bool orders)
    {
        await using var host = PublicPortalsHost(permitLimit: 2);
        Func<HttpClient, Task<HttpResponseMessage>> ask = orders ? AskPublicOrders : AskPublicBooking;

        // Each of these used to be a partition of its own. None holds an address, so all of them are the connection's address.
        (await ask(PortalVisitor(host, "qualquer-coisa-1"))).StatusCode.Should().NotBe(HttpStatusCode.TooManyRequests);
        (await ask(PortalVisitor(host, "qualquer-coisa-2"))).StatusCode.Should().NotBe(HttpStatusCode.TooManyRequests);
        (await ask(PortalVisitor(host, "qualquer-coisa-3"))).StatusCode.Should().Be(HttpStatusCode.TooManyRequests,
            "text that is not an address does not start a new budget");
        (await ask(PortalVisitor(host, null))).StatusCode.Should().Be(HttpStatusCode.TooManyRequests,
            "and it is the same budget as a call with no header at all");
    }

    [Fact]
    public async Task Login_Budget_IsNotSharedWithAnotherRouteOfTheSamePolicy()
    {
        var client = NewClient(forwardedFor: "203.0.113.41");
        var payload = TestCredentials.LoginPayload(TestCredentials.AdminLogin, "wrongpass");
        for (var i = 0; i < 6; i++)
            await client.PostAsJsonAsync("/api/auth/login", payload);

        var other = await client.PostAsJsonAsync("/api/auth/forgot-password", new { email = "ninguem@orken.test" });
        other.StatusCode.Should().NotBe(HttpStatusCode.TooManyRequests, "each route keeps a budget of its own, as before");
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
    public async Task RateLimited_Response_HasRetryAfter_JsonBody_And_ExposesItThroughCors()
    {
        var client = NewClient(forwardedFor: "203.0.113.9");
        client.DefaultRequestHeaders.Add("Origin", "https://www.orken.com.br");

        HttpResponseMessage response = null!;
        for (int i = 0; i < 6; i++)
            response = await client.PostAsJsonAsync("/api/auth/login",
                TestCredentials.LoginPayload(TestCredentials.AdminLogin, "wrongpass"));

        response.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        response.Headers.RetryAfter.Should().NotBeNull();
        var retryAfter = (int)response.Headers.RetryAfter!.Delta!.Value.TotalSeconds;
        retryAfter.Should().BeInRange(1, 900, "the time left in the 900 s window of this policy");

        var body = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        body.GetProperty("code").GetString().Should().Be("rate_limited");
        body.GetProperty("retryAfterSeconds").GetInt32().Should().Be(retryAfter);
        body.GetProperty("error").GetString().Should().NotBeNullOrWhiteSpace();

        response.Headers.GetValues("Access-Control-Allow-Origin").Should().Equal("https://www.orken.com.br");
        string.Join(",", response.Headers.GetValues("Access-Control-Expose-Headers")).Should().Contain("Retry-After");
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
    // PLATFORM LOGIN RATE LIMITING
    // ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PlatformLogin_ExceedsRateLimit_Returns429()
    {
        var client = NewClient(forwardedFor: "203.0.113.50");

        HttpStatusCode lastStatus = HttpStatusCode.OK;
        for (int i = 0; i < 6; i++)
        {
            var response = await client.PostAsJsonAsync("/api/platform/auth/login",
                new { email = "nobody@nexo.test", password = "wrongpass" });
            lastStatus = response.StatusCode;
        }

        lastStatus.Should().Be(HttpStatusCode.TooManyRequests,
            "platform login must be rate limited (6th attempt within the window → 429)");
    }

    [Fact]
    public async Task PlatformLogin_HasSeparateBucketFromTenantLogin()
    {
        // Same client IP, but the limiter partitions on (IP | path): exhausting the
        // /platform/auth/login bucket must NOT throttle the tenant /auth/login path.
        var client = NewClient(forwardedFor: "203.0.113.51");

        for (int i = 0; i < 8; i++)
            await client.PostAsJsonAsync("/api/platform/auth/login",
                new { email = "nobody@nexo.test", password = "wrongpass" });

        var tenant = await client.PostAsJsonAsync("/api/auth/login",
            TestCredentials.LoginPayload(TestCredentials.AdminLogin, "wrongpass"));

        tenant.StatusCode.Should().NotBe(HttpStatusCode.TooManyRequests,
            "tenant login is a different path partition; platform-login attempts must not limit it");
    }

    // ────────────────────────────────────────────────────────────────────────────
    // REFRESH RATE LIMITING
    // ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Refresh_DoesNotShareTheLoginLimit()
    {
        // Same address and a spent login limit: the renewal has a ceiling of its own, far above this number of calls.
        var client = NewClient(forwardedFor: "203.0.113.60");
        var loginResponse = await client.PostAsJsonAsync("/api/auth/login", TestCredentials.AdminLoginPayload());
        loginResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var loginBody = await loginResponse.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        var refreshToken = loginBody.GetProperty("refreshToken").GetString()!;
        for (int i = 0; i < 5; i++)
            await client.PostAsJsonAsync("/api/auth/login", TestCredentials.LoginPayload(TestCredentials.AdminLogin, "wrongpass"));

        for (int i = 0; i < 6; i++)
        {
            var response = await client.PostAsJsonAsync("/api/auth/refresh", TestCredentials.RefreshPayload(refreshToken));
            response.StatusCode.Should().NotBe(HttpStatusCode.TooManyRequests, $"Refresh request {i + 1}/6 is under the ceiling");
        }
    }

    [Fact]
    public async Task Refresh_ExceedsCeiling_Returns429()
    {
        await using var host = _factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("RateLimiting:AuthRefresh:Enabled", "true");
            b.UseSetting("RateLimiting:AuthRefresh:PermitLimit", "5");
        });
        var client = host.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        HttpStatusCode lastStatus = HttpStatusCode.OK;
        for (int i = 0; i < 6; i++)
        {
            var response = await client.PostAsJsonAsync("/api/auth/refresh", TestCredentials.RefreshPayload(TestCredentials.MalformedToken));
            lastStatus = response.StatusCode;
        }

        lastStatus.Should().Be(HttpStatusCode.TooManyRequests, "6th refresh attempt is over this host's ceiling of 5");
    }

    /// <summary>The Redis budgets on this suite's cache, which is what the shared integration host and the end-to-end host use.</summary>
    [Fact]
    public async Task Refresh_FailureBudget_WorksOnTheInMemoryCacheToo()
    {
        await using var host = _factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("RateLimiting:AuthRefresh:Enabled", "true");
            b.UseSetting("RateLimiting:AuthRefresh:FailureBudget", "2");
        });
        var client = host.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add("X-Forwarded-For", "203.0.113.61");

        var statuses = new List<HttpStatusCode>();
        for (int i = 0; i < 3; i++)
            statuses.Add((await client.PostAsJsonAsync("/api/auth/refresh", TestCredentials.RefreshPayload(TestCredentials.MalformedToken))).StatusCode);

        statuses.Should().Equal(HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized, HttpStatusCode.TooManyRequests);
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
        // Janela curta num host próprio: o relógio é o de verdade. Login que não existe: não conta contra conta nenhuma.
        using var shortWindow = _factory.WithRateLimitingEnabled(permitLimit: 2, windowSeconds: 3);
        var client = shortWindow.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var payload = TestCredentials.LoginPayload($"ninguem-{Guid.NewGuid():N}", "qualquer");
        var deadline = DateTime.UtcNow.AddSeconds(30);

        // A primeira chamada do host é lenta e pode gastar a janela inteira: insiste até ver o limite.
        HttpResponseMessage response;
        do
            response = await client.PostAsJsonAsync("/api/auth/login", payload);
        while (response.StatusCode != HttpStatusCode.TooManyRequests && DateTime.UtcNow < deadline);
        response.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);

        var retryAfter = int.Parse(response.Headers.GetValues("Retry-After").Single());
        retryAfter.Should().BeInRange(1, 3, "é o tempo que falta numa janela de 3 segundos");

        await Task.Delay(TimeSpan.FromSeconds(retryAfter));
        response = await client.PostAsJsonAsync("/api/auth/login", payload);
        while (response.StatusCode == HttpStatusCode.TooManyRequests && DateTime.UtcNow < deadline)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(200));
            response = await client.PostAsJsonAsync("/api/auth/login", payload);
        }

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "a janela reabriu: a tentativa chega ao login e é recusada por ser inválida");
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

        // RateLimitRejection: Retry-After é o tempo que falta, em segundos, na janela de 900 segundos deste host
        int.Parse(rateLimitedResponse.Headers.GetValues("Retry-After").Single()).Should().BeInRange(1, 900);
    }
}
