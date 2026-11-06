using System.Collections.Concurrent;
using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Nexo.Application.Common.Interfaces;
using Nexo.Domain.Enums;
using Nexo.Infrastructure.Persistence;

namespace Nexo.Infrastructure.Hubs;

/// <summary>What changed, as a hint: whoever receives it asks the database. A late or repeated one closes nothing wrongly.</summary>
public sealed record HubSessionChange(
    IReadOnlyCollection<Guid> Users,
    IReadOnlyCollection<Guid> Tenants,
    IReadOnlyCollection<string>? EndedTokens = null);

/// <summary>
/// The open hub connections of this instance and the rule that says whether the session behind each one still exists. A token is
/// only checked when a connection opens, so a connection has to be judged again whenever a session can have ended.
///
/// The rule is the one of the HTTP path (SecurityStampValidationMiddleware and TenantResolutionMiddleware): the stamp of the token
/// is the one in the database, the person is active and the company is active. It reads the database every time, in one query for
/// any number of connections, and never the cache: see the comment in SecurityStampValidationMiddleware. A token without a stamp
/// no longer exists among the ones this API issues, and a connection that cannot be checked does not stay.
///
/// A failed read says nothing about anyone: <see cref="SweepAsync"/> and <see cref="ReviewAsync"/> throw and close nothing.
/// </summary>
public sealed class HubSessionGuard
{
    /// <summary>Marks the read in the SQL text, so a test can count it and an operator can find it.</summary>
    public const string ReadTag = "hub-session-read";

    private static readonly TimeSpan CallLimit = TimeSpan.FromSeconds(5);

    private readonly ConcurrentDictionary<string, HubSession> _sessions = new();
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<HubSessionGuard> _logger;

    public HubSessionGuard(IServiceScopeFactory scopes, ILogger<HubSessionGuard> logger)
    {
        _scopes = scopes;
        _logger = logger;
    }

    public int ConnectionCount => _sessions.Count;

    private enum Verdict { Valid, Ended, Unknown }

    private sealed record HubSession(string ConnectionId, HubCallerContext Context, Guid UserId, Guid TenantId, string? Stamp, string? ImpersonationJti);

    private sealed record SessionRow(Guid UserId, Guid TenantId, string Stamp, UserStatus Status, TenantStatus TenantStatus);

    public void Register(HubCallerContext context)
        => _sessions[context.ConnectionId] = SessionOf(context);

    public void Forget(string connectionId) => _sessions.TryRemove(connectionId, out _);

    /// <summary>
    /// A connection that just registered. From the registration on, a change finds it; one committed before is in this read. A
    /// database that does not answer leaves it open: the HTTP checks passed a moment ago, and the sweep judges it next.
    /// </summary>
    public async Task CloseIfEndedAsync(HubCallerContext context)
    {
        var session = SessionOf(context);
        if (await CheckAsync(session, context.ConnectionAborted) == Verdict.Ended)
            Close(session, "ended before the connection opened");
    }

    /// <summary>
    /// A hub call: goes on only with a session confirmed now. An ended one is aborted, and a database that does not answer refuses
    /// this call and keeps the connection (nothing joins a group unchecked, and a healthy session is not punished for it).
    /// </summary>
    public async Task EnsureSessionAsync(HubCallerContext context)
    {
        var session = SessionOf(context);
        switch (await CheckAsync(session, context.ConnectionAborted))
        {
            case Verdict.Valid:
                return;
            case Verdict.Ended:
                Close(session, "ended, found on a hub call");
                throw new HubException("Sua sessão foi encerrada. Entre de novo.");
            default:
                throw new HubException("Não foi possível conferir a sessão agora. Tente de novo.");
        }
    }

    /// <summary>One pass over every connection of this instance, with one read. Returns how many closed.</summary>
    public Task<int> SweepAsync(CancellationToken ct) => CloseEndedAsync(_sessions.Values.ToList(), ct);

    /// <summary>The connections a change touches: none, and the database is not even read. Returns how many closed.</summary>
    public async Task<int> ReviewAsync(HubSessionChange change, CancellationToken ct)
    {
        var closed = 0;
        if (change.EndedTokens is { Count: > 0 } ended)
        {
            // Not in the database: the message itself is the news, and it holds even when the cache did not take the entry.
            foreach (var session in _sessions.Values.Where(s => s.ImpersonationJti is { } jti && ended.Contains(jti)))
            {
                Close(session, "the platform access ended");
                closed++;
            }
        }
        return closed + await CloseEndedAsync(_sessions.Values.Where(s => change.Users.Contains(s.UserId) || change.Tenants.Contains(s.TenantId)).ToList(), ct);
    }

    private async Task<int> CloseEndedAsync(IReadOnlyList<HubSession> sessions, CancellationToken ct)
    {
        if (sessions.Count == 0) return 0;

        // The list above was taken before this read: the database is at least as new as any token in it, so a connection that
        // opened with a stamp just issued can never look older than the row.
        using var scope = _scopes.CreateScope();
        var rows = await ReadAsync(scope, sessions, ct);
        var closed = 0;
        foreach (var session in sessions)
        {
            if (await WhyEndedAsync(scope, session, rows, ct) is not { } reason) continue;
            Close(session, reason);
            closed++;
        }
        return closed;
    }

    private async Task<Verdict> CheckAsync(HubSession session, CancellationToken ct)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(CallLimit);
        try
        {
            using var scope = _scopes.CreateScope();
            var rows = await ReadAsync(scope, [session], limit.Token);
            return await WhyEndedAsync(scope, session, rows, limit.Token) is null ? Verdict.Valid : Verdict.Ended;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;   // the connection itself is gone
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not check the session of hub connection {ConnectionId}", session.ConnectionId);
            return Verdict.Unknown;
        }
    }

    private static Task<Dictionary<Guid, SessionRow>> ReadAsync(IServiceScope scope, IReadOnlyList<HubSession> sessions, CancellationToken ct)
    {
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        var userIds = sessions.Select(s => s.UserId).Distinct().ToList();
        return db.Users.IgnoreQueryFilters().AsNoTracking()
            .TagWith(ReadTag)
            .Where(u => userIds.Contains(u.Id))
            .Join(db.Tenants.IgnoreQueryFilters().AsNoTracking(), u => u.TenantId, t => t.Id,
                (u, t) => new SessionRow(u.Id, u.TenantId, u.SecurityStamp, u.Status, t.Status))
            .ToDictionaryAsync(r => r.UserId, ct);
    }

    private static async Task<string?> WhyEndedAsync(IServiceScope scope, HubSession session, Dictionary<Guid, SessionRow> rows, CancellationToken ct)
    {
        if (!rows.TryGetValue(session.UserId, out var row)) return "the user no longer exists";
        if (row.TenantId != session.TenantId) return "the user is in another company";
        if (session.Stamp is null || session.Stamp != row.Stamp) return "the security stamp changed";
        if (row.Status != UserStatus.Active) return "the user is not active";
        if (row.TenantStatus != TenantStatus.Active) return "the company is not active";
        // Same source as the HTTP path, and only for the few connections of a platform access. The token's own expiry closes
        // them in the end if the cache is out.
        if (session.ImpersonationJti is { } jti
            && await scope.ServiceProvider.GetRequiredService<ICacheService>().ExistsAsync(ImpersonationClaims.EndedKey(jti), ct))
            return "the platform access ended";
        return null;
    }

    private void Close(HubSession session, string reason)
    {
        _logger.LogInformation("Closing hub connection {ConnectionId} of user {UserId}: {Reason}", session.ConnectionId, session.UserId, reason);
        session.Context.Abort();
    }

    private static HubSession SessionOf(HubCallerContext context)
    {
        var user = context.User;
        return new HubSession(
            context.ConnectionId,
            context,
            Guid.TryParse(user?.FindFirstValue("userId"), out var userId) ? userId : Guid.Empty,
            Guid.TryParse(user?.FindFirstValue("tenantId"), out var tenantId) ? tenantId : Guid.Empty,
            user?.FindFirstValue("security_stamp"),
            user?.HasClaim(ImpersonationClaims.Marker, "1") == true ? user.FindFirstValue("jti") : null);
    }
}
