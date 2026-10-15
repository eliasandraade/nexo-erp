using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Nexo.Application.Integrations.Contracts;
using Microsoft.Extensions.Options;
using Nexo.Application.Integrations.Options;
using Nexo.Infrastructure.Persistence;
using StackExchange.Redis;

namespace Nexo.Infrastructure.Health;

/// <summary>Tags that split the checks: "ready" ones decide whether the API takes traffic.</summary>
public static class HealthTags
{
    public const string Ready = "ready";
}

// The checks below never attach the exception to their result: what went wrong goes to the log,
// the public health body only carries fixed descriptions.

/// <summary>
/// PostgreSQL — the one dependency the API cannot work without: unhealthy here means "do not send
/// traffic". The timeout cancels the connection attempt itself, so nothing keeps running on the
/// DbContext after the probe answered.
/// </summary>
public sealed class DatabaseHealthCheck : IHealthCheck
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(3);
    private readonly NexoDbContext _db;
    private readonly ILogger<DatabaseHealthCheck> _logger;

    public DatabaseHealthCheck(NexoDbContext db, ILogger<DatabaseHealthCheck> logger)
    {
        _db     = db;
        _logger = logger;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(Timeout);
        try
        {
            return await _db.Database.CanConnectAsync(cts.Token)
                ? HealthCheckResult.Healthy("reachable")
                : HealthCheckResult.Unhealthy("unreachable");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;   // the caller gave up — not a database verdict
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Health: PostgreSQL unreachable.");
            return HealthCheckResult.Unhealthy("unreachable");
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
    private readonly IServiceProvider _services;
    private readonly ILogger<RedisHealthCheck> _logger;

    public RedisHealthCheck(IServiceProvider services, ILogger<RedisHealthCheck> logger)
    {
        _services = services;
        _logger   = logger;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            // resolved here, inside the try: the multiplexer factory throws on a bad connection string
            var redis = _services.GetService<IConnectionMultiplexer>();
            if (redis is null) return HealthCheckResult.Healthy("not configured");
            await redis.GetDatabase().PingAsync().WaitAsync(Timeout, cancellationToken);
            return HealthCheckResult.Healthy("reachable");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Health: Redis unreachable; cache is bypassed.");
            return HealthCheckResult.Degraded("unreachable — cache bypassed");
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

    private sealed record Snapshot(DateTime At, HealthCheckResult Result);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private Snapshot? _last;

    private readonly IStorageProvider _storage;
    private readonly IIntegrationFeatureFlags _flags;
    private readonly bool _hasPrivateBucket;
    private readonly ILogger<ObjectStorageHealthCheck> _logger;

    public ObjectStorageHealthCheck(IStorageProvider storage, IIntegrationFeatureFlags flags, IOptions<StorageOptions> options, ILogger<ObjectStorageHealthCheck> logger)
    {
        _storage = storage;
        _flags   = flags;
        _hasPrivateBucket = !string.IsNullOrWhiteSpace(options.Value.R2.PrivateBucketName);
        _logger  = logger;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (!_flags.StorageEnabled) return HealthCheckResult.Healthy("disabled");
        if (Fresh() is { } cached) return cached;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (Fresh() is { } again) return again;
            HealthCheckResult result;
            try
            {
                await _storage.PingAsync(cancellationToken).WaitAsync(Timeout, cancellationToken);
                // reachable, but private files would sit in the public bucket: say so until configured
                result = _hasPrivateBucket
                    ? HealthCheckResult.Healthy("reachable")
                    : HealthCheckResult.Degraded("reachable — private bucket not configured");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;   // the probe gave up; keep no verdict
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Health: object storage unreachable; uploads unavailable.");
                result = HealthCheckResult.Degraded("unreachable — uploads unavailable");
            }
            Volatile.Write(ref _last, new Snapshot(DateTime.UtcNow, result));
            return result;
        }
        finally
        {
            _gate.Release();
        }
    }

    private HealthCheckResult? Fresh()
        => Volatile.Read(ref _last) is { } s && DateTime.UtcNow - s.At < Reuse ? s.Result : null;
}
