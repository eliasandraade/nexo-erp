using Nexo.Domain.Common;
using Nexo.Domain.Exceptions;

namespace Nexo.Domain.Modules.Service;

/// <summary>
/// A booking in the Orken Service agenda — store-scoped. Links a customer, a professional, a
/// catalog item, and (optionally) a subject (pet/veículo/aluno). <see cref="PriceSnapshot"/>
/// copies the catalog price at booking/reschedule time so later catalog price changes never
/// rewrite history. The status state machine (Scheduled→Confirmed→InProgress→Completed, with
/// Cancelled/NoShow exits) is enforced here; cross-entity rules (active professional/catalog,
/// subject ownership, overlap) live in the application service.
/// </summary>
public class SvcAppointment : StoreEntity
{
    private SvcAppointment() { }                                   // EF Core
    private SvcAppointment(Guid tenantId) : base(tenantId) { }

    public Guid                 CustomerId         { get; private set; }
    public Guid                 ProfessionalId     { get; private set; }
    public Guid                 CatalogItemId      { get; private set; }
    public Guid?                SubjectId          { get; private set; }
    public DateTime             StartsAt           { get; private set; }
    public DateTime             EndsAt             { get; private set; }
    public SvcAppointmentStatus Status             { get; private set; }
    public string?              Notes              { get; private set; }
    public string?              CancellationReason { get; private set; }
    public decimal              PriceSnapshot      { get; private set; }

    public bool IsTerminal => Status is SvcAppointmentStatus.Completed
                                     or SvcAppointmentStatus.Cancelled
                                     or SvcAppointmentStatus.NoShow;

    public static SvcAppointment Create(
        Guid tenantId, Guid customerId, Guid professionalId, Guid catalogItemId,
        Guid? subjectId, DateTime startsAt, DateTime endsAt, decimal priceSnapshot, string? notes = null)
    {
        EnsureValid(customerId, professionalId, catalogItemId, startsAt, endsAt, priceSnapshot);
        return new SvcAppointment(tenantId)
        {
            CustomerId     = customerId,
            ProfessionalId = professionalId,
            CatalogItemId  = catalogItemId,
            SubjectId      = subjectId,
            StartsAt       = startsAt,
            EndsAt         = endsAt,
            PriceSnapshot  = priceSnapshot,
            Notes          = notes?.Trim(),
            Status         = SvcAppointmentStatus.Scheduled,
        };
    }

    public void Reschedule(
        Guid customerId, Guid professionalId, Guid catalogItemId,
        Guid? subjectId, DateTime startsAt, DateTime endsAt, decimal priceSnapshot, string? notes)
    {
        if (IsTerminal)
            throw new DomainException($"Cannot edit a {Status} appointment.");
        EnsureValid(customerId, professionalId, catalogItemId, startsAt, endsAt, priceSnapshot);

        CustomerId     = customerId;
        ProfessionalId = professionalId;
        CatalogItemId  = catalogItemId;
        SubjectId      = subjectId;
        StartsAt       = startsAt;
        EndsAt         = endsAt;
        PriceSnapshot  = priceSnapshot;
        Notes          = notes?.Trim();
        SetUpdatedAt();
    }

    public void ChangeStatus(SvcAppointmentStatus target, string? reason)
    {
        if (!CanTransition(Status, target))
            throw new DomainException($"Cannot change appointment status from {Status} to {target}.");

        Status = target;
        if (target == SvcAppointmentStatus.Cancelled)
            CancellationReason = reason?.Trim();
        SetUpdatedAt();
    }

    private static void EnsureValid(
        Guid customerId, Guid professionalId, Guid catalogItemId,
        DateTime startsAt, DateTime endsAt, decimal priceSnapshot)
    {
        if (customerId == Guid.Empty)     throw new DomainException("Customer is required.");
        if (professionalId == Guid.Empty) throw new DomainException("Professional is required.");
        if (catalogItemId == Guid.Empty)  throw new DomainException("Catalog item is required.");
        if (startsAt >= endsAt)           throw new DomainException("StartsAt must be before EndsAt.");
        if (priceSnapshot < 0m)           throw new DomainException("Price snapshot cannot be negative.");
    }

    private static bool CanTransition(SvcAppointmentStatus from, SvcAppointmentStatus to) => (from, to) switch
    {
        (SvcAppointmentStatus.Scheduled,  SvcAppointmentStatus.Confirmed)  => true,
        (SvcAppointmentStatus.Scheduled,  SvcAppointmentStatus.Cancelled)  => true,
        (SvcAppointmentStatus.Scheduled,  SvcAppointmentStatus.NoShow)     => true,
        (SvcAppointmentStatus.Confirmed,  SvcAppointmentStatus.InProgress) => true,
        (SvcAppointmentStatus.Confirmed,  SvcAppointmentStatus.Cancelled)  => true,
        (SvcAppointmentStatus.Confirmed,  SvcAppointmentStatus.NoShow)     => true,
        (SvcAppointmentStatus.InProgress, SvcAppointmentStatus.Completed)  => true,
        (SvcAppointmentStatus.InProgress, SvcAppointmentStatus.Cancelled)  => true,
        _ => false,
    };
}
