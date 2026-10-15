using Nexo.Domain.Common;
using Nexo.Domain.Exceptions;

namespace Nexo.Domain.Modules.Service;

/// <summary>
/// Bloqueio de agenda: a period no one can book — a professional's day off or lunch, or (no
/// professional) the whole store closed for a holiday. Internal and public booking both respect it.
/// </summary>
public class SvcTimeBlock : StoreEntity
{
    private SvcTimeBlock() { }
    private SvcTimeBlock(Guid tenantId) : base(tenantId) { }

    /// <summary>The professional blocked; null blocks every professional of the store.</summary>
    public Guid?    ProfessionalId { get; private set; }
    public DateTime StartsAt       { get; private set; }
    public DateTime EndsAt         { get; private set; }
    public string?  Reason         { get; private set; }
    public Guid     CreatedBy      { get; private set; }

    public static SvcTimeBlock Create(
        Guid tenantId, Guid? professionalId, DateTime startsAt, DateTime endsAt, string? reason, Guid createdBy)
    {
        if (startsAt >= endsAt)
            throw new DomainException("O bloqueio precisa terminar depois de começar.");
        if ((endsAt - startsAt).TotalDays > 366)
            throw new DomainException("Um bloqueio vai até um ano; para mais, crie outro.");
        var trimmed = reason?.Trim();
        if (trimmed is { Length: > 200 })
            throw new DomainException("Motivo do bloqueio com até 200 caracteres.");

        return new SvcTimeBlock(tenantId)
        {
            ProfessionalId = professionalId is { } p && p != Guid.Empty ? p : null,
            StartsAt       = DateTime.SpecifyKind(startsAt, DateTimeKind.Utc),
            EndsAt         = DateTime.SpecifyKind(endsAt, DateTimeKind.Utc),
            Reason         = string.IsNullOrEmpty(trimmed) ? null : trimmed,
            CreatedBy      = createdBy,
        };
    }

    /// <summary>True when the block covers part of [start, end) for the given professional.</summary>
    public bool Blocks(Guid professionalId, DateTime start, DateTime end)
        => (ProfessionalId is null || ProfessionalId == professionalId) && StartsAt < end && EndsAt > start;
}
