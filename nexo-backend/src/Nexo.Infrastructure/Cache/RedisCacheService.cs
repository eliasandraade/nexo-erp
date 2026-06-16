using System.Text.Json;
using Microsoft.Extensions.Logging;
using Nexo.Application.Common.Interfaces;
using StackExchange.Redis;

namespace Nexo.Infrastructure.Cache;

/// <summary>
/// Redis-backed implementation of ICacheService.
/// Fail-open strategy: if Redis is unavailable, methods return null/false
/// rather than throwing — the caller falls back to the database.
///
/// CIRCUIT BREAKER (critical): the hot-path middleware (tenant resolution +
/// security stamp) makes several cache calls per authenticated request. When
/// Redis is unreachable, each blocking call used to wait the full per-call
/// timeout, so a dead Redis added 10-16s PER request. The breaker makes calls
/// fail FAST while Redis is down:
///   - If the (singleton) multiplexer reports it isn't connected, skip the call
///     entirely (~0ms) — no wait at all.
///   - On any timeout/error, open the circuit for a short cooldown during which
///     every call short-circuits without touching Redis. This bounds the cost of
///     an outage to ~one probe per cooldown and throttles the warning log.
///   - After the cooldown, the next call probes Redis again. With
///     AbortOnConnectFail=false the multiplexer reconnects in the background, so
///     caching resumes automatically once connectivity is restored — no restart.
///
/// Key namespaces:
///   tenant:{id}:modules          → List of active module keys (TTL 5min)
///   jwt:blacklist:{jti}          → Access token revocation flag (TTL = token expiry)
///   user:blocked:{userId}        → User block flag (no TTL)
///   refresh:valid:{jti}          → Valid refresh token data (TTL 7d)
///   refresh:blacklist:{jti}      → Revoked refresh token flag (TTL 7d)
/// </summary>
public class RedisCacheService : ICacheService
{
    // Per-call timeout guard. A healthy internal Redis round-trip is a few ms, so
    // 500ms only ever fires for a stalled/unreachable server. (Was 2000ms — and
    // with ~10 calls per request that alone produced the 10-16s stalls.)
    private static readonly TimeSpan _callTimeout = TimeSpan.FromMilliseconds(500);

    // Circuit breaker state. RedisCacheService is registered scoped (one per
    // request), so the state must be static to be shared process-wide.
    private static readonly TimeSpan _circuitCooldown = TimeSpan.FromSeconds(10);
    private static long s_circuitResetAtTicks; // UtcNow ticks until which Redis is treated as "down"

    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RedisCacheService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public RedisCacheService(
        IConnectionMultiplexer redis,
        ILogger<RedisCacheService> logger)
    {
        _redis = redis;
        _logger = logger;
    }

    /// <summary>
    /// True when Redis should be skipped without a network call: the circuit is
    /// open, or the multiplexer is not currently connected.
    /// </summary>
    private bool RedisUnavailable =>
        Volatile.Read(ref s_circuitResetAtTicks) > DateTime.UtcNow.Ticks || !_redis.IsConnected;

    /// <summary>Open the circuit for the cooldown window after a failure (logs once per trip).</summary>
    private void TripCircuit(Exception ex, string op, string key)
    {
        var wasClosed = Volatile.Read(ref s_circuitResetAtTicks) <= DateTime.UtcNow.Ticks;
        Volatile.Write(ref s_circuitResetAtTicks, DateTime.UtcNow.Add(_circuitCooldown).Ticks);
        // Log only on the closed→open transition so we don't emit one line per call.
        if (wasClosed)
            _logger.LogWarning(ex,
                "Redis {Op} failed for key '{Key}'. Circuit opened for {Seconds}s — serving from DB (fail-open).",
                op, key, _circuitCooldown.TotalSeconds);
    }

    public async Task<T?> GetAsync<T>(string key, CancellationToken ct = default) where T : class
    {
        if (RedisUnavailable) return null;
        try
        {
            var db = _redis.GetDatabase();
            var value = await db.StringGetAsync(key).WaitAsync(_callTimeout, ct);

            if (!value.HasValue) return null;

            return JsonSerializer.Deserialize<T>(value!, JsonOptions);
        }
        catch (Exception ex)
        {
            TripCircuit(ex, "GET", key);
            return null;
        }
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan? ttl = null, CancellationToken ct = default)
        where T : class
    {
        if (RedisUnavailable) return;
        try
        {
            var db = _redis.GetDatabase();
            var json = JsonSerializer.Serialize(value, JsonOptions);
            await db.StringSetAsync(key, json, ttl).WaitAsync(_callTimeout, ct);
        }
        catch (Exception ex)
        {
            TripCircuit(ex, "SET", key);
        }
    }

    public async Task RemoveAsync(string key, CancellationToken ct = default)
    {
        if (RedisUnavailable) return;
        try
        {
            var db = _redis.GetDatabase();
            await db.KeyDeleteAsync(key).WaitAsync(_callTimeout, ct);
        }
        catch (Exception ex)
        {
            TripCircuit(ex, "DEL", key);
        }
    }

    public async Task<T?> GetOrSetAsync<T>(
        string key,
        Func<Task<T?>> factory,
        TimeSpan? ttl = null,
        CancellationToken ct = default) where T : class
    {
        // GetAsync/SetAsync are already circuit-guarded: when Redis is down the GET
        // returns null fast and the SET is a no-op, so the factory (DB) runs and we
        // return its value without any Redis wait.
        var cached = await GetAsync<T>(key, ct);
        if (cached is not null) return cached;

        var value = await factory();
        if (value is not null)
            await SetAsync(key, value, ttl, ct);

        return value;
    }

    public async Task<bool> ExistsAsync(string key, CancellationToken ct = default)
    {
        if (RedisUnavailable) return false;
        try
        {
            var db = _redis.GetDatabase();
            return await db.KeyExistsAsync(key).WaitAsync(_callTimeout, ct);
        }
        catch (Exception ex)
        {
            TripCircuit(ex, "EXISTS", key);
            return false;
        }
    }

    public async Task SetFlagAsync(string key, TimeSpan ttl, CancellationToken ct = default)
    {
        if (RedisUnavailable) return;
        try
        {
            var db = _redis.GetDatabase();
            await db.StringSetAsync(key, "1", ttl).WaitAsync(_callTimeout, ct);
        }
        catch (Exception ex)
        {
            TripCircuit(ex, "SETFLAG", key);
        }
    }
}
