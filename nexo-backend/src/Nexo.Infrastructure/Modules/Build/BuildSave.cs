using Microsoft.EntityFrameworkCore;
using Nexo.Domain.Exceptions;
using Nexo.Infrastructure.Persistence;

namespace Nexo.Infrastructure.Modules.Build;

internal static class BuildSave
{
    /// <summary>
    /// Saves; a concurrency-token clash (two people deciding/approving the same record at once — the status is the
    /// token) becomes a 409 instead of a 500.
    /// </summary>
    public static async Task SaveBuildAsync(this NexoDbContext db, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException("Este registro foi alterado ao mesmo tempo por outra pessoa. Recarregue e tente de novo.");
        }
    }
}
