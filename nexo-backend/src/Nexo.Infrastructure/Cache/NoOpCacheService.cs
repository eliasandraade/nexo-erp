using Nexo.Application.Common.Interfaces;

namespace Nexo.Infrastructure.Cache;

/// <summary>
/// No-op cache implementation for local development without Redis.
/// Every call misses — all data comes from the database.
/// WARNING: Never use in production — JWT blacklist and module cache will not work.
/// </summary>
public class NoOpCacheService : ICacheService
{
    public Task<T?> GetAsync<T>(string key, CancellationToken ct = default) where T : class
        => Task.FromResult<T?>(null);

    public Task SetAsync<T>(string key, T value, TimeSpan? ttl = null, CancellationToken ct = default) where T : class
        => Task.CompletedTask;

    public Task RemoveAsync(string key, CancellationToken ct = default)
        => Task.CompletedTask;

    public async Task<T?> GetOrSetAsync<T>(
        string key,
        Func<Task<T?>> factory,
        TimeSpan? ttl = null,
        CancellationToken ct = default) where T : class
        => await factory();

    public Task<bool> ExistsAsync(string key, CancellationToken ct = default)
        => Task.FromResult(false);

    public Task SetFlagAsync(string key, TimeSpan ttl, CancellationToken ct = default)
        => Task.CompletedTask;
}
