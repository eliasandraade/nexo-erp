using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Nexo.Application.Integrations.Contracts;
using Nexo.Application.Integrations.Options;
using Nexo.Infrastructure.Persistence;
using StackExchange.Redis;

namespace Nexo.Infrastructure.Health;

/// <summary>Tags that split the checks: "ready" ones decide whether the API takes traffic.</summary>
public static class HealthTags
{
    public const string Ready = "ready";
}

/// <summary>
/// PostgreSQL — the one dependency the API cannot work without: unhealthy here means "do not send
/// traffic". A short timeout keeps the probe from hanging behind a stuck pool.
/// </summary>
public sealed class DatabaseHealthCheck : IHealthCheck
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(3);
    private readonly NexoDbContext _db;

    public DatabaseHealthCheck(NexoDbContext db) => _db = db;

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            var ok = await _db.Database.CanConnectAsync(ct).WaitAsync(Timeout, ct);
            return ok ? HealthCheckResult.Healthy("reachable") : HealthCheckResult.Unhealthy("unreachable");
        }
        catch (Exception ex) when (ex is TimeoutException or OperationCanceledException or DbUpdateException or InvalidOperationException
                                       or Npgsql.NpgsqlException)
        {
            return HealthCheckResult.Unhealthy("unreachable", ex);
        }
    }
}

/// <summary>
/// Redis — a cache: the API keeps working without it (fail-open), slower. So a failure degrades the
/// report but never takes the API out of rotation. Not configured (local dev, tests) is healthy.
/// </summary>
public sealed class RedisHealthCheck : IHealthCheck
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(1);
    private readonly IConnectionMultiplexer? _redis;

    public RedisHealthCheck(IServiceProvider services) => _redis = services.GetService<IConnectionMultiplexer>();

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        if (_redis is null) return HealthCheckResult.Healthy("not configured");
        try
        {
            await _redis.GetDatabase().PingAsync().WaitAsync(Timeout, ct);
            return HealthCheckResult.Healthy("reachable");
        }
        catch (Exception ex) when (ex is TimeoutException or RedisException or OperationCanceledException)
        {
            return HealthCheckResult.Degraded("unreachable — cache bypassed", ex);
        }
    }
}

/// <summary>
/// Object storage (uploads: photos, logos, records). Uploads fail without it, the rest works — so
/// it degrades. The result is kept for a minute: probes run often and each ping is a paid request.
/// Registered as a singleton so the kept result is shared by every probe.
/// </summary>
public sealed class ObjectStorageHealthCheck : IHealthCheck
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan Reuse = TimeSpan.FromMinutes(1);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private (DateTime At, HealthCheckResult Result)? _last;

    private readonly IStorageProvider _storage;
    private readonly IIntegrationFeatureFlags _flags;

    public ObjectStorageHealthCheck(IStorageProvider storage, IIntegrationFeatureFlags flags)
    {
        _storage = storage;
        _flags   = flags;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        if (!_flags.StorageEnabled) return HealthCheckResult.Healthy("disabled");
        if (_last is { } fresh && DateTime.UtcNow - fresh.At < Reuse) return fresh.Result;

        await _gate.WaitAsync(ct);
        try
        {
            if (_last is { } again && DateTime.UtcNow - again.At < Reuse) return again.Result;
            HealthCheckResult result;
            try
            {
                await _storage.PingAsync(ct).WaitAsync(Timeout, ct);
                result = HealthCheckResult.Healthy("reachable");
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                result = HealthCheckResult.Degraded("unreachable — uploads unavailable", ex);
            }
            _last = (DateTime.UtcNow, result);
            return result;
        }
        finally
        {
            _gate.Release();
        }
    }
}
