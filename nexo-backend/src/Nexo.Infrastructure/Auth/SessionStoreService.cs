using Microsoft.EntityFrameworkCore;
using Nexo.Application.Common.Interfaces;
using Nexo.Domain.Entities;
using Nexo.Infrastructure.Persistence;

namespace Nexo.Infrastructure.Auth;

/// <summary>
/// Persists user session metadata to the database.
/// Used by the platform admin to list and revoke active sessions.
/// </summary>
public class SessionStoreService : ISessionStore
{
    private readonly NexoDbContext _db;

    public SessionStoreService(NexoDbContext db) => _db = db;

    public async Task CreateAsync(
        Guid userId,
        Guid tenantId,
        string refreshJti,
        string? ipAddress,
        string? userAgent,
        DateTime expiresAt,
        CancellationToken ct = default)
    {
        var session = UserSession.Create(userId, tenantId, refreshJti, ipAddress, userAgent, expiresAt);
        _db.UserSessions.Add(session);

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch
        {
            // Session tracking is best-effort: never fail a login because of it.
        }
    }

    public async Task TouchAsync(string oldRefreshJti, string newRefreshJti, CancellationToken ct = default)
    {
        var session = await _db.UserSessions
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.RefreshJti == oldRefreshJti, ct);

        if (session is null) return;

        session.Touch(newRefreshJti);

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch
        {
            // Best-effort
        }
    }
}
