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
/// Key namespaces:
///   tenant:{id}:modules          → List of active module keys (TTL 5min)
///   jwt:blacklist:{jti}          → Access token revocation flag (TTL = token expiry)
///   user:blocked:{userId}        → User block flag (no TTL)
///   refresh:valid:{jti}          → Valid refresh token data (TTL 7d)
///   refresh:blacklist:{jti}      → Revoked refresh token flag (TTL 7d)
/// </summary>
public class RedisCacheService : ICacheService
{
    // Guard every Redis call: if SE.Redis ignores AsyncTimeout and keeps the backlog
    // open, this per-call timeout guarantees we bail rather than blocking the pipeline.
    // 2000ms matches Railway internal Redis round-trip budget (was 200ms — too tight).
    private static readonly TimeSpan _callTimeout = TimeSpan.FromMilliseconds(2000);

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

    public async Task<T?> GetAsync<T>(string key, CancellationToken ct = default) where T : class
    {
        try
        {
            var db = _redis.GetDatabase();
            var value = await db.StringGetAsync(key).WaitAsync(_callTimeout, ct);

            if (!value.HasValue) return null;

            return JsonSerializer.Deserialize<T>(value!, JsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis GET failed for key '{Key}'. Returning null (fail-open).", key);
            return null;
        }
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan? ttl = null, CancellationToken ct = default)
        where T : class
    {
        try
        {
            var db = _redis.GetDatabase();
            var json = JsonSerializer.Serialize(value, JsonOptions);
            await db.StringSetAsync(key, json, ttl).WaitAsync(_callTimeout, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis SET failed for key '{Key}'. Continuing without cache.", key);
        }
    }

    public async Task RemoveAsync(string key, CancellationToken ct = default)
    {
        try
        {
            var db = _redis.GetDatabase();
            await db.KeyDeleteAsync(key).WaitAsync(_callTimeout, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis DEL failed for key '{Key}'. Continuing.", key);
        }
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

    public async Task<bool> ExistsAsync(string key, CancellationToken ct = default)
    {
        try
        {
            var db = _redis.GetDatabase();
            return await db.KeyExistsAsync(key).WaitAsync(_callTimeout, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis EXISTS failed for key '{Key}'. Returning false (fail-open).", key);
            return false;
        }
    }

    public async Task SetFlagAsync(string key, TimeSpan ttl, CancellationToken ct = default)
    {
        try
        {
            var db = _redis.GetDatabase();
            await db.StringSetAsync(key, "1", ttl).WaitAsync(_callTimeout, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis SET flag failed for key '{Key}'. Continuing.", key);
        }
    }
}
