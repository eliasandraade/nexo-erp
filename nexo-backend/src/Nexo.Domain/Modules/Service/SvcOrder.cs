using Nexo.Domain.Common;
using Nexo.Domain.Exceptions;

namespace Nexo.Domain.Modules.Service;

/// <summary>
/// An order of service (ordem de serviço) — store-scoped aggregate root. Created manually or
/// from an appointment (<see cref="AppointmentId"/>). Holds <see cref="SvcOrderItem"/> children;
/// <see cref="TotalAmount"/> is recomputed by the application service from the items (never trusted
/// from the client). Status machine: Draft→Open→InProgress→Completed (Cancelled exits); terminal
/// orders cannot be edited nor have items changed.
/// </summary>
public class SvcOrder : StoreEntity
{
    private SvcOrder() { }                                   // EF Core
    private SvcOrder(Guid tenantId) : base(tenantId) { }

    public string         Code               { get; private set; } = string.Empty;
    public Guid           CustomerId         { get; private set; }
    public Guid?          SubjectId          { get; private set; }
    public Guid?          ProfessionalId     { get; private set; }
    public Guid?          AppointmentId      { get; private set; }
    public SvcOrderStatus Status             { get; private set; }
    public string?        Notes              { get; private set; }
    public string?        CancellationReason { get; private set; }
    public decimal        TotalAmount        { get; private set; }

    // Navigation (BuildBudget pattern: public collection; items persisted via the item repository).
    public ICollection<SvcOrderItem> Items { get; private set; } = [];

    public bool IsTerminal => Status is SvcOrderStatus.Completed or SvcOrderStatus.Cancelled;

    public static SvcOrder Create(
        Guid tenantId, string code, Guid customerId,
        Guid? subjectId, Guid? professionalId, Guid? appointmentId, string? notes = null)
    {
        if (string.IsNullOrWhiteSpace(code)) throw new DomainException("Order code is required.");
        if (customerId == Guid.Empty)        throw new DomainException("Customer is required.");

        return new SvcOrder(tenantId)
        {
            Code           = code.Trim(),
            CustomerId     = customerId,
            SubjectId      = subjectId,
            ProfessionalId = professionalId,
            AppointmentId  = appointmentId,
            Status         = SvcOrderStatus.Draft,
            Notes          = notes?.Trim(),
            TotalAmount    = 0m,
        };
    }

    public void UpdateDetails(Guid? subjectId, Guid? professionalId, string? notes)
    {
        EnsureEditable();
        SubjectId      = subjectId;
        ProfessionalId = professionalId;
        Notes          = notes?.Trim();
        SetUpdatedAt();
    }

    /// <summary>Recomputes TotalAmount from the supplied items (server-authoritative).</summary>
    public void RecalculateTotal(IEnumerable<SvcOrderItem> items)
    {
        TotalAmount = items.Sum(i => i.TotalAmount);
        SetUpdatedAt();
    }

    public void ChangeStatus(SvcOrderStatus target, string? reason)
    {
        if (!CanTransition(Status, target))
            throw new DomainException($"Cannot change order status from {Status} to {target}.");
        Status = target;
        if (target == SvcOrderStatus.Cancelled)
            CancellationReason = reason?.Trim();
        SetUpdatedAt();
    }

    /// <summary>Throws if the order is terminal (Completed/Cancelled) — blocks edits and item changes.</summary>
    public void EnsureEditable()
    {
        if (IsTerminal)
            throw new DomainException($"Cannot modify a {Status} order.");
    }

    private static bool CanTransition(SvcOrderStatus from, SvcOrderStatus to) => (from, to) switch
    {
        (SvcOrderStatus.Draft,      SvcOrderStatus.Open)       => true,
        (SvcOrderStatus.Draft,      SvcOrderStatus.Cancelled)  => true,
        (SvcOrderStatus.Open,       SvcOrderStatus.InProgress) => true,
        (SvcOrderStatus.Open,       SvcOrderStatus.Cancelled)  => true,
        (SvcOrderStatus.InProgress, SvcOrderStatus.Completed)  => true,
        (SvcOrderStatus.InProgress, SvcOrderStatus.Cancelled)  => true,
        _ => false,
    };
}
