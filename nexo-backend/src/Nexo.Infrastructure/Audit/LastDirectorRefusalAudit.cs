using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Nexo.Application.Common.Interfaces;
using Nexo.Infrastructure.Persistence;

namespace Nexo.Infrastructure.Audit;

/// <summary>
/// See <see cref="ILastDirectorRefusalAudit"/>. The entry is staged right away, on the caller's flow, in a scope of its own: that is
/// where <see cref="IAuditWriter"/> reads the IP and who is acting. The write happens here, one at a time and in order, so a burst of
/// refusals or a stuck database takes one connection and nothing else. A queue that is full drops the entry and logs it.
/// </summary>
public sealed class LastDirectorRefusalAudit : BackgroundService, ILastDirectorRefusalAudit
{
    private const int QueueCapacity = 256;
    private const int DescriptionLimit = 500;
    private static readonly TimeSpan WriteLimit = TimeSpan.FromSeconds(5);

    private sealed record Pending(AsyncServiceScope Scope, NexoDbContext Db, Guid TenantId, Guid TargetId);

    private readonly Channel<Pending> _queue = Channel.CreateBounded<Pending>(new BoundedChannelOptions(QueueCapacity) { SingleReader = true });
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<LastDirectorRefusalAudit> _logger;

    public LastDirectorRefusalAudit(IServiceScopeFactory scopes, ILogger<LastDirectorRefusalAudit> logger)
    {
        _scopes = scopes;
        _logger = logger;
    }

    public void Record(Guid tenantId, string source, IReadOnlyList<LastDirectorAttempt> attempts)
    {
        var target = attempts.Count > 0 ? attempts[0].UserId : Guid.Empty;
        AsyncServiceScope? opened = null;
        try
        {
            var scope = _scopes.CreateAsyncScope();
            opened = scope;
            var current = scope.ServiceProvider.GetRequiredService<ICurrentUser>();
            var actorId = current.IsAuthenticated && current.UserId != Guid.Empty ? current.UserId : (Guid?)null;

            scope.ServiceProvider.GetRequiredService<IAuditWriter>().Stage(
                actionType:  AuditActions.UserLastDirectorRefused,
                severity:    AuditSeverity.Warning,
                entityType:  "User",
                entityId:    target.ToString(),
                description: Describe(tenantId, actorId, attempts),
                tenantId:    tenantId,
                actorId:     actorId,
                actorName:   actorId is null ? "system" : null,
                actorType:   actorId is null ? "system" : "user",
                metadata:    new
                {
                    source,
                    targets = attempts.Select(a => new { userId = a.UserId, role = a.Role, status = a.Status, deleted = a.Deleted }),
                });

            if (_queue.Writer.TryWrite(new Pending(scope, scope.ServiceProvider.GetRequiredService<NexoDbContext>(), tenantId, target))) return;

            _logger.LogError("Last director refusal not recorded in the audit: the queue is full. TenantId={TenantId} TargetId={TargetId}", tenantId, target);
            scope.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Last director refusal not recorded in the audit. TenantId={TenantId} TargetId={TargetId}", tenantId, target);
            DisposeQuietly(opened);
        }
    }

    /// <summary>Stopping does not cancel the reading: what was already accepted is written while the host gives it time.</summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var pending in _queue.Reader.ReadAllAsync(CancellationToken.None))
            await WriteAsync(pending);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _queue.Writer.TryComplete();
        await base.StopAsync(cancellationToken);
    }

    private async Task WriteAsync(Pending pending)
    {
        await using (pending.Scope)
        {
            try
            {
                using var limit = new CancellationTokenSource(WriteLimit);
                await pending.Db.SaveChangesAsync(limit.Token);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Last director refusal not recorded in the audit. TenantId={TenantId} TargetId={TargetId}", pending.TenantId, pending.TargetId);
            }
        }
    }

    /// <summary>What the audit screens show, which list no metadata: the attempt in words, and identifiers only.</summary>
    private static string Describe(Guid tenantId, Guid? actorId, IReadOnlyList<LastDirectorAttempt> attempts)
    {
        var what = attempts.Select(a => a.Deleted ? $"{a.UserId}: exclusão" : $"{a.UserId}: perfil {a.Role}, status {a.Status}");
        var description = $"Alteração recusada: deixaria a empresa {tenantId} sem Diretoria ativa. " +
                          $"Ator: {(actorId is { } id ? id.ToString() : "sistema")}. Tentativa em {string.Join("; ", what)}.";
        return description.Length <= DescriptionLimit ? description : description[..DescriptionLimit];
    }

    private void DisposeQuietly(AsyncServiceScope? scope)
    {
        try
        {
            scope?.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "A scope of the audit of last director refusals did not close cleanly.");
        }
    }
}
