using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nexo.Application.Common.Interfaces;
using Nexo.Application.Features.Auth;
using Nexo.Infrastructure.Persistence;
using Nexo.IntegrationTests.Auth;
using Nexo.IntegrationTests.Common;
using Nexo.IntegrationTests.Helpers;
using static Nexo.IntegrationTests.Auth.BrowserSessionKit;

namespace Nexo.IntegrationTests.Security;

/// <summary>
/// The rotation of the cookie against a real Redis, through the real pipeline: use once under concurrency, reuse detection, the two
/// budgets of the renewal policy (section 4 of docs/security/sessao-auditoria-e-migracao.md) and a refused Origin that leaves nothing
/// behind. The rules are those of the token in the body: only where the token comes from changes.
/// </summary>
[Collection(RedisRealCollection.Name)]
public sealed class SessionCookieRedisTests : IClassFixture<RedisWebApplicationFactory>, IAsyncLifetime
{
    private const string Budget = "RateLimiting:AuthRefresh:";
    private static int s_addresses;

    private readonly RedisWebApplicationFactory _factory;
    private readonly BrowserSessionKit _kit;

    public SessionCookieRedisTests(RedisWebApplicationFactory factory)
    {
        _factory = factory;
        _kit = new BrowserSessionKit(factory);
    }

    public Task InitializeAsync() => _factory.WaitForCacheAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Tabs_renewing_at_once_with_the_same_cookie_rotate_once_and_the_family_lives()
    {
        var session = await SignInAsync();

        var answers = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => _kit.RenewAsync(session.Cookie)));

        answers.Count(a => a.StatusCode == HttpStatusCode.OK).Should().Be(1, "a token works once");
        answers.Where(a => a.StatusCode != HttpStatusCode.OK).Should().OnlyContain(a => a.StatusCode == HttpStatusCode.Conflict);
        foreach (var loser in answers.Where(a => a.StatusCode == HttpStatusCode.Conflict))
        {
            ShouldSetNoCookie(loser);
            (await CodeOfAsync(loser)).Should().Be(RefreshErrors.AlreadyRotated);
        }

        var winner = answers.Single(a => a.StatusCode == HttpStatusCode.OK);
        (await _kit.RenewAsync(CookieValue(winner))).StatusCode.Should().Be(HttpStatusCode.OK, "the race does not end the session");
        _factory.RedisKeys($"refresh:family:{FamilyOf(session.Cookie)}:revoked").Should().BeEmpty();
    }

    [Fact]
    public async Task A_refused_origin_leaves_nothing_in_Redis()
    {
        var session = await SignInAsync();
        var jti = RedisSessionKit.JtiOf(session.Cookie);

        await ShouldBeRefusedByOriginAsync(await _kit.RenewAsync(session.Cookie, "https://evil.orken.com.br"));

        _factory.RedisKeys($"refresh:claim:{jti}").Should().BeEmpty("nothing reserved the token");
        _factory.RedisKeys($"refresh:valid:{jti}").Should().ContainSingle("it is still the token of the session");
        _factory.RedisKeys($"refresh:family:{FamilyOf(session.Cookie)}*").Should().BeEmpty("nothing rotated and nothing was revoked");
    }

    [Fact]
    public async Task The_cookie_alone_with_no_header_leaves_nothing_in_Redis()
    {
        var session = await SignInAsync();
        var jti = RedisSessionKit.JtiOf(session.Cookie);

        (await _kit.PostAsync("/api/auth/refresh", sessionCookie: session.Cookie, mode: null)).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        _factory.RedisKeys($"refresh:claim:{jti}").Should().BeEmpty();
        _factory.RedisKeys($"refresh:valid:{jti}").Should().ContainSingle();
    }

    [Fact]
    public async Task A_spent_cookie_after_the_grace_ends_the_family_and_goes_to_the_audit()
    {
        var session = await SignInAsync();
        var rotated = await _kit.RenewAsync(session.Cookie);
        rotated.StatusCode.Should().Be(HttpStatusCode.OK);
        await AgeLastRotationAsync(session.Cookie);

        var replay = await _kit.RenewAsync(session.Cookie);

        replay.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        ShouldSetNoCookie(replay);
        (await _kit.RenewAsync(CookieValue(rotated))).StatusCode.Should().Be(HttpStatusCode.Unauthorized, "the whole family ends");

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        var family = FamilyOf(session.Cookie);
        var critical = await db.AuditRecords.IgnoreQueryFilters().AsNoTracking()
            .Where(a => a.ActionType == AuditActions.UserSessionRevoked && a.Severity == AuditSeverity.Critical)
            .Select(a => a.MetadataJson).ToListAsync();
        critical.Should().Contain(m => m != null && m.Contains(family));
    }

    [Fact]
    public async Task A_spent_cookie_inside_the_grace_is_a_race_and_nothing_ends()
    {
        var session = await SignInAsync();
        var rotated = await _kit.RenewAsync(session.Cookie);

        var late = await _kit.RenewAsync(session.Cookie);

        late.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await _kit.RenewAsync(CookieValue(rotated))).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task The_budget_of_the_session_counts_renewals_through_the_cookie()
    {
        await using var host = Host((Budget + "FamilyBudget", "3"));
        var kit = _kit.On(host);
        var address = NewAddress();
        var session = await SignInAsync();
        var cookie = session.Cookie;

        for (var renewal = 0; renewal < 3; renewal++)
        {
            var renewed = await kit.RenewAsync(cookie, clientIp: address);
            renewed.StatusCode.Should().Be(HttpStatusCode.OK, await renewed.Content.ReadAsStringAsync());
            cookie = CookieValue(renewed);
        }

        var over = await kit.RenewAsync(cookie, clientIp: address);

        over.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        over.Headers.GetValues("Retry-After").Single().Should().NotBeNullOrEmpty();
        ShouldSetNoCookie(over);
        (await CodeOfAsync(over)).Should().Be("rate_limited");
        _factory.RedisKeys($"refresh:valid:{RedisSessionKit.JtiOf(cookie)}").Should().ContainSingle("the session stays: only the renewal waits");
    }

    [Fact]
    public async Task A_forged_cookie_spends_the_failure_budget_of_the_address()
    {
        await using var host = Host((Budget + "FailureBudget", "2"));
        var kit = _kit.On(host);
        var address = NewAddress();
        var forged = "forjado-" + Guid.NewGuid().ToString("N");

        (await kit.RenewAsync(forged, clientIp: address)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await kit.RenewAsync(forged, clientIp: address)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var over = await kit.RenewAsync(forged, clientIp: address);

        over.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task A_request_with_no_token_at_all_spends_no_budget()
    {
        await using var host = Host((Budget + "FailureBudget", "1"));
        var kit = _kit.On(host);
        var address = NewAddress();

        for (var visit = 0; visit < 5; visit++)
            (await kit.PostAsync("/api/auth/refresh", clientIp: address)).StatusCode.Should().Be(HttpStatusCode.Unauthorized,
                "someone arriving with no session, not someone trying tokens");
    }

    [Fact]
    public async Task A_platform_session_rotates_once_under_concurrency_through_the_cookie()
    {
        var (email, password) = await _kit.NewPlatformAdminAsync();
        var session = await _kit.SignInAsync(email, password);

        var answers = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => _kit.RenewAsync(session.Cookie)));

        answers.Count(a => a.StatusCode == HttpStatusCode.OK).Should().Be(1);
        answers.Where(a => a.StatusCode != HttpStatusCode.OK).Should().OnlyContain(a => a.StatusCode == HttpStatusCode.Conflict);
        (await _kit.RenewAsync(CookieValue(answers.Single(a => a.StatusCode == HttpStatusCode.OK)))).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_spent_platform_cookie_after_the_grace_ends_the_session()
    {
        var (email, password) = await _kit.NewPlatformAdminAsync();
        var session = await _kit.SignInAsync(email, password);
        var rotated = await _kit.RenewAsync(session.Cookie);
        rotated.StatusCode.Should().Be(HttpStatusCode.OK);
        var next = CookieValue(rotated);
        await AgePlatformRotationAsync(next);

        (await _kit.RenewAsync(session.Cookie)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        (await _kit.RenewAsync(next)).StatusCode.Should().Be(HttpStatusCode.Unauthorized, "the session ended");
    }

    [Fact]
    public async Task Without_Redis_the_renewal_through_the_cookie_waits_and_the_cookie_stays()
    {
        var session = await SignInAsync();

        await _factory.FreezeRedisAsync();
        try
        {
            var unavailable = await _kit.RenewAsync(session.Cookie);

            unavailable.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable, await unavailable.Content.ReadAsStringAsync());
            unavailable.Headers.GetValues("Retry-After").Single().Should().NotBeNullOrEmpty();
            ShouldSetNoCookie(unavailable);
        }
        finally
        {
            await _factory.UnfreezeRedisAsync();
        }

        (await _kit.RenewAsync(session.Cookie)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private WebApplicationFactory<Program> Host(params (string Key, string Value)[] settings)
        => _factory.WithWebHostBuilder(b =>
        {
            b.UseSetting(Budget + "Enabled", "true");
            foreach (var (key, value) in settings)
                b.UseSetting(key, value);
        });

    private async Task<CookieSession> SignInAsync()
    {
        var account = await _kit.NewAccountAsync();
        return await _kit.SignInAsync(account.Login, account.Password);
    }

    private string FamilyOf(string refreshToken)
    {
        using var scope = _factory.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<IJwtTokenService>().ValidateRefreshToken(refreshToken)!.Family;
    }

    /// <summary>Moves the last rotation of the family back in time: the grace goes by without the wait.</summary>
    private async Task AgeLastRotationAsync(string anyTokenOfTheFamily)
    {
        using var scope = _factory.Services.CreateScope();
        var cache = scope.ServiceProvider.GetRequiredService<ICacheService>();
        var key = $"refresh:family:{FamilyOf(anyTokenOfTheFamily)}";
        var head = await cache.GetAsync<RefreshFamilyHead>(key);
        head.Should().NotBeNull();
        await cache.SetAsync(key, head! with { RotatedAt = head.RotatedAt - AuthService.RotationGrace - TimeSpan.FromSeconds(1) }, TimeSpan.FromHours(1));
    }

    private async Task AgePlatformRotationAsync(string currentToken)
    {
        var hash = Nexo.Infrastructure.Auth.PlatformAuthService.Hash(currentToken);
        var past = DateTime.UtcNow - Nexo.Infrastructure.Auth.PlatformAuthService.RotationGrace - TimeSpan.FromSeconds(1);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        await db.PlatformSessions.Where(s => s.RefreshTokenHash == hash).ExecuteUpdateAsync(x => x.SetProperty(s => s.RotatedAt, past));
    }

    /// <summary>An address nobody else in this run has (TEST-NET-2, RFC 5737).</summary>
    private static string NewAddress()
    {
        var value = Interlocked.Increment(ref s_addresses);
        return $"198.51.{100 + value / 250}.{1 + value % 250}";
    }
}
