namespace Nexo.Application.Common.Interfaces;

/// <summary>
/// Generic cache abstraction backed by Redis.
/// Used for: tenant module lists, report results, JWT blacklist, rate limiting.
///
/// Implementations must be thread-safe and handle Redis unavailability gracefully
/// by returning null/false rather than throwing (fail-open strategy).
/// </summary>
public interface ICacheService
{
    /// <summary>Gets a cached value. Returns null if not found or cache is unavailable.</summary>
    Task<T?> GetAsync<T>(string key, CancellationToken ct = default) where T : class;

    /// <summary>Sets a value in cache with optional TTL. Fails silently if cache is unavailable.</summary>
    Task SetAsync<T>(string key, T value, TimeSpan? ttl = null, CancellationToken ct = default) where T : class;

    /// <summary>Removes a key. Fails silently if cache is unavailable.</summary>
    Task RemoveAsync(string key, CancellationToken ct = default);

    /// <summary>
    /// Cache-aside pattern: returns cached value if exists, otherwise calls factory,
    /// caches the result, and returns it.
    /// </summary>
    Task<T?> GetOrSetAsync<T>(
        string key,
        Func<Task<T?>> factory,
        TimeSpan? ttl = null,
        CancellationToken ct = default) where T : class;

    /// <summary>
    /// Checks if a key exists (used for JWT blacklist and user-blocked checks).
    /// Returns false if cache is unavailable (fail-open).
    /// </summary>
    Task<bool> ExistsAsync(string key, CancellationToken ct = default);

    /// <summary>
    /// Sets a flag key with TTL. Used for JWT blacklist and user-blocked entries.
    /// The value is just a marker ("1") — only existence matters.
    /// </summary>
    Task SetFlagAsync(string key, TimeSpan ttl, CancellationToken ct = default);

    /// <summary>
    /// Atomically adds 1 and returns the new value; the TTL starts with the first increment (fixed
    /// window). Null when the cache is unavailable (fail-open — callers decide what that means).
    /// </summary>
    Task<long?> IncrementAsync(string key, TimeSpan window, CancellationToken ct = default);

    /// <summary>
    /// Counts one more in a fixed window: the increment, its lifetime and the time left are one atomic step, so concurrent
    /// callers never share a slot and a counter never outlives its window. Null when the count is not available. A slow cache
    /// only skips this count: it is not treated as an outage, and it must not affect any other call of the process.
    /// </summary>
    Task<WindowCount?> CountInWindowAsync(string key, TimeSpan window, CancellationToken ct = default);

    /// <summary>
    /// True while the cache is not answering. Reads fail open to "nothing there", so a caller for which a missing key is a
    /// verdict (a session that no longer exists) has to look at this before believing it.
    /// </summary>
    bool IsUnavailable { get; }
}

/// <summary>A counter in a fixed window: the value after this count and what is left of the window.</summary>
public readonly record struct WindowCount(long Count, TimeSpan Remaining);
