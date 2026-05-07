using Nexo.Application.Modules.Interpreter.Interfaces;
using Nexo.Domain.Modules.Interpreter;

namespace Nexo.Infrastructure.Modules.Interpreter;

/// <summary>
/// MVP stub: returns empty context JSON so prompt injection works without error.
/// The rebuild is a no-op — a real implementation would summarize confirmed movements
/// into a compact JSONB profile via the MovementMemoryProfile entity.
/// </summary>
public sealed class MovementMemoryServiceImpl : IMovementMemoryService
{
    private readonly IMovementMemoryProfileRepository _profiles;

    public MovementMemoryServiceImpl(IMovementMemoryProfileRepository profiles)
        => _profiles = profiles;

    public async Task<string> GetCompactContextAsync(Guid tenantId, Guid userId, CancellationToken ct = default)
    {
        var profile = await _profiles.GetAsync(tenantId, userId, ct);
        return profile?.Summary ?? "{}";
    }

    public async Task RebuildProfileAsync(Guid tenantId, Guid? userId, CancellationToken ct = default)
    {
        // MVP: upsert a placeholder profile so the entity exists.
        // A real implementation would aggregate the last N confirmed movements
        // into a compact JSON summary and store it.
        var existing = await _profiles.GetAsync(tenantId, userId, ct);

        if (existing is null)
        {
            var profile = MovementMemoryProfile.Create(
                tenantId:   tenantId,
                userId:     userId,
                profileType: userId.HasValue ? ProfileType.User : ProfileType.Tenant);

            profile.Rebuild(summaryJson: "{}", movementsConsidered: 0);
            await _profiles.AddAsync(profile, ct);
        }
        else
        {
            existing.Rebuild(summaryJson: existing.Summary, movementsConsidered: existing.MovementsConsidered);
            _profiles.Update(existing);
        }

        await _profiles.SaveChangesAsync(ct);
    }
}
