using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Nexo.Application.Common.Interfaces;
using Nexo.Domain.Entities;
using Nexo.Domain.Enums;
using Nexo.Domain.Exceptions;
using Npgsql;

namespace Nexo.Infrastructure.Persistence;

/// <summary>
/// A company keeps at least one active Diretoria, whatever code path changes its users. <see cref="NexoDbContext"/> runs this
/// on every save, so a path that never heard of the rule still cannot skip it.
///
/// The decision is taken under a per-company advisory lock held until the transaction ends, on what is committed at that
/// moment and not on what the request read earlier: two directors demoting each other on different connections line up on
/// the lock, and whoever comes second sees what the first one committed. Only removals need the lock. Whatever adds a director
/// can only raise the count, and a count that has not seen it yet errs on the safe side.
///
/// Only what removes a director is refused. A company already at zero can still be changed, otherwise it could not be repaired.
///
/// A refusal goes to the security audit on its own connection (<see cref="ILastDirectorRefusalAudit"/>), since the transaction
/// rolls back. Nothing here waits for that entry, so the lock is never held for it.
/// </summary>
internal static class LastDirectorGuard
{
    /// <summary>The companies this save may take a director from, in a fixed order so two saves never lock in opposite orders.</summary>
    public static IReadOnlyList<Guid> TenantsAtRisk(NexoDbContext db)
        => db.ChangeTracker.Entries<User>().Where(Leaves).Select(e => e.Entity.TenantId).Distinct().Order().ToList();

    /// <summary>Refuses the save when, for one of these companies, it would remove the last active director. Requires an open transaction.</summary>
    public static async Task EnforceAsync(NexoDbContext db, IReadOnlyList<Guid> tenants, CancellationToken ct)
    {
        var entries = db.ChangeTracker.Entries<User>().ToList();
        foreach (var tenantId in tenants)
        {
            await LockAsync(db, tenantId, ct);
            var active = await ActiveDirectors(db, tenantId).Select(u => u.Id).ToListAsync(ct);

            var ofTheCompany = entries.Where(e => e.Entity.TenantId == tenantId).ToList();
            var leaving = ofTheCompany.Where(Leaves).Select(e => e.Entity.Id).ToHashSet();
            var arriving = ofTheCompany.Where(Arrives).Select(e => e.Entity.Id);

            if (active.Exists(leaving.Contains) && !active.Except(leaving).Concat(arriving).Any())
            {
                db.LastDirectorRefusals?.Record(tenantId, LastDirectorRefusalSources.DataContext, AttemptsOn(ofTheCompany, active, leaving));
                throw new LastDirectorException();
            }
        }
    }

    /// <summary>The directors this save was taking out, as the save had them: the attempt, not what the database still says.</summary>
    private static List<LastDirectorAttempt> AttemptsOn(List<EntityEntry<User>> entries, List<Guid> active, HashSet<Guid> leaving)
        => entries.Where(e => leaving.Contains(e.Entity.Id) && active.Contains(e.Entity.Id))
            .Select(e => new LastDirectorAttempt(
                e.Entity.Id,
                e.Entity.Role.ToString().ToLowerInvariant(),
                e.Entity.Status.ToString().ToLowerInvariant(),
                e.State == EntityState.Deleted))
            .ToList();

    /// <summary>
    /// Takes the company's lock, for as long as the transaction lasts. The wait for it is bounded, so a stalled change cannot
    /// hang whoever comes next, and running out of it is an answer, not a 500. The bound is lifted right after: it is not meant
    /// for the row locks the rest of the transaction may wait for, whose failure would not be handled here.
    /// </summary>
    public static async Task LockAsync(NexoDbContext db, Guid tenantId, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("The directors lock only exists inside a transaction.");

        try
        {
            await db.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout = '5s'", ct);
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtext({"users:directors:" + tenantId}))", ct);
            await db.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout = DEFAULT", ct);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.LockNotAvailable)
        {
            throw new ConflictException("Outra alteração de usuários desta empresa está em andamento. Tente de novo.", "users_busy");
        }
    }

    /// <summary>The active directors committed in the company. IgnoreQueryFilters: a save may run with no tenant resolved, the tenant is explicit here.</summary>
    public static IQueryable<User> ActiveDirectors(NexoDbContext db, Guid tenantId)
        => db.Users.IgnoreQueryFilters()
            .Where(u => u.TenantId == tenantId && u.Role == UserRole.Diretoria && u.Status == UserStatus.Active);

    /// <summary>
    /// Deleted, or written with a role or status other than active Diretoria. Whether it was a director is for the committed
    /// rows to say: the values this request loaded may be old.
    /// </summary>
    private static bool Leaves(EntityEntry<User> entry)
        => entry.State == EntityState.Deleted
           || (entry.State == EntityState.Modified && !entry.Entity.IsActiveDirector && RoleOrStatusChanged(entry));

    private static bool Arrives(EntityEntry<User> entry)
        => entry.Entity.IsActiveDirector
           && (entry.State == EntityState.Added || (entry.State == EntityState.Modified && RoleOrStatusChanged(entry)));

    private static bool RoleOrStatusChanged(EntityEntry<User> entry)
        => entry.Property(u => u.Role).IsModified || entry.Property(u => u.Status).IsModified;
}
