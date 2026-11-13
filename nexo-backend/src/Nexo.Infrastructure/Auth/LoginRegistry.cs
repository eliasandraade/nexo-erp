using Microsoft.EntityFrameworkCore;
using Nexo.Domain.Entities;
using Nexo.Infrastructure.Persistence;
using Npgsql;

namespace Nexo.Infrastructure.Auth;

/// <summary>
/// The rule every way of creating an account asks: is this login free? Sign-in receives a login and no company,
/// so a login belongs to one account in the whole system. A platform operator's e-mail counts as taken too,
/// because the shared sign-in form tries the platform first and a company user with that login could never enter.
/// The unique index on users.login is what holds under concurrency; this check is what answers before it.
/// </summary>
public static class LoginRegistry
{
    /// <summary>The unique index on users.login (UserConfiguration).</summary>
    public const string UniqueIndex = "IX_users_login";

    /// <summary>The older unique index on (tenant_id, login): inside one company it is the one that answers first.</summary>
    public const string UniqueIndexInCompany = "IX_users_tenant_id_login";

    public static async Task<bool> IsTakenAsync(NexoDbContext db, string? login, CancellationToken ct = default)
    {
        var normalized = User.NormalizeLogin(login);
        return await db.Users.IgnoreQueryFilters().AnyAsync(u => u.Login == normalized, ct)
            || await db.PlatformUsers.AnyAsync(u => u.Email == normalized, ct);
    }

    /// <summary>True when a save lost the race for a login: two requests passed the check and the index refused the second.</summary>
    public static bool LostTheLogin(DbUpdateException ex)
        => ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: UniqueIndex or UniqueIndexInCompany };
}
