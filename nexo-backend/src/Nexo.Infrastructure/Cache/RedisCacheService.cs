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
/// CIRCUIT BREAKER (critical): the hot-path middleware (tenant resolution and
/// access checks) makes several cache calls per authenticated request. When
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
///   - A caller that cancels is not a Redis failure: the cancellation propagates and the
///     circuit stays closed. Otherwise any client closing a tab would turn the cache (and
///     what relies on it, like session renewal and the login lock) off for the whole process during the cooldown.
///
/// Key namespaces:
///   tenant:{id}:modules          → List of active module keys (TTL 5min)
///   jwt:blacklist:{jti}          → Access token revocation flag (TTL = token expiry)
///   user:blocked:{userId}        → User block flag (no TTL)
///   refresh:valid:{jti}          → Valid refresh token data (TTL 7d)
///   refresh:blacklist:{jti}      → Revoked refresh token flag (TTL 7d)
///   refresh:claim:{jti}          → Reserva de uso único da troca de um refresh token (TTL 1 min)
///   refresh:family:{fid}         → Token atual e anterior da família (sessão) e hora da troca (TTL = vida do refresh)
///   refresh:family:{fid}:revoked → Família encerrada: logout ou token já usado reapresentado (TTL = vida do refresh)
///   refresh:budget:ip:{ip}       → Invalid refresh tokens from this address in the current window (RateLimiting:AuthRefresh)
///   refresh:budget:family:{fid}  → Renewals of this session in the current window (RateLimiting:AuthRefresh)
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

    // A count is optional and sits in front of work that does need Redis, so running out of time here only skips it and does
    // not open the circuit. If it did, one slow count would turn every refresh-token lookup of the process into "no such token"
    // for the whole cooldown.
    private static readonly TimeSpan _countWarningInterval = TimeSpan.FromMinutes(1);
    private static long s_nextCountWarningTicks;

    // One atomic step. The lifetime is set whenever the counter has none, which also heals one left without it.
    private const string CountInWindowScript = """
        local count = redis.call('INCR', KEYS[1])
        local ttl = redis.call('PTTL', KEYS[1])
        if ttl < 0 then
          ttl = tonumber(ARGV[1])
          redis.call('PEXPIRE', KEYS[1], ttl)
        end
        return { count, ttl }
        """;

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
        catch (Exception ex) when (!ct.IsCancellationRequested)
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
        catch (Exception ex) when (!ct.IsCancellationRequested)
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
        catch (Exception ex) when (!ct.IsCancellationRequested)
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
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            TripCircuit(ex, "EXISTS", key);
            return false;
        }
    }

    public async Task<long?> IncrementAsync(string key, TimeSpan window, CancellationToken ct = default)
    {
        if (RedisUnavailable) return null;
        try
        {
            var result = (RedisResult[]?)await _redis.GetDatabase()
                .ScriptEvaluateAsync(CountInWindowScript, [key], [(long)window.TotalMilliseconds])
                .WaitAsync(_callTimeout, ct);
            return (long)result![0];
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            TripCircuit(ex, "INCR", key);
            return null;
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
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            TripCircuit(ex, "SETFLAG", key);
        }
    }

    public async Task<WindowCount?> CountInWindowAsync(string key, TimeSpan window, CancellationToken ct = default)
    {
        if (RedisUnavailable) return null;
        try
        {
            var result = (RedisResult[]?)await _redis.GetDatabase()
                .ScriptEvaluateAsync(CountInWindowScript, [key], [(long)window.TotalMilliseconds])
                .WaitAsync(_callTimeout, ct);
            return new WindowCount((long)result![0], TimeSpan.FromMilliseconds((long)result[1]));
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            WarnCountSkipped(ex, key);
            return null;
        }
    }

    public bool IsUnavailable => RedisUnavailable;

    /// <summary>A budget that stopped counting is worth knowing about, once a minute at most.</summary>
    private void WarnCountSkipped(Exception ex, string key)
    {
        var now = DateTime.UtcNow.Ticks;
        var next = Volatile.Read(ref s_nextCountWarningTicks);
        if (now < next || Interlocked.CompareExchange(ref s_nextCountWarningTicks, now + _countWarningInterval.Ticks, next) != next) return;
        _logger.LogWarning(ex, "Redis did not answer a count for key '{Key}' in {Milliseconds} ms: this count was skipped (fail-open).",
            key, _callTimeout.TotalMilliseconds);
    }
}
