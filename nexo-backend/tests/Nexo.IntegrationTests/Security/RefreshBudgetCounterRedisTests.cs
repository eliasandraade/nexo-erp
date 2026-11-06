using System.Diagnostics;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Nexo.Application.Common.Interfaces;
using Nexo.IntegrationTests.Helpers;

namespace Nexo.IntegrationTests.Security;

/// <summary>
/// The counter under the renewal budgets (<see cref="ICacheService.CountInWindowAsync"/>), against a real Redis. What matters here
/// is what cannot be seen through HTTP: no two callers share a slot, a counter always dies with its window, and a slow answer
/// costs only this count: it does not open the circuit that the rest of the process depends on.
/// </summary>
[Collection(RedisRealCollection.Name)]
public sealed class RefreshBudgetCounterRedisTests : IClassFixture<RedisWebApplicationFactory>, IAsyncLifetime
{
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(5);

    private readonly RedisWebApplicationFactory _factory;

    public RefreshBudgetCounterRedisTests(RedisWebApplicationFactory factory) => _factory = factory;

    public Task InitializeAsync() => _factory.WaitForCacheAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private static string NewKey() => $"teste:contagem:{Guid.NewGuid():N}";

    private async Task<WindowCount?> CountAsync(string key, TimeSpan window)
    {
        using var scope = _factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ICacheService>().CountInWindowAsync(key, window);
    }

    private bool CacheIsUnavailable()
    {
        using var scope = _factory.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<ICacheService>().IsUnavailable;
    }

    [Fact]
    public async Task Concurrent_counts_each_get_their_own_value_and_the_window_is_set_once()
    {
        const int callers = 300;
        var key = NewKey();

        var counts = await Task.WhenAll(Enumerable.Range(0, callers).Select(_ => CountAsync(key, Window)));

        counts.Should().NotContainNulls();
        counts.Select(c => c!.Value.Count).Should().BeEquivalentTo(Enumerable.Range(1, callers).Select(i => (long)i), "no two callers share a slot");
        counts.Should().OnlyContain(c => c!.Value.Remaining > TimeSpan.Zero && c.Value.Remaining <= Window);
        ((long)await _factory.RedisDatabase.StringGetAsync(key)).Should().Be(callers);
        (await _factory.RedisDatabase.KeyTimeToLiveAsync(key)).Should().NotBeNull();
    }

    [Fact]
    public async Task The_window_is_fixed_and_a_new_one_starts_from_one_when_it_ends()
    {
        var key = NewKey();
        var window = TimeSpan.FromSeconds(2);

        var first = (await CountAsync(key, window))!.Value;
        await Task.Delay(TimeSpan.FromMilliseconds(600));
        var second = (await CountAsync(key, window))!.Value;

        first.Count.Should().Be(1);
        second.Count.Should().Be(2);
        second.Remaining.Should().BeLessThan(first.Remaining, "counting again does not push the end of the window");

        await Task.Delay(window);
        (await CountAsync(key, window))!.Value.Count.Should().Be(1);
    }

    /// <summary>A counter that lost its lifetime would hold a budget spent for good: the address or the session would never come back.</summary>
    [Fact]
    public async Task A_counter_left_without_a_lifetime_gets_one_back()
    {
        var key = NewKey();
        await _factory.RedisDatabase.StringSetAsync(key, 41);

        var counted = (await CountAsync(key, Window))!.Value;

        counted.Count.Should().Be(42);
        counted.Remaining.Should().BeCloseTo(Window, TimeSpan.FromSeconds(5));
        (await _factory.RedisDatabase.KeyTimeToLiveAsync(key)).Should().NotBeNull();
    }

    /// <summary>
    /// The trap of the fail-open cache: any other call that runs out of time opens the circuit for the whole process for 10 s, and
    /// meanwhile every refresh-token lookup answers "no such token". A budget in front of that lookup cannot be the cause.
    /// </summary>
    [Fact]
    public async Task A_count_that_runs_out_of_time_returns_nothing_quickly_and_leaves_the_circuit_closed()
    {
        var key = NewKey();
        await _factory.FreezeRedisAsync();
        try
        {
            var clock = Stopwatch.StartNew();
            var counted = await CountAsync(key, Window);
            clock.Stop();

            counted.Should().BeNull();
            clock.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(2), "a count does not hold a renewal for long");
            CacheIsUnavailable().Should().BeFalse("a slow count is not an outage");
        }
        finally
        {
            await _factory.UnfreezeRedisAsync();
        }
    }

    [Fact]
    public async Task With_redis_stopped_a_count_returns_nothing_and_the_cache_says_it_is_unavailable_until_it_returns()
    {
        var key = NewKey();
        await _factory.StopRedisAsync();
        try
        {
            (await CountAsync(key, Window)).Should().BeNull();
            CacheIsUnavailable().Should().BeTrue();
        }
        finally
        {
            await _factory.StartRedisAsync();
        }

        CacheIsUnavailable().Should().BeFalse();
        (await CountAsync(key, Window))!.Value.Count.Should().Be(1);
    }
}
