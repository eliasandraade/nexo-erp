using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Nexo.Application.Common.Interfaces;
using Nexo.Application.Features.Auth;
using Nexo.Domain.Entities;
using Nexo.Domain.Enums;
using Nexo.Infrastructure.Persistence;
using Nexo.IntegrationTests.Common;
using Nexo.IntegrationTests.Helpers;
using Xunit.Abstractions;
using static Nexo.IntegrationTests.Security.RedisSessionKit;

namespace Nexo.IntegrationTests.Security;

/// <summary>
/// The renewal policy (<c>auth-refresh</c>, docs/security/sessao-auditoria-e-migracao.md, section 4) through the real HTTP
/// pipeline: the real limiter, the production cache service and a real Redis. Every test builds its own host, so the
/// in-memory ceiling starts empty, and has its own client address, so a budget left in Redis by another test never counts.
/// </summary>
[Collection(RedisRealCollection.Name)]
public sealed class RefreshRateLimitingRedisTests : IClassFixture<RedisWebApplicationFactory>, IAsyncLifetime
{
    private const string TenantRoute = "/api/auth/refresh";
    private const string PlatformRoute = "/api/platform/auth/refresh";
    private const string Budget = "RateLimiting:AuthRefresh:";

    private readonly RedisWebApplicationFactory _factory;
    private readonly RedisSessionKit _kit;
    private readonly ITestOutputHelper _output;

    public RefreshRateLimitingRedisTests(RedisWebApplicationFactory factory, ITestOutputHelper output)
    {
        _factory = factory;
        _kit = new RedisSessionKit(factory);
        _output = output;
    }

    public Task InitializeAsync() => _factory.WaitForCacheAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    // ────────────────────────────────────────────────────────────────────────────
    // A STORE BEHIND ONE ADDRESS
    // ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Sixty_sessions_behind_one_address_all_renew_at_once_cycle_after_cycle()
    {
        const int terminals = 60;
        await using var host = Host();
        var account = await SyntheticTenant.CreateAsync(_factory, "varejo");
        var address = NewAddress();
        var tokens = (await SignInManyAsync(account, terminals)).Select(l => l.RefreshToken).ToArray();
        var families = tokens.Select(FamilyOf).ToArray();

        for (var cycle = 1; cycle <= 2; cycle++)
        {
            var answers = await Task.WhenAll(tokens.Select(t => RenewAsync(host, address, t)));

            var statuses = answers.GroupBy(a => a.StatusCode).ToDictionary(g => g.Key, g => g.Count());
            _output.WriteLine($"cycle {cycle}: {string.Join(", ", statuses.Select(s => $"{(int)s.Key}={s.Value}"))}");
            statuses.Should().Equal(new Dictionary<HttpStatusCode, int> { [HttpStatusCode.OK] = terminals },
                "every terminal of a store renews once per access token, and none of them did anything wrong");

            var pairs = await Task.WhenAll(answers.Select(a => a.Content.ReadFromJsonAsync<RefreshResponse>()));
            tokens = pairs.Select(p => p!.RefreshToken).ToArray();
            tokens.Should().OnlyHaveUniqueItems();
        }

        foreach (var family in families)
            (await _factory.RedisDatabase.KeyExistsAsync($"refresh:family:{family}:revoked")).Should().BeFalse("no session of the store was ended");
    }

    // ────────────────────────────────────────────────────────────────────────────
    // REUSE DETECTION STAYS AS IT IS
    // ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task The_same_token_presented_concurrently_rotates_once_and_the_family_survives()
    {
        const int tabs = 20;
        await using var host = Host();
        var (login, account) = await NewSessionAsync();
        var address = NewAddress();
        var family = FamilyOf(login.RefreshToken);
        var sessionsBefore = await OpenSessionsAsync(account.UserId);

        var answers = await Task.WhenAll(Enumerable.Range(0, tabs).Select(_ => RenewAsync(host, address, login.RefreshToken)));

        answers.Count(a => a.StatusCode == HttpStatusCode.OK).Should().Be(1, "the token is single use");
        var losers = answers.Where(a => a.StatusCode != HttpStatusCode.OK).ToList();
        losers.Should().OnlyContain(a => a.StatusCode == HttpStatusCode.Conflict, "the others lost a race: that ends nothing");
        foreach (var loser in losers)
        {
            var body = await loser.Content.ReadFromJsonAsync<JsonElement>();
            body.GetProperty("code").GetString().Should().Be(RefreshErrors.AlreadyRotated);
            body.TryGetProperty("accessToken", out _).Should().BeFalse("whoever loses the race gets no token");
        }

        var winner = (await answers.Single(a => a.StatusCode == HttpStatusCode.OK).Content.ReadFromJsonAsync<RefreshResponse>())!;
        (await _factory.RedisDatabase.KeyExistsAsync($"refresh:family:{family}:revoked")).Should().BeFalse("a race is not a theft");
        (await _factory.RedisDatabase.KeyExistsAsync($"refresh:valid:{JtiOf(login.RefreshToken)}")).Should().BeFalse("the old token is spent");
        (await _factory.RedisDatabase.KeyExistsAsync($"refresh:valid:{JtiOf(winner.RefreshToken)}")).Should().BeTrue();

        var head = JsonDocument.Parse((await _factory.RedisDatabase.StringGetAsync($"refresh:family:{family}")).ToString()).RootElement;
        head.GetProperty("currentJti").GetString().Should().Be(JtiOf(winner.RefreshToken), "exactly one pair was issued");
        head.GetProperty("previousJti").GetString().Should().Be(JtiOf(login.RefreshToken));
        (await OpenSessionsAsync(account.UserId)).Should().Be(sessionsBefore, "no second session came out of the race");

        (await RenewAsync(host, address, winner.RefreshToken)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_spent_token_after_the_grace_period_revokes_the_family_and_audits()
    {
        await using var host = Host();
        var (login, account) = await NewSessionAsync();
        var (_, other) = await _kit.SignInAsync(account);
        var address = NewAddress();
        var rotated = await RenewOkAsync(host, address, login.RefreshToken);
        var family = FamilyOf(login.RefreshToken);
        await AgeLastRotationAsync(family);

        var replay = await RenewAsync(host, address, login.RefreshToken);

        replay.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await replay.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString().Should().Be(RefreshErrors.SessionEnded);
        (await _factory.RedisDatabase.KeyExistsAsync($"refresh:family:{family}:revoked")).Should().BeTrue();
        (await RenewAsync(host, address, rotated.RefreshToken)).StatusCode.Should().Be(HttpStatusCode.Unauthorized, "the whole family ends");
        (await CriticalReuseAuditsAsync(family)).Should().Be(1);
        (await RenewAsync(host, address, other.RefreshToken)).StatusCode.Should().Be(HttpStatusCode.OK, "another session of the person goes on");
    }

    /// <summary>
    /// Whoever holds a copy of the token could otherwise keep the family over budget, so that the owner's old token only ever
    /// meets a 429 and never trips the reuse detection. A spent token is judged before any budget.
    /// </summary>
    [Fact]
    public async Task A_family_over_budget_cannot_hide_a_reused_token()
    {
        await using var host = Host((Budget + "FamilyBudget", "2"));
        var (login, _) = await NewSessionAsync();
        var address = NewAddress();
        var first = await RenewOkAsync(host, address, login.RefreshToken);
        var second = await RenewOkAsync(host, address, first.RefreshToken);
        (await RenewAsync(host, address, second.RefreshToken)).StatusCode.Should().Be(HttpStatusCode.TooManyRequests, "the budget of the family is spent");
        var family = FamilyOf(login.RefreshToken);
        await AgeLastRotationAsync(family);

        var replay = await RenewAsync(host, address, login.RefreshToken);

        replay.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "a token already used, however many renewals came before it, ends the family");
        (await _factory.RedisDatabase.KeyExistsAsync($"refresh:family:{family}:revoked")).Should().BeTrue();
        (await CriticalReuseAuditsAsync(family)).Should().Be(1);
        (await RenewAsync(host, address, second.RefreshToken)).StatusCode.Should().Be(HttpStatusCode.Unauthorized, "an ended family gets the verdict, not a 429");
    }

    // ────────────────────────────────────────────────────────────────────────────
    // FAILURE BUDGET PER ADDRESS
    // ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Invalid_tokens_over_budget_get_429_and_a_valid_session_from_the_same_address_still_renews()
    {
        const int budget = 5;
        await using var host = Host((Budget + "FailureBudget", budget.ToString()));
        var (login, _) = await NewSessionAsync();
        var address = NewAddress();

        var tries = new[]
        {
            BadSignature(), "not-a-jwt", BadSignature(expiredBy: TimeSpan.FromDays(1)), SignedByUs(audience: "nexo-frontend-test"), "a.b.c",
        };
        foreach (var invalid in tries)
        {
            var refused = await RenewAsync(host, address, invalid);
            refused.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "within the budget an invalid token only gets the verdict");
            (await refused.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString().Should().Be(RefreshErrors.Invalid);
        }

        var limited = await RenewAsync(host, address, BadSignature());

        limited.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        var retryAfter = (int)limited.Headers.RetryAfter!.Delta!.Value.TotalSeconds;
        retryAfter.Should().BeInRange(1, 900);
        var body = await limited.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("code").GetString().Should().Be("rate_limited");
        body.GetProperty("retryAfterSeconds").GetInt32().Should().Be(retryAfter);

        (await RenewAsync(host, address, login.RefreshToken)).StatusCode.Should().Be(HttpStatusCode.OK, "a valid token is never affected");
        (await RenewAsync(host, NewAddress(), BadSignature())).StatusCode.Should().Be(HttpStatusCode.Unauthorized, "the budget is per address");
        (await _factory.RedisDatabase.KeyTimeToLiveAsync($"refresh:budget:ip:{address}")).Should().NotBeNull("a counter without a lifetime would block the address for good");
    }

    [Fact]
    public async Task Expired_tokens_never_spend_the_failure_budget_so_a_store_of_expired_terminals_is_not_blocked()
    {
        const int budget = 3;
        const int expiredTerminals = 60;
        await using var host = Host((Budget + "FailureBudget", budget.ToString()));
        var (login, _) = await NewSessionAsync();
        var address = NewAddress();

        // the Monday morning of a store: every terminal comes back with a token that ran out over the weekend
        var answers = await Task.WhenAll(Enumerable.Range(0, expiredTerminals)
            .Select(_ => RenewAsync(host, address, SignedByUs(expiredBy: TimeSpan.FromDays(3)))));

        answers.Should().OnlyContain(a => a.StatusCode == HttpStatusCode.Unauthorized,
            "the verdict is what sends each terminal to the login: a 429 would keep them stuck, because a 429 does not end a session");
        (await _factory.RedisDatabase.KeyExistsAsync($"refresh:budget:ip:{address}")).Should().BeFalse("nothing was counted");

        (await RenewAsync(host, address, login.RefreshToken)).StatusCode.Should().Be(HttpStatusCode.OK);
        for (var i = 0; i < budget; i++)
            (await RenewAsync(host, address, BadSignature())).StatusCode.Should().Be(HttpStatusCode.Unauthorized, "the budget is still whole");
        (await RenewAsync(host, address, BadSignature())).StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        (await RenewAsync(host, address, SignedByUs(expiredBy: TimeSpan.FromDays(3)))).StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "an expired token gets its verdict even with the budget spent: only an invalid token gets the 429");
    }

    [Fact]
    public async Task Races_and_ended_sessions_do_not_spend_the_failure_budget()
    {
        await using var host = Host((Budget + "FailureBudget", "2"));
        var (raced, _) = await NewSessionAsync();
        var (ended, _) = await NewSessionAsync();
        var address = NewAddress();
        await RenewOkAsync(host, address, raced.RefreshToken);
        await _factory.CreateApiClient().WithBearer(ended.AccessToken)
            .PostAsJsonAsync("/api/auth/logout", new { refreshToken = ended.RefreshToken })
            .ShouldHaveStatusAsync(HttpStatusCode.NoContent, _factory);

        for (var i = 0; i < 5; i++)
        {
            (await RenewAsync(host, address, raced.RefreshToken)).StatusCode.Should().Be(HttpStatusCode.Conflict);
            (await RenewAsync(host, address, ended.RefreshToken)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        (await _factory.RedisDatabase.KeyExistsAsync($"refresh:budget:ip:{address}")).Should().BeFalse();
    }

    [Fact]
    public async Task The_failure_budget_is_atomic_under_concurrency()
    {
        const int budget = 10;
        const int attempts = 80;
        await using var host = Host((Budget + "FailureBudget", budget.ToString()));
        var address = NewAddress();

        var answers = await Task.WhenAll(Enumerable.Range(0, attempts).Select(_ => RenewAsync(host, address, BadSignature())));

        answers.Count(a => a.StatusCode == HttpStatusCode.Unauthorized).Should().Be(budget, "no two attempts share a slot");
        answers.Count(a => a.StatusCode == HttpStatusCode.TooManyRequests).Should().Be(attempts - budget);
        ((long)await _factory.RedisDatabase.StringGetAsync($"refresh:budget:ip:{address}")).Should().Be(attempts);
    }

    // ────────────────────────────────────────────────────────────────────────────
    // BUDGET PER SESSION FAMILY
    // ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_family_over_budget_gets_429_stays_alive_leaves_the_others_alone_and_works_after_the_window()
    {
        const int windowSeconds = 6;
        await using var host = Host((Budget + "FamilyBudget", "2"), (Budget + "FamilyWindowSeconds", windowSeconds.ToString()));
        var (looped, _) = await NewSessionAsync();
        var (bystander, _) = await NewSessionAsync();
        var address = NewAddress();
        var family = FamilyOf(looped.RefreshToken);

        var first = await RenewOkAsync(host, address, looped.RefreshToken);
        var current = await RenewOkAsync(host, address, first.RefreshToken);
        var limited = await RenewAsync(host, address, current.RefreshToken);

        limited.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        var retryAfter = (int)limited.Headers.RetryAfter!.Delta!.Value.TotalSeconds;
        retryAfter.Should().BeInRange(1, windowSeconds);
        (await limited.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString().Should().Be("rate_limited");

        (await _factory.RedisDatabase.KeyExistsAsync($"refresh:valid:{JtiOf(current.RefreshToken)}")).Should().BeTrue("the token was not spent");
        (await _factory.RedisDatabase.KeyExistsAsync($"refresh:family:{family}:revoked")).Should().BeFalse("the session stays alive");
        (await RenewAsync(host, address, bystander.RefreshToken)).StatusCode.Should().Be(HttpStatusCode.OK, "another family from the same address is not touched");

        await Task.Delay(TimeSpan.FromSeconds(retryAfter));
        HttpResponseMessage again;
        var deadline = DateTime.UtcNow.AddSeconds(windowSeconds * 2);
        do
            again = await RenewAsync(host, address, current.RefreshToken);
        while (again.StatusCode == HttpStatusCode.TooManyRequests && DateTime.UtcNow < deadline);
        again.StatusCode.Should().Be(HttpStatusCode.OK, "the same token works again as soon as the window ends");
    }

    // ────────────────────────────────────────────────────────────────────────────
    // CEILING PER ADDRESS
    // ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task The_ceiling_stops_a_flood_from_one_address_even_with_valid_tokens()
    {
        await using var host = Host((Budget + "PermitLimit", "3"));
        var (login, _) = await NewSessionAsync();
        var address = NewAddress();

        for (var i = 0; i < 3; i++)
            await RenewAsync(host, address, SignedByUs(expiredBy: TimeSpan.FromDays(1)));

        var limited = await RenewAsync(host, address, login.RefreshToken);

        limited.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        limited.Headers.RetryAfter!.Delta!.Value.TotalSeconds.Should().BeInRange(1, 900);
        (await _factory.RedisDatabase.KeyExistsAsync($"refresh:valid:{JtiOf(login.RefreshToken)}")).Should().BeTrue("the ceiling answers before the token is touched");
        (await RenewAsync(host, NewAddress(), login.RefreshToken)).StatusCode.Should().Be(HttpStatusCode.OK, "the ceiling is per address");
    }

    [Fact]
    public async Task The_production_defaults_are_the_agreed_numbers()
    {
        await using var host = Host();
        var (login, _) = await NewSessionAsync();

        // failure budget: 30
        var bad = NewAddress();
        for (var i = 0; i < 30; i++)
            (await RenewAsync(host, bad, BadSignature())).StatusCode.Should().Be(HttpStatusCode.Unauthorized, $"attempt {i + 1} of 30");
        (await RenewAsync(host, bad, BadSignature())).StatusCode.Should().Be(HttpStatusCode.TooManyRequests);

        // family budget: 60
        var current = login.RefreshToken;
        var terminal = NewAddress();
        for (var i = 0; i < 60; i++)
            current = (await RenewOkAsync(host, terminal, current)).RefreshToken;
        (await RenewAsync(host, terminal, current)).StatusCode.Should().Be(HttpStatusCode.TooManyRequests);

        // ceiling: 900 (the window of this address already holds the 61 calls above)
        var flood = NewAddress();
        var expired = SignedByUs(expiredBy: TimeSpan.FromDays(1));
        foreach (var batch in Enumerable.Range(0, 900).Chunk(30))
        {
            var answers = await Task.WhenAll(batch.Select(_ => RenewAsync(host, flood, expired)));
            answers.Should().OnlyContain(a => a.StatusCode == HttpStatusCode.Unauthorized);
        }
        (await RenewAsync(host, flood, expired)).StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task A_429_of_the_budgets_tells_the_browser_when_to_come_back()
    {
        await using var host = Host((Budget + "FailureBudget", "0"));
        var client = host.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = false });
        client.DefaultRequestHeaders.Add("X-Forwarded-For", NewAddress());
        client.DefaultRequestHeaders.Add("Origin", "https://www.orken.com.br");

        var limited = await client.PostAsJsonAsync(TenantRoute, TestCredentials.RefreshPayload(BadSignature()));

        limited.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        limited.Headers.GetValues("Access-Control-Allow-Origin").Should().Equal("https://www.orken.com.br");
        string.Join(",", limited.Headers.GetValues("Access-Control-Expose-Headers")).Should().Contain("Retry-After");
    }

    // ────────────────────────────────────────────────────────────────────────────
    // REDIS OUT
    // ────────────────────────────────────────────────────────────────────────────

    public enum Outage
    {
        /// <summary>Connection refused, as in a redeploy of the service.</summary>
        Stopped,
        /// <summary>The connection stays open and nothing answers: every call runs out of time.</summary>
        Frozen,
    }

    private Task BeginAsync(Outage outage) => outage == Outage.Stopped ? _factory.StopRedisAsync() : _factory.FreezeRedisAsync();

    private Task EndAsync(Outage outage) => outage == Outage.Stopped ? _factory.StartRedisAsync() : _factory.UnfreezeRedisAsync();

    [Theory]
    [InlineData(Outage.Stopped)]
    [InlineData(Outage.Frozen)]
    public async Task With_redis_out_the_budgets_stop_counting_and_the_ceiling_still_holds(Outage outage)
    {
        const int ceiling = 8;
        await using var host = Host((Budget + "PermitLimit", ceiling.ToString()), (Budget + "FailureBudget", "2"));
        _ = host.Services;
        var address = NewAddress();

        await BeginAsync(outage);
        try
        {
            for (var i = 0; i < ceiling; i++)
                (await RenewAsync(host, address, BadSignature())).StatusCode.Should().Be(HttpStatusCode.Unauthorized,
                    "without Redis nothing counts: an invalid token still gets its verdict, and never a 429 from the budget");

            var limited = await RenewAsync(host, address, BadSignature());
            limited.StatusCode.Should().Be(HttpStatusCode.TooManyRequests, "the ceiling lives in memory");
            limited.Headers.RetryAfter.Should().NotBeNull();
        }
        finally
        {
            await EndAsync(outage);
        }
    }

    /// <summary>
    /// Without Redis the server cannot tell whether a session is alive. A 401 here would be a false verdict: the client ends the
    /// session on a 401, so a blip of the cache, or the 10 s of an open circuit after one slow call, would sign out everyone
    /// who renewed in that moment. The answer is "come back": nothing is issued and nothing ends.
    /// </summary>
    [Theory]
    [InlineData(Outage.Stopped)]
    [InlineData(Outage.Frozen)]
    public async Task With_redis_out_a_live_session_is_told_to_wait_and_renews_when_redis_returns(Outage outage)
    {
        await using var host = Host();
        _ = host.Services;
        var (login, _) = await NewSessionAsync();
        var address = NewAddress();
        // the first request of a host is slow for reasons of its own: what is measured below is the outage
        var (warmUp, _) = await NewSessionAsync();
        await RenewOkAsync(host, address, warmUp.RefreshToken);

        await BeginAsync(outage);
        HttpResponseMessage during;
        var clock = Stopwatch.StartNew();
        try
        {
            during = await RenewAsync(host, address, login.RefreshToken);
            clock.Stop();
        }
        finally
        {
            await EndAsync(outage);
        }

        _output.WriteLine($"{outage}: answered {(int)during.StatusCode} in {clock.ElapsedMilliseconds} ms");
        during.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        clock.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5), "an outage does not hold the terminal");
        during.Headers.RetryAfter!.Delta!.Value.TotalSeconds.Should().BeGreaterThan(0);
        var body = await during.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("code").GetString().Should().Be("session_renewal_unavailable");
        body.TryGetProperty("accessToken", out _).Should().BeFalse();

        await _factory.WaitForCacheAsync(host.Services);
        (await RenewAsync(host, address, login.RefreshToken)).StatusCode.Should().Be(HttpStatusCode.OK, "the session was never in question");
    }

    [Theory]
    [InlineData(Outage.Stopped)]
    [InlineData(Outage.Frozen)]
    public async Task With_redis_out_a_session_the_database_knows_has_ended_still_gets_the_verdict(Outage outage)
    {
        await using var host = Host();
        _ = host.Services;
        var (director, _, target) = await _kit.NewCompanyAsync(UserRole.Gerente);
        var (_, login) = await _kit.SignInAsync(target);
        var address = NewAddress();

        await BeginAsync(outage);
        try
        {
            await _kit.ChangeRoleAsync(director, target, "vendedor");
            (await RenewAsync(host, address, login.RefreshToken)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }
        finally
        {
            await EndAsync(outage);
        }
    }

    // ────────────────────────────────────────────────────────────────────────────
    // PLATFORM SESSIONS
    // ────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(TenantRoute)]
    [InlineData(PlatformRoute)]
    public async Task A_platform_session_over_its_budget_gets_429_stays_alive_and_leaves_the_others_alone(string route)
    {
        await using var host = Host((Budget + "FamilyBudget", "2"));
        var looped = await SignInToPlatformAsync();
        var bystander = await SignInToPlatformAsync();
        var address = NewAddress();

        var first = await RenewPlatformOkAsync(host, address, looped, route);
        var current = await RenewPlatformOkAsync(host, address, first, route);
        var limited = await RenewAsync(host, address, current, route);

        limited.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        limited.Headers.RetryAfter.Should().NotBeNull();
        (await limited.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString().Should().Be("rate_limited");
        (await RenewAsync(host, address, bystander, route)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await PlatformSessionOfAsync(current)).RevokedAt.Should().BeNull("the session stays alive");
    }

    [Theory]
    [InlineData(TenantRoute)]
    [InlineData(PlatformRoute)]
    public async Task Unknown_platform_tokens_spend_the_failure_budget_and_a_valid_one_is_not_affected(string route)
    {
        await using var host = Host((Budget + "FailureBudget", "3"));
        var live = await SignInToPlatformAsync();
        var address = NewAddress();

        for (var i = 0; i < 3; i++)
            (await RenewAsync(host, address, $"p.{Convert.ToHexString(RandomNumberGenerator.GetBytes(32))}", route))
                .StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        (await RenewAsync(host, address, $"p.{Convert.ToHexString(RandomNumberGenerator.GetBytes(32))}", route))
            .StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        (await RenewAsync(host, address, live, route)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_reused_platform_token_still_ends_the_session_and_is_audited_even_over_budget()
    {
        await using var host = Host((Budget + "FamilyBudget", "1"));
        var original = await SignInToPlatformAsync();
        var address = NewAddress();
        var current = await RenewPlatformOkAsync(host, address, original);
        (await RenewAsync(host, address, current, PlatformRoute)).StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        var sessionId = (await PlatformSessionOfAsync(current)).Id;

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
            await db.PlatformSessions.Where(s => s.Id == sessionId)
                .ExecuteUpdateAsync(x => x.SetProperty(s => s.RotatedAt, DateTime.UtcNow.AddMinutes(-5)));
        }

        var replay = await RenewAsync(host, address, original, PlatformRoute);

        replay.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        using var check = _factory.Services.CreateScope();
        var checkDb = check.ServiceProvider.GetRequiredService<NexoDbContext>();
        (await checkDb.PlatformSessions.AsNoTracking().SingleAsync(s => s.Id == sessionId)).RevokedAt.Should().NotBeNull();
        (await CriticalAuditsAsync(AuditActions.PlatformSessionRevoked, sessionId.ToString())).Should().Be(1);
    }

    [Theory]
    [InlineData(Outage.Stopped)]
    [InlineData(Outage.Frozen)]
    public async Task A_platform_session_renews_with_redis_out_because_it_lives_in_the_database(Outage outage)
    {
        await using var host = Host((Budget + "FamilyBudget", "1"));
        _ = host.Services;
        var token = await SignInToPlatformAsync();
        var address = NewAddress();

        await BeginAsync(outage);
        try
        {
            // two renewals over a budget of one: without Redis the budget does not count, and the renewal does not need it
            var next = await RenewPlatformOkAsync(host, address, token);
            await RenewPlatformOkAsync(host, address, next);
        }
        finally
        {
            await EndAsync(outage);
        }
    }

    // ────────────────────────────────────────────────────────────────────────────
    // WHERE THE ADDRESS COMES FROM
    // ────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// A client sends whatever headers it likes. The default source trusts the first X-Forwarded-For entry because Railway's edge
    /// overwrites it (ClientIpResolver): reaching the app with another value is only possible without that edge, which is what
    /// the other sources are for. With them, a changing X-Forwarded-For buys nothing.
    /// </summary>
    [Theory]
    [InlineData("ForwardedFor", true)]
    [InlineData("RealIp", false)]
    [InlineData("Remote", false)]
    public async Task A_spoofed_forwarded_for_only_moves_the_budget_under_the_source_that_trusts_it(string source, bool moves)
    {
        await using var host = Host(("RateLimiting:ClientIpSource", source), (Budget + "FailureBudget", "2"));
        var fixedRealIp = NewAddress();

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 4; i++)
            statuses.Add((await RenewAsync(host, NewAddress(), BadSignature(), realIp: fixedRealIp)).StatusCode);

        statuses.Should().Equal(moves
            ? [HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized]
            : [HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized, HttpStatusCode.TooManyRequests, HttpStatusCode.TooManyRequests]);
    }

    [Theory]
    [InlineData("ForwardedFor", true)]
    [InlineData("RealIp", false)]
    [InlineData("Remote", false)]
    public async Task A_spoofed_forwarded_for_only_moves_the_ceiling_under_the_source_that_trusts_it(string source, bool moves)
    {
        await using var host = Host(("RateLimiting:ClientIpSource", source), (Budget + "PermitLimit", "2"));
        var fixedRealIp = NewAddress();
        var expired = SignedByUs(expiredBy: TimeSpan.FromDays(1));

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 4; i++)
            statuses.Add((await RenewAsync(host, NewAddress(), expired, realIp: fixedRealIp)).StatusCode);

        statuses.Should().Equal(moves
            ? [HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized]
            : [HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized, HttpStatusCode.TooManyRequests, HttpStatusCode.TooManyRequests]);
    }

    [Theory]
    [InlineData("ForwardedFor", true)]
    [InlineData("RealIp", false)]
    [InlineData("Remote", false)]
    public async Task The_login_limit_obeys_the_client_ip_source(string source, bool moves)
    {
        await using var host = Host(("RateLimiting:ClientIpSource", source), ("RateLimiting:AuthLogin:PermitLimit", "2"));
        var fixedRealIp = NewAddress();
        var payload = TestCredentials.LoginPayload($"ninguem-{Guid.NewGuid():N}", "qualquer");

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 4; i++)
        {
            var client = ClientOf(host, NewAddress(), fixedRealIp);
            statuses.Add((await client.PostAsJsonAsync("/api/auth/login", payload)).StatusCode);
        }

        statuses.Should().Equal(moves
            ? [HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized]
            : [HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized, HttpStatusCode.TooManyRequests, HttpStatusCode.TooManyRequests]);
    }

    [Fact]
    public async Task The_first_forwarded_for_entry_is_the_address_whatever_comes_after_it()
    {
        await using var host = Host((Budget + "FailureBudget", "1"));
        var address = NewAddress();

        (await RenewAsync(host, $"{address}, {NewAddress()}", BadSignature())).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await RenewAsync(host, $"{address}, {NewAddress()}", BadSignature())).StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    // ────────────────────────────────────────────────────────────────────────────
    // WHAT THE LOGIN LIMIT DOES TO A STORE (measured, not changed: the number is the owner's decision)
    // ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task The_login_limit_lets_five_terminals_of_a_store_in_per_window()
    {
        const int terminals = 20;
        await using var host = Host();
        var account = await SyntheticTenant.CreateAsync(_factory, "varejo");
        var address = NewAddress();

        var statuses = new List<HttpStatusCode>();
        int? retryAfter = null;
        for (var i = 0; i < terminals; i++)
        {
            var answer = await ClientOf(host, address).PostAsJsonAsync("/api/auth/login", TestCredentials.LoginPayload(account.Login, account.Password));
            statuses.Add(answer.StatusCode);
            if (answer.StatusCode == HttpStatusCode.TooManyRequests) retryAfter ??= (int)answer.Headers.RetryAfter!.Delta!.Value.TotalSeconds;
        }

        var signedIn = statuses.Count(s => s == HttpStatusCode.OK);
        _output.WriteLine($"{terminals} terminals, right password, one address: {signedIn} signed in, {terminals - signedIn} got 429, Retry-After {retryAfter} s");
        signedIn.Should().Be(5);
        statuses.Count(s => s == HttpStatusCode.TooManyRequests).Should().Be(terminals - 5);
        retryAfter.Should().BeInRange(1, 900);
    }

    // ────────────────────────────────────────────────────────────────────────────
    // HELPERS
    // ────────────────────────────────────────────────────────────────────────────

    /// <summary>A host with the production limits on, unless a test asks for other numbers.</summary>
    private WebApplicationFactory<Program> Host(params (string Key, string Value)[] settings)
        => _factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("RateLimiting:AuthLogin:Enabled", "true");
            b.UseSetting(Budget + "Enabled", "true");
            foreach (var (key, value) in settings)
                b.UseSetting(key, value);
        });

    /// <summary>An address nobody else in this run has (TEST-NET-3, RFC 5737).</summary>
    private static string NewAddress()
    {
        var value = Interlocked.Increment(ref s_addresses);
        return $"203.0.{113 + value / 250}.{1 + value % 250}";
    }

    private static int s_addresses;

    private static HttpClient ClientOf(WebApplicationFactory<Program> host, string forwardedFor, string? realIp = null)
    {
        var client = host.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = false });
        client.DefaultRequestHeaders.Add("X-Forwarded-For", forwardedFor);
        if (realIp is not null) client.DefaultRequestHeaders.Add("X-Real-IP", realIp);
        return client;
    }

    private static Task<HttpResponseMessage> RenewAsync(WebApplicationFactory<Program> host, string forwardedFor, string token,
        string route = TenantRoute, string? realIp = null)
        => ClientOf(host, forwardedFor, realIp).PostAsJsonAsync(route, TestCredentials.RefreshPayload(token));

    private static async Task<RefreshResponse> RenewOkAsync(WebApplicationFactory<Program> host, string address, string token)
    {
        var answer = await RenewAsync(host, address, token);
        answer.StatusCode.Should().Be(HttpStatusCode.OK, await answer.Content.ReadAsStringAsync());
        return (await answer.Content.ReadFromJsonAsync<RefreshResponse>())!;
    }

    private async Task<(LoginResponse Login, SyntheticAccount Account)> NewSessionAsync()
    {
        var account = await SyntheticTenant.CreateAsync(_factory, "varejo");
        var (_, login) = await _kit.SignInAsync(account);
        return (login, account);
    }

    private async Task<IReadOnlyList<LoginResponse>> SignInManyAsync(SyntheticAccount account, int sessions)
    {
        var logins = new List<LoginResponse>(sessions);
        for (var i = 0; i < sessions; i++)
            logins.Add((await _kit.SignInAsync(account)).Body);
        return logins;
    }

    private string FamilyOf(string refreshToken)
    {
        using var scope = _factory.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<IJwtTokenService>().ValidateRefreshToken(refreshToken)!.Family;
    }

    /// <summary>Moves the last rotation of the family back past the grace period, without waiting for it.</summary>
    private async Task AgeLastRotationAsync(string family)
    {
        using var scope = _factory.Services.CreateScope();
        var cache = scope.ServiceProvider.GetRequiredService<ICacheService>();
        var key = $"refresh:family:{family}";
        var head = await cache.GetAsync<RefreshFamilyHead>(key);
        head.Should().NotBeNull();
        await cache.SetAsync(key, head! with { RotatedAt = head.RotatedAt - AuthService.RotationGrace - TimeSpan.FromSeconds(1) }, TimeSpan.FromHours(1));
    }

    private Task<int> CriticalReuseAuditsAsync(string family) => CriticalAuditsAsync(AuditActions.UserSessionRevoked, family);

    private async Task<int> CriticalAuditsAsync(string action, string mention)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        var metadata = await db.AuditRecords.IgnoreQueryFilters().AsNoTracking()
            .Where(a => a.ActionType == action && a.Severity == AuditSeverity.Critical)
            .Select(a => a.MetadataJson)
            .ToListAsync();
        return metadata.Count(m => m != null && m.Contains(mention));
    }

    private async Task<int> OpenSessionsAsync(Guid userId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        return await db.UserSessions.IgnoreQueryFilters().AsNoTracking().CountAsync(s => s.UserId == userId && !s.IsRevoked);
    }

    private async Task<string> SignInToPlatformAsync()
    {
        var email = $"renova-{Guid.NewGuid():N}"[..18] + "@platform.test";
        const string password = TestCredentials.PlatformPassword;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
            db.PlatformUsers.Add(PlatformUser.Create(email, scope.ServiceProvider.GetRequiredService<IPasswordHasher>().Hash(password), "super_admin"));
            await db.SaveChangesAsync();
        }
        var login = await _factory.CreateApiClient().PostAsJsonAsync("/api/platform/auth/login", new { email, password });
        login.StatusCode.Should().Be(HttpStatusCode.OK, await login.Content.ReadAsStringAsync());
        return (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("refreshToken").GetString()!;
    }

    private static async Task<string> RenewPlatformOkAsync(WebApplicationFactory<Program> host, string address, string token, string route = PlatformRoute)
    {
        var answer = await RenewAsync(host, address, token, route);
        answer.StatusCode.Should().Be(HttpStatusCode.OK, await answer.Content.ReadAsStringAsync());
        return (await answer.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("refreshToken").GetString()!;
    }

    private async Task<PlatformSession> PlatformSessionOfAsync(string refreshToken)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(refreshToken))).ToLowerInvariant();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        return await db.PlatformSessions.AsNoTracking().SingleAsync(s => s.RefreshTokenHash == hash);
    }

    /// <summary>A refresh token with our own key, as the API issues them: past its expiry, or for another audience.</summary>
    private string SignedByUs(TimeSpan? expiredBy = null, string audience = "nexo-refresh")
    {
        var config = _factory.Services.GetRequiredService<IConfiguration>();
        return Sign(Encoding.UTF8.GetBytes(config["Jwt:Secret"]!), config["Jwt:Issuer"]!, audience, expiredBy);
    }

    /// <summary>A well-formed token with the right claims, signed with a key that is not ours (a new one every call).</summary>
    private string BadSignature(TimeSpan? expiredBy = null)
    {
        var config = _factory.Services.GetRequiredService<IConfiguration>();
        return Sign(RandomNumberGenerator.GetBytes(32), config["Jwt:Issuer"]!, "nexo-refresh", expiredBy);
    }

    private static string Sign(byte[] key, string issuer, string audience, TimeSpan? expiredBy)
    {
        var expires = DateTime.UtcNow + (expiredBy is { } past ? -past : TimeSpan.FromDays(7));
        var id = Guid.NewGuid().ToString();
        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims:
            [
                new Claim(JwtRegisteredClaimNames.Jti, id),
                new Claim("userId", Guid.NewGuid().ToString()),
                new Claim("tenantId", Guid.NewGuid().ToString()),
                new Claim("fid", id),
            ],
            notBefore: expires.AddDays(-7),
            expires: expires,
            signingCredentials: new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
