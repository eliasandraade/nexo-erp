using Nexo.Domain.Common;
using Nexo.Domain.Exceptions;

namespace Nexo.Domain.Modules.Interpreter;

// Compact, prompt-ready summary of movement patterns for a tenant or user.
// ProfileVersion increments on every rebuild — simple int, not semver.
// Rebuild is always async/decoupled; this entity is never mutated inline.
public class MovementMemoryProfile : TenantEntity
{
    private MovementMemoryProfile() { }
    private MovementMemoryProfile(Guid tenantId) : base(tenantId) { }

    public Guid?       UserId               { get; private set; }
    public ProfileType ProfileType          { get; private set; }
    public int         ProfileVersion       { get; private set; }
    public string      Summary              { get; private set; } = "{}";
    public DateTime    LastRebuildAt        { get; private set; }
    public int         MovementsConsidered  { get; private set; }

    public static MovementMemoryProfile Create(
        Guid        tenantId,
        Guid?       userId,
        ProfileType profileType)
    {
        return new MovementMemoryProfile(tenantId)
        {
            UserId              = userId,
            ProfileType         = profileType,
            ProfileVersion      = 0,
            Summary             = "{}",
            LastRebuildAt       = DateTime.UtcNow,
            MovementsConsidered = 0
        };
    }

    public void Rebuild(string summaryJson, int movementsConsidered)
    {
        if (string.IsNullOrWhiteSpace(summaryJson))
            throw new DomainException("Summary JSON cannot be empty.");
        if (movementsConsidered < 0)
            throw new DomainException("MovementsConsidered cannot be negative.");

        Summary              = summaryJson;
        MovementsConsidered  = movementsConsidered;
        ProfileVersion       += 1;
        LastRebuildAt        = DateTime.UtcNow;
        SetUpdatedAt();
    }
}
