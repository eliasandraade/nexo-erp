using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nexo.Application.Common.Interfaces;
using Nexo.Application.Features.Auth;
using Nexo.Infrastructure.Cache;
using Nexo.Infrastructure.Persistence;
using Nexo.IntegrationTests.Common;
using Nexo.IntegrationTests.Helpers;
using StackExchange.Redis;

namespace Nexo.IntegrationTests.Security;

/// <summary>
/// What the second independent review proved against a real Redis: a command that times out in the API and still runs in Redis, a rotation that loses the cache
/// midway, and a restart from a snapshot. The claim of a token lives two seconds, and one atomic step is what ends the rotation.
/// </summary>
[Collection(RedisRealCollection.Name)]
public sealed class RenewalSecondReviewRedisTests : IClassFixture<RedisWebApplicationFactory>, IAsyncLifetime
{
    private static readonly TimeSpan ClaimLife = TimeSpan.FromSeconds(2);

    private readonly RedisWebApplicationFactory _factory;

    public RenewalSecondReviewRedisTests(RedisWebApplicationFactory factory) => _factory = factory;

    public Task InitializeAsync() => _factory.WaitForCacheAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private HttpClient Client(WebApplicationFactory<Program>? host = null)
        => (host ?? _factory).CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = false });

    private async Task<HttpResponseMessage> PostAsync(string path, object? body, WebApplicationFactory<Program>? host = null, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        if (body is not null) request.Content = JsonContent.Create(body);
        return await Client(host).SendAsync(request, ct);
    }

    private Task<HttpResponseMessage> RenewAsync(string token, WebApplicationFactory<Program>? host = null, CancellationToken ct = default)
        => PostAsync("/api/auth/refresh", TestCredentials.RefreshPayload(token), host, ct);

    private async Task<string> RotateAsync(string token, WebApplicationFactory<Program>? host = null)
    {
        var renewal = await RenewAsync(token, host);
        renewal.StatusCode.Should().Be(HttpStatusCode.OK, await renewal.Content.ReadAsStringAsync());
        return (await renewal.Content.ReadFromJsonAsync<RefreshResponse>())!.RefreshToken;
    }

    private async Task<LoginResponse> SignInAsync(WebApplicationFactory<Program>? host = null)
    {
        var account = await SyntheticTenant.CreateAsync(_factory, "varejo");
        var response = await PostAsync("/api/auth/login", TestCredentials.LoginPayload(account.Login, account.Password), host);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<LoginResponse>())!;
    }

    private static string JtiOf(string token) => RedisSessionKit.JtiOf(token);

    private string FamilyOf(string token)
    {
        using var scope = _factory.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<IJwtTokenService>().ValidateRefreshToken(token)!.Family;
    }

    private ConnectionMultiplexer Admin()
        => ConnectionMultiplexer.Connect(_factory.Services.GetRequiredService<IConfiguration>()["ConnectionStrings:Redis"] + ",allowAdmin=true");

    private static async Task<string?> CodeAsync(HttpResponseMessage response)
    {
        try { return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("code").GetString(); }
        catch (Exception e) when (e is JsonException or KeyNotFoundException) { return null; }
    }

    private async Task<int> ReuseRowsOfFamilyAsync(string family)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        return (await db.AuditRecords.IgnoreQueryFilters().AsNoTracking().Where(a => a.Severity == AuditSeverity.Critical).Select(a => a.MetadataJson).ToListAsync())
            .Count(m => m != null && m.Contains(family) && m.Contains("refresh_reuse"));
    }

    /// <summary>The time of the rotation moved back: no test here waits out the 30 seconds.</summary>
    private async Task AgeTheRotationAsync(string token)
    {
        using var scope = _factory.Services.CreateScope();
        var cache = scope.ServiceProvider.GetRequiredService<ICacheService>();
        var key = $"refresh:family:{FamilyOf(token)}";
        var head = await cache.GetAsync<RefreshFamilyHead>(key);
        await cache.SetAsync(key, head! with { RotatedAt = DateTime.UtcNow - AuthService.RotationGrace - TimeSpan.FromSeconds(1) }, TimeSpan.FromHours(1));
    }

    private WebApplicationFactory<Program> HostWith(GatedCache.Gate gate)
        => _factory.WithWebHostBuilder(b => b.ConfigureTestServices(services =>
        {
            services.RemoveAll<ICacheService>();
            services.AddScoped<ICacheService>(sp => new GatedCache(ActivatorUtilities.CreateInstance<RedisCacheService>(sp), gate));
        }));

    // ── A claim with no rotation behind it ────────────────────────────────────────────────────────────────────────────

    /// <summary>The proof of the review. The command times out in the API and still runs in Redis: nobody rotated, and the only copy of a live token must not hear otherwise.</summary>
    [Fact]
    public async Task A_stall_during_the_claim_gives_no_409_to_the_only_copy_of_a_live_token()
    {
        var token = (await SignInAsync()).RefreshToken;
        using var admin = Admin();

        await admin.GetServers()[0].ExecuteAsync("CLIENT", "PAUSE", "1500", "WRITE");
        var duringTheStall = await RenewAsync(token);
        duringTheStall.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        duringTheStall.Headers.RetryAfter!.Delta.Should().BeGreaterThan(ClaimLife, "by the time it comes back, the claim that ran late is over");
        await Task.Delay(1700);
        await _factory.WaitForCacheAsync();

        _factory.RedisKeys($"refresh:family:{FamilyOf(token)}").Should().BeEmpty("nobody rotated this token");
        (await RenewAsync(token)).StatusCode.Should().Be(HttpStatusCode.OK, "the claim that ran late is over, and the token was good all along");
    }

    [Fact]
    public async Task A_claim_left_behind_answers_come_back_and_then_it_is_over()
    {
        var token = (await SignInAsync()).RefreshToken;
        await _factory.RedisDatabase.StringSetAsync($"refresh:claim:{JtiOf(token)}", "de-ninguem", ClaimLife);

        var whileItLasts = await RenewAsync(token);

        whileItLasts.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable, "there is no rotation behind it");
        whileItLasts.Headers.RetryAfter.Should().NotBeNull();
        (await CodeAsync(whileItLasts)).Should().Be(RefreshErrors.Unavailable);
        await Task.Delay(ClaimLife + TimeSpan.FromMilliseconds(100));
        (await RenewAsync(token)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>A claim with a rotation behind it is the real race, and that one is still a 409.</summary>
    [Fact]
    public async Task Losing_the_claim_to_a_real_rotation_is_still_a_409()
    {
        var gate = new GatedCache.Gate();
        await using var host = HostWith(gate);
        await _factory.WaitForCacheAsync(host.Services);
        var token = (await SignInAsync(host)).RefreshToken;

        gate.HoldNextCommit();
        var winner = RenewAsync(token, host);
        await gate.CommitReached.WaitAsync(TimeSpan.FromSeconds(10));
        var loser = RenewAsync(token, host);
        await Task.Delay(100);
        gate.ReleaseCommit();

        (await winner).StatusCode.Should().Be(HttpStatusCode.OK);
        var lost = await loser;
        lost.StatusCode.Should().Be(HttpStatusCode.Conflict, await lost.Content.ReadAsStringAsync());
        lost.Headers.RetryAfter.Should().NotBeNull();
    }

    /// <summary>The claim ends by itself, so a winner slower than it finds the rotation already done by someone else. Whoever commits first is the only one.</summary>
    [Fact]
    public async Task A_winner_slower_than_the_claim_leaves_one_live_branch()
    {
        var gate = new GatedCache.Gate();
        await using var host = HostWith(gate);
        await _factory.WaitForCacheAsync(host.Services);
        var token = (await SignInAsync(host)).RefreshToken;
        var liveBefore = _factory.RedisKeys("refresh:valid:*").Count;

        gate.HoldNextCommit();
        var slow = RenewAsync(token, host);
        await gate.CommitReached.WaitAsync(TimeSpan.FromSeconds(10));
        await Task.Delay(ClaimLife + TimeSpan.FromMilliseconds(200));
        var next = await RotateAsync(token, host);
        gate.ReleaseCommit();

        var slowAnswer = await slow;
        slowAnswer.StatusCode.Should().Be(HttpStatusCode.Conflict, "someone else rotated it, and nothing of this one was written");
        _factory.RedisKeys("refresh:valid:*").Count.Should().Be(liveBefore, "one token out, one token in");
        _factory.RedisKeys($"refresh:valid:{JtiOf(token)}").Should().BeEmpty();
        _factory.RedisKeys($"refresh:valid:{JtiOf(next)}").Should().ContainSingle();
        (await RenewAsync(next, host)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>The other order: the first commit lands inside the claim, and whoever comes after the claim finds the token spent.</summary>
    [Fact]
    public async Task A_commit_that_came_too_late_for_a_claim_nobody_took_over_changes_nothing()
    {
        var gate = new GatedCache.Gate();
        await using var host = HostWith(gate);
        await _factory.WaitForCacheAsync(host.Services);
        var token = (await SignInAsync(host)).RefreshToken;

        gate.HoldNextCommit();
        var slow = RenewAsync(token, host);
        await gate.CommitReached.WaitAsync(TimeSpan.FromSeconds(10));
        await Task.Delay(ClaimLife + TimeSpan.FromMilliseconds(200));
        gate.ReleaseCommit();

        var answer = await slow;
        answer.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable, "nothing was decided: the token is still the good one");
        _factory.RedisKeys($"refresh:valid:{JtiOf(token)}").Should().ContainSingle();
        _factory.RedisKeys($"refresh:family:{FamilyOf(token)}").Should().BeEmpty();
        (await RenewAsync(token, host)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>The same stall one step later. When the API gives up on the commit, the commit can no longer apply: the claim is over by then.</summary>
    [Fact]
    public async Task A_commit_that_ran_out_of_time_does_not_apply_when_Redis_comes_back()
    {
        using var admin = Admin();
        var gate = new GatedCache.Gate();
        await using var host = HostWith(gate);
        await _factory.WaitForCacheAsync(host.Services);
        var token = (await SignInAsync(host)).RefreshToken;

        gate.BeforeNextCommit = () => admin.GetServers()[0].ExecuteAsync("CLIENT", "PAUSE", "3500", "WRITE");
        var duringTheStall = await RenewAsync(token, host);

        duringTheStall.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable, await duringTheStall.Content.ReadAsStringAsync());
        await Task.Delay(1500);
        await _factory.WaitForCacheAsync(host.Services);
        _factory.RedisKeys($"refresh:valid:{JtiOf(token)}").Should().ContainSingle("the only copy of the token is still good");
        _factory.RedisKeys($"refresh:family:{FamilyOf(token)}").Should().BeEmpty("and nothing says it was spent");
        (await RenewAsync(token, host)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>A stall shorter than the claim: the commit runs inside it, so the API has to be still there to hear that it did. Otherwise a rotation nobody received.</summary>
    [Fact]
    public async Task A_commit_slowed_down_by_less_than_the_claim_still_gets_its_answer()
    {
        using var admin = Admin();
        var gate = new GatedCache.Gate();
        await using var host = HostWith(gate);
        await _factory.WaitForCacheAsync(host.Services);
        var token = (await SignInAsync(host)).RefreshToken;

        gate.BeforeNextCommit = () => admin.GetServers()[0].ExecuteAsync("CLIENT", "PAUSE", "1200", "WRITE");
        var renewal = await RenewAsync(token, host);

        renewal.StatusCode.Should().Be(HttpStatusCode.OK, await renewal.Content.ReadAsStringAsync());
        var next = (await renewal.Content.ReadFromJsonAsync<RefreshResponse>())!.RefreshToken;
        (await RenewAsync(next, host)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>The store switch rotates the same way, and hears the same answer: with nothing behind the claim, no 409.</summary>
    [Fact]
    public async Task A_store_switch_with_a_claim_left_behind_is_told_to_come_back()
    {
        var signedIn = await SignInAsync();
        await _factory.RedisDatabase.StringSetAsync($"refresh:claim:{JtiOf(signedIn.RefreshToken)}", "de-ninguem", ClaimLife);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/switch-store")
        {
            Content = JsonContent.Create(new { storeId = signedIn.Session.StoreId, refreshToken = signedIn.RefreshToken }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", signedIn.AccessToken);

        var switched = await Client().SendAsync(request);

        switched.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable, await switched.Content.ReadAsStringAsync());
    }

    // ── A client that goes away ───────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_client_gone_before_the_commit_changes_nothing()
    {
        var gate = new GatedCache.Gate();
        await using var host = HostWith(gate);
        await _factory.WaitForCacheAsync(host.Services);
        var token = (await SignInAsync(host)).RefreshToken;
        using var client = new CancellationTokenSource();

        gate.AfterNextClaim = async () =>
        {
            await client.CancelAsync();
            await Task.Delay(50);
        };
        await RenewAsync(token, host, client.Token).ContinueWith(_ => { }, TaskScheduler.Default);
        await gate.ClaimTaken.WaitAsync(TimeSpan.FromSeconds(10));
        await Task.Delay(ClaimLife + TimeSpan.FromMilliseconds(100));

        _factory.RedisKeys($"refresh:valid:{JtiOf(token)}").Should().ContainSingle();
        _factory.RedisKeys($"refresh:family:{FamilyOf(token)}").Should().BeEmpty();
        (await RenewAsync(token, host)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_client_gone_during_the_commit_leaves_the_row_following_the_token()
    {
        var gate = new GatedCache.Gate();
        await using var host = HostWith(gate);
        await _factory.WaitForCacheAsync(host.Services);
        var token = (await SignInAsync(host)).RefreshToken;
        using var client = new CancellationTokenSource();

        gate.HoldNextCommit();
        var renewal = RenewAsync(token, host, client.Token).ContinueWith(_ => { }, TaskScheduler.Default);
        await gate.CommitReached.WaitAsync(TimeSpan.FromSeconds(10));
        await client.CancelAsync();
        await Task.Delay(50);
        gate.ReleaseCommit();
        await renewal;
        await gate.TouchDone(_factory, FamilyOf(token));

        gate.CommitCouldBeCancelled.Should().BeFalse("the request's token does not reach the rotation");
        using var scope = _factory.Services.CreateScope();
        var head = await scope.ServiceProvider.GetRequiredService<ICacheService>().GetAsync<RefreshFamilyHead>($"refresh:family:{FamilyOf(token)}");
        head.Should().NotBeNull();
        _factory.RedisKeys($"refresh:valid:{JtiOf(token)}").Should().BeEmpty();
        _factory.RedisKeys($"refresh:valid:{head!.CurrentJti}").Should().ContainSingle();
        (await scope.ServiceProvider.GetRequiredService<NexoDbContext>().UserSessions.IgnoreQueryFilters().AsNoTracking().CountAsync(s => s.RefreshJti == head.CurrentJti))
            .Should().Be(1, "the row follows the token even though the answer found nobody");
    }

    // ── The cache lost during the rotation ────────────────────────────────────────────────────────────────────────────

    /// <summary>The proof of the review: it answered 200 and left two live branches.</summary>
    [Fact]
    public async Task Losing_the_cache_during_the_rotation_answers_come_back_and_leaves_one_branch()
    {
        var gate = new GatedCache.Gate();
        await using var host = HostWith(gate);
        await _factory.WaitForCacheAsync(host.Services);
        var token = (await SignInAsync(host)).RefreshToken;
        var liveBefore = _factory.RedisKeys("refresh:valid:*").Count;

        gate.NextCommitGetsNoAnswer = true;
        var renewal = await RenewAsync(token, host);

        renewal.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable, await renewal.Content.ReadAsStringAsync());
        renewal.Headers.RetryAfter.Should().NotBeNull();
        _factory.RedisKeys("refresh:valid:*").Count.Should().Be(liveBefore, "no second branch");
        _factory.RedisKeys($"refresh:valid:{JtiOf(token)}").Should().ContainSingle();
        await Task.Delay(ClaimLife + TimeSpan.FromMilliseconds(100));
        (await RenewAsync(token, host)).StatusCode.Should().Be(HttpStatusCode.OK, "the session was fine all along");
    }

    // ── The row of a terminal older than its first expiry ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task During_an_outage_a_terminal_older_than_the_first_expiry_of_its_row_is_told_to_come_back()
    {
        var signedIn = await SignInAsync();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
            var jti = JtiOf(signedIn.RefreshToken);
            await db.UserSessions.IgnoreQueryFilters().Where(s => s.RefreshJti == jti).ExecuteUpdateAsync(x => x.SetProperty(s => s.ExpiresAt, DateTime.UtcNow.AddDays(-1)));
        }
        var current = await RotateAsync(signedIn.RefreshToken);

        await _factory.StopRedisAsync();
        HttpResponseMessage during;
        try { during = await RenewAsync(current); }
        finally { await _factory.StartRedisAsync(); }

        during.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable, await during.Content.ReadAsStringAsync());
    }

    // ── A restart from a snapshot ─────────────────────────────────────────────────────────────────────────────────────

    private static async Task SnapshotNowAsync(ConnectionMultiplexer admin)
    {
        var server = admin.GetServers()[0];
        var before = await server.LastSaveAsync();
        await Task.Delay(1100); // LASTSAVE has a resolution of one second
        await server.SaveAsync(SaveType.BackgroundSave);
        for (var i = 0; i < 100 && await server.LastSaveAsync() == before; i++) await Task.Delay(100);
        (await server.LastSaveAsync()).Should().NotBe(before);
    }

    private async Task CrashAndRestartRedisAsync(ConnectionMultiplexer admin)
    {
        try { admin.GetServers()[0].Shutdown(ShutdownMode.Never); }
        catch (RedisException) { /* the connection dies with it */ }
        await Task.Delay(1000);
        await _factory.StartRedisAsync();
    }

    /// <summary>The honest terminal renewed in the last minute before the crash. The snapshot says its token was never issued, and the row of the session knows better.</summary>
    [Fact]
    public async Task After_a_crash_the_token_the_row_calls_current_raises_no_theft_alarm()
    {
        var signedIn = await SignInAsync();
        var t1 = await RotateAsync(signedIn.RefreshToken);
        using var admin = Admin();
        await SnapshotNowAsync(admin);
        var t2 = await RotateAsync(t1);

        await CrashAndRestartRedisAsync(admin);

        var honest = await RenewAsync(t2);
        honest.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "the session may still end");
        (await ReuseRowsOfFamilyAsync(FamilyOf(t2))).Should().Be(0, "nobody stole anything");
    }

    // ── The thief who rotates first ───────────────────────────────────────────────────────────────────────────────────

    /// <summary>The client no longer signs out by itself after a 409, so this is what keeps the copy detectable: it comes back after the grace, and the server decides.</summary>
    [Fact]
    public async Task A_thief_who_rotated_first_is_caught_when_the_victim_comes_back_after_the_grace()
    {
        var victim = (await SignInAsync()).RefreshToken;
        var thief = await RotateAsync(victim);

        var insideTheGrace = await RenewAsync(victim);
        insideTheGrace.StatusCode.Should().Be(HttpStatusCode.Conflict);
        insideTheGrace.Headers.RetryAfter!.Delta!.Value.Should().BeGreaterThan(TimeSpan.Zero);
        await AgeTheRotationAsync(thief);

        var afterTheGrace = await RenewAsync(victim);

        afterTheGrace.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await CodeAsync(afterTheGrace)).Should().Be(RefreshErrors.SessionEnded);
        _factory.RedisKeys($"refresh:family:{FamilyOf(victim)}:revoked").Should().ContainSingle();
        (await ReuseRowsOfFamilyAsync(FamilyOf(victim))).Should().Be(1);
    }

    [Fact]
    public async Task The_same_thief_loses_the_pair_he_holds()
    {
        var victim = (await SignInAsync()).RefreshToken;
        var thief = await RotateAsync(victim);
        await AgeTheRotationAsync(thief);

        (await RenewAsync(victim)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        (await RenewAsync(thief)).StatusCode.Should().Be(HttpStatusCode.Unauthorized, "the copy ended with the family");
    }

    // ── The gate ──────────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>The real cache, with stops at the two steps of a rotation.</summary>
    private sealed class GatedCache(ICacheService inner, GatedCache.Gate gate) : ICacheService
    {
        public sealed class Gate
        {
            private readonly TaskCompletionSource _commitReached = new(TaskCreationOptions.RunContinuationsAsynchronously);
            private readonly TaskCompletionSource _claimTaken = new(TaskCreationOptions.RunContinuationsAsynchronously);
            private TaskCompletionSource? _nextHold;
            private TaskCompletionSource? _held;

            public Task CommitReached => _commitReached.Task;
            public Task ClaimTaken => _claimTaken.Task;
            public Func<Task>? AfterNextClaim { get; set; }
            public Func<Task>? BeforeNextCommit { get; set; }
            public bool NextCommitGetsNoAnswer { get; set; }
            public bool CommitCouldBeCancelled { get; private set; }

            public void HoldNextCommit() => _nextHold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            public void ReleaseCommit() => _held?.TrySetResult();

            internal async Task AtClaimAsync()
            {
                var hook = AfterNextClaim;
                AfterNextClaim = null;
                if (hook is not null) await hook();
                _claimTaken.TrySetResult();
            }

            /// <returns>False when this commit is to get no answer.</returns>
            internal async Task<bool> AtCommitAsync(CancellationToken ct)
            {
                CommitCouldBeCancelled = ct.CanBeCanceled;
                var before = BeforeNextCommit;
                BeforeNextCommit = null;
                if (before is not null) await before();
                if (NextCommitGetsNoAnswer)
                {
                    NextCommitGetsNoAnswer = false;
                    return false;
                }
                var hold = _nextHold;
                _nextHold = null;
                if (hold is not null) _held = hold;
                _commitReached.TrySetResult();
                if (hold is not null) await hold.Task.WaitAsync(TimeSpan.FromSeconds(30));
                return true;
            }

            /// <summary>The row moves right after the commit, on the same request, with nobody left to wait for it.</summary>
            public async Task TouchDone(RedisWebApplicationFactory factory, string family)
            {
                using var scope = factory.Services.CreateScope();
                var cache = scope.ServiceProvider.GetRequiredService<ICacheService>();
                var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
                for (var attempt = 0; attempt < 100; attempt++)
                {
                    var head = await cache.GetAsync<RefreshFamilyHead>($"refresh:family:{family}");
                    if (head is not null && await db.UserSessions.IgnoreQueryFilters().AsNoTracking().AnyAsync(s => s.RefreshJti == head.CurrentJti)) return;
                    await Task.Delay(50);
                }
            }
        }

        public Task<T?> GetAsync<T>(string key, CancellationToken ct = default) where T : class => inner.GetAsync<T>(key, ct);
        public Task SetAsync<T>(string key, T value, TimeSpan? ttl = null, CancellationToken ct = default) where T : class => inner.SetAsync(key, value, ttl, ct);
        public Task RemoveAsync(string key, CancellationToken ct = default) => inner.RemoveAsync(key, ct);
        public Task<T?> GetOrSetAsync<T>(string key, Func<Task<T?>> factory, TimeSpan? ttl = null, CancellationToken ct = default) where T : class => inner.GetOrSetAsync(key, factory, ttl, ct);
        public Task<bool> ExistsAsync(string key, CancellationToken ct = default) => inner.ExistsAsync(key, ct);
        public Task<long?> IncrementAsync(string key, TimeSpan window, CancellationToken ct = default) => inner.IncrementAsync(key, window, ct);
        public Task SetFlagAsync(string key, TimeSpan ttl, CancellationToken ct = default) => inner.SetFlagAsync(key, ttl, ct);
        public Task<WindowCount?> CountInWindowAsync(string key, TimeSpan window, CancellationToken ct = default) => inner.CountInWindowAsync(key, window, ct);
        public bool IsUnavailable => inner.IsUnavailable;

        public async Task<bool?> TryClaimAsync(CacheClaim claim, CancellationToken ct = default)
        {
            var taken = await inner.TryClaimAsync(claim, ct);
            if (taken == true) await gate.AtClaimAsync();
            return taken;
        }

        public async Task<bool?> CommitClaimAsync(CacheClaim claim, string spentKey, IReadOnlyList<CacheWrite> writes, TimeSpan lifetime, CancellationToken ct = default)
            => await gate.AtCommitAsync(ct) ? await inner.CommitClaimAsync(claim, spentKey, writes, lifetime, ct) : null;
    }
}
