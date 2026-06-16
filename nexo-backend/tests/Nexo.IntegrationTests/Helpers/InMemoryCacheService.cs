using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Nexo.Application.Common.Interfaces;

namespace Nexo.IntegrationTests.Helpers;

/// <summary>
/// Faithful in-memory <see cref="ICacheService"/> for the Testing environment.
///
/// Production resolves <see cref="ICacheService"/> to RedisCacheService. The test host has
/// no Redis connection string, so AddInfrastructure falls back to NoOpCacheService — which
/// silently DROPS every write and returns null on every read. That broke the refresh-token
/// validity round-trip: <c>AuthService.LoginAsync</c> stores <c>refresh:valid:{jti}</c> and
/// <c>AuthService.RefreshAsync</c> reads it back, so with NoOp the very first refresh always
/// missed and returned 401 (and concurrent/multi-tab refresh likewise failed).
///
/// This is a REAL cache, not a mock: it stores values with TTL, honours removes, and is
/// backed by the singleton <see cref="IMemoryCache"/> so state is shared across requests
/// within the shared test host — mirroring Redis's process-wide store. It therefore
/// exercises the exact rotation / replay-protection / revocation logic the auth security
/// tests assert, instead of bypassing it. Values are JSON round-tripped to match
/// RedisCacheService's copy semantics (no reference aliasing between cache and caller).
/// </summary>
public sealed class InMemoryCacheService : ICacheService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly IMemoryCache _cache;

    public InMemoryCacheService(IMemoryCache cache) => _cache = cache;

    public Task<T?> GetAsync<T>(string key, CancellationToken ct = default) where T : class
    {
        if (_cache.TryGetValue(key, out string? json) && json is not null)
            return Task.FromResult(JsonSerializer.Deserialize<T>(json, JsonOptions));

        return Task.FromResult<T?>(null);
    }

    public Task SetAsync<T>(string key, T value, TimeSpan? ttl = null, CancellationToken ct = default)
        where T : class
    {
        var json = JsonSerializer.Serialize(value, JsonOptions);
        var options = new MemoryCacheEntryOptions();
        if (ttl.HasValue)
            options.AbsoluteExpirationRelativeToNow = ttl;

        _cache.Set(key, json, options);
        return Task.CompletedTask;
    }

    public Task RemoveAsync(string key, CancellationToken ct = default)
    {
        _cache.Remove(key);
        return Task.CompletedTask;
    }

    public async Task<T?> GetOrSetAsync<T>(
        string key,
        Func<Task<T?>> factory,
        TimeSpan? ttl = null,
        CancellationToken ct = default) where T : class
    {
        var cached = await GetAsync<T>(key, ct);
        if (cached is not null) return cached;

        var value = await factory();
        if (value is not null)
            await SetAsync(key, value, ttl, ct);

        return value;
    }

    public Task<bool> ExistsAsync(string key, CancellationToken ct = default)
        => Task.FromResult(_cache.TryGetValue(key, out _));

    public Task SetFlagAsync(string key, TimeSpan ttl, CancellationToken ct = default)
    {
        _cache.Set(key, "1", new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = ttl });
        return Task.CompletedTask;
    }
}
