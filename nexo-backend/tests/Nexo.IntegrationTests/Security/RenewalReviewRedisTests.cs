using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nexo.Application.Common.Interfaces;
using Nexo.Application.Features.Auth;
using Nexo.Domain.Entities;
using Nexo.Infrastructure.Persistence;
using Nexo.IntegrationTests.Common;
using Nexo.IntegrationTests.Helpers;
using StackExchange.Redis;

namespace Nexo.IntegrationTests.Security;

/// <summary>
/// What only shows against a real Redis: a sign-out heard during an outage has to hold after the keys come back from the snapshot, and the
/// reservation of a token must not outlive the crash of whoever took it.
/// </summary>
[Collection(RedisRealCollection.Name)]
public sealed class RenewalReviewRedisTests : IClassFixture<RedisWebApplicationFactory>, IAsyncLifetime
{
    private readonly RedisWebApplicationFactory _factory;

    public RenewalReviewRedisTests(RedisWebApplicationFactory factory) => _factory = factory;

    public Task InitializeAsync() => _factory.WaitForCacheAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private HttpClient Client() => _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = false });

    private async Task<HttpResponseMessage> PostAsync(string path, object? body, string? bearer = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        if (body is not null) request.Content = JsonContent.Create(body);
        if (bearer is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        return await Client().SendAsync(request);
    }

    private Task<HttpResponseMessage> RenewAsync(string token) => PostAsync("/api/auth/refresh", TestCredentials.RefreshPayload(token));

    private async Task<LoginResponse> SignInAsync()
    {
        var account = await SyntheticTenant.CreateAsync(_factory, "varejo");
        var response = await PostAsync("/api/auth/login", TestCredentials.LoginPayload(account.Login, account.Password));
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<LoginResponse>())!;
    }

    private string JtiOf(string token) => RedisSessionKit.JtiOf(token);

    private string FamilyOf(string token)
    {
        using var scope = _factory.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<IJwtTokenService>().ValidateRefreshToken(token)!.Family;
    }

    // ── A sign-out during an outage ───────────────────────────────────────────────────────────────────────────────────

    /// <summary>The whole cycle of the review: the row is revoked, the keys come back from the snapshot, and the token must not rotate.</summary>
    [Fact]
    public async Task A_sign_out_while_Redis_is_down_holds_after_it_comes_back()
    {
        var signedIn = await SignInAsync();

        await _factory.StopRedisAsync();
        try
        {
            var signedOut = await PostAsync("/api/auth/logout", new { refreshToken = signedIn.RefreshToken }, signedIn.AccessToken);
            signedOut.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }
        finally
        {
            await _factory.StartRedisAsync();
        }
        _factory.RedisKeys($"refresh:valid:{JtiOf(signedIn.RefreshToken)}").Should().ContainSingle("the snapshot brought the entry back: this is the gap");

        var replay = await RenewAsync(signedIn.RefreshToken);

        replay.StatusCode.Should().Be(HttpStatusCode.Unauthorized, await replay.Content.ReadAsStringAsync());
        _factory.RedisKeys($"refresh:valid:{JtiOf(signedIn.RefreshToken)}").Should().BeEmpty("the refusal cleans it");
        _factory.RedisKeys($"refresh:family:{FamilyOf(signedIn.RefreshToken)}:revoked").Should().ContainSingle("and the family stays ended");
    }

    [Fact]
    public async Task A_sign_out_while_Redis_is_frozen_holds_after_it_comes_back()
    {
        var signedIn = await SignInAsync();

        await _factory.FreezeRedisAsync();
        try
        {
            (await PostAsync("/api/auth/logout", new { refreshToken = signedIn.RefreshToken }, signedIn.AccessToken)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        }
        finally
        {
            await _factory.UnfreezeRedisAsync();
        }

        (await RenewAsync(signedIn.RefreshToken)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task The_store_switch_refuses_a_session_ended_during_the_outage_too()
    {
        var signedIn = await SignInAsync();
        await _factory.StopRedisAsync();
        try
        {
            (await PostAsync("/api/auth/logout", new { refreshToken = signedIn.RefreshToken }, signedIn.AccessToken)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        }
        finally
        {
            await _factory.StartRedisAsync();
        }

        var switched = await PostAsync("/api/auth/switch-store", new { storeId = signedIn.Session.StoreId, refreshToken = signedIn.RefreshToken }, signedIn.AccessToken);

        switched.StatusCode.Should().Be(HttpStatusCode.Unauthorized, await switched.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// Why the check asks "is it revoked?" and not "is it active?": the row follows the token by a best-effort step, so it may still hold the
    /// previous one. A live token with a stale row has to go on renewing.
    /// </summary>
    [Fact]
    public async Task A_session_row_that_did_not_follow_the_rotation_does_not_refuse_a_live_token()
    {
        var signedIn = await SignInAsync();
        var rotated = await RenewAsync(signedIn.RefreshToken);
        var current = (await rotated.Content.ReadFromJsonAsync<RefreshResponse>())!;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
            var currentJti = JtiOf(current.RefreshToken);
            var oldJti = JtiOf(signedIn.RefreshToken);
            await db.UserSessions.IgnoreQueryFilters().Where(s => s.RefreshJti == currentJti)
                .ExecuteUpdateAsync(x => x.SetProperty(s => s.RefreshJti, oldJti));
        }

        (await RenewAsync(current.RefreshToken)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_platform_session_has_no_such_gap_it_lives_in_the_database()
    {
        var email = $"rv-{Guid.NewGuid():N}"[..13] + "@platform.test";
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
            db.PlatformUsers.Add(PlatformUser.Create(email, scope.ServiceProvider.GetRequiredService<IPasswordHasher>().Hash(TestCredentials.PlatformPassword), "super_admin"));
            await db.SaveChangesAsync();
        }
        var login = await PostAsync("/api/platform/auth/login", new { email, password = TestCredentials.PlatformPassword });
        var body = await login.Content.ReadFromJsonAsync<JsonElement>();

        await _factory.StopRedisAsync();
        try
        {
            (await PostAsync("/api/auth/logout", new { refreshToken = body.GetProperty("refreshToken").GetString() }, body.GetProperty("accessToken").GetString())).StatusCode.Should().Be(HttpStatusCode.NoContent);
        }
        finally
        {
            await _factory.StartRedisAsync();
        }

        (await RenewAsync(body.GetProperty("refreshToken").GetString()!)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── The reservation of a token ────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// INCR and then EXPIRE leave, on a crash between the two, a key with no expiry: a live token answering 409 forever. One atomic step never
    /// leaves that, and a key already left like that by the old code gets its expiry at the next touch.
    /// </summary>
    [Fact]
    public async Task A_reservation_left_with_no_expiry_does_not_stay_forever()
    {
        var signedIn = await SignInAsync();
        var key = $"refresh:claim:{JtiOf(signedIn.RefreshToken)}";
        await _factory.RedisDatabase.StringSetAsync(key, 1);
        (await _factory.RedisDatabase.KeyTimeToLiveAsync(key)).Should().BeNull("this is what a crash between the two steps leaves");

        var renewal = await RenewAsync(signedIn.RefreshToken);

        renewal.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var ttl = await _factory.RedisDatabase.KeyTimeToLiveAsync(key);
        ttl.Should().NotBeNull("the reservation has to end by itself");
        ttl!.Value.Should().BeLessThanOrEqualTo(TimeSpan.FromMinutes(1));

        await _factory.RedisDatabase.KeyDeleteAsync(key);
        (await RenewAsync(signedIn.RefreshToken)).StatusCode.Should().Be(HttpStatusCode.OK, "once it ends, the token is good");
    }

    [Fact]
    public async Task The_counter_and_its_expiry_are_one_step_under_concurrency()
    {
        var key = $"teste:contador:{Guid.NewGuid():N}";
        using var scope = _factory.Services.CreateScope();
        var cache = scope.ServiceProvider.GetRequiredService<ICacheService>();

        var counts = await Task.WhenAll(Enumerable.Range(0, 50).Select(_ => cache.IncrementAsync(key, TimeSpan.FromSeconds(30))));

        counts.Select(c => c!.Value).Should().BeEquivalentTo(Enumerable.Range(1, 50).Select(i => (long)i));
        var ttl = await _factory.RedisDatabase.KeyTimeToLiveAsync(key);
        ttl.Should().NotBeNull();
        ttl!.Value.Should().BeLessThanOrEqualTo(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task A_count_does_not_move_the_end_of_a_window_already_running()
    {
        var key = $"teste:contador:{Guid.NewGuid():N}";
        using var scope = _factory.Services.CreateScope();
        var cache = scope.ServiceProvider.GetRequiredService<ICacheService>();
        await cache.IncrementAsync(key, TimeSpan.FromSeconds(30));
        await _factory.RedisDatabase.KeyExpireAsync(key, TimeSpan.FromSeconds(5));

        await cache.IncrementAsync(key, TimeSpan.FromSeconds(30));

        (await _factory.RedisDatabase.KeyTimeToLiveAsync(key))!.Value.Should().BeLessThanOrEqualTo(TimeSpan.FromSeconds(5));
    }
}
