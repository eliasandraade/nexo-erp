using Nexo.Domain.Common;
using Nexo.Domain.Exceptions;

namespace Nexo.Domain.Modules.Service;

/// <summary>
/// Append-only record of a balance consumption. May reference an order/order-item and/or the
/// appointment it covered, for traceability and so the same service is never commissioned twice —
/// those links NEVER change the order's total/status or the appointment.
/// </summary>
public class SvcPackageUsage : StoreEntity
{
    private SvcPackageUsage() { }
    private SvcPackageUsage(Guid tenantId) : base(tenantId) { }

    public Guid    CustomerPackageId     { get; private set; }
    public Guid    CustomerPackageItemId { get; private set; }
    public Guid    CatalogItemId         { get; private set; }
    public Guid?   OrderId               { get; private set; }
    public Guid?   OrderItemId           { get; private set; }
    /// <summary>The appointment this consumption paid for, when there is one.</summary>
    public Guid?   AppointmentId         { get; private set; }
    public decimal Quantity              { get; private set; }
    public string? Notes                 { get; private set; }
    /// <summary>
    /// Who performed the consumed service. Optional: a package can be consumed without naming a
    /// professional, and in that case no commission is earned — there is nobody to pay.
    /// </summary>
    public Guid?   ProfessionalId        { get; private set; }
    /// <summary>
    /// Value attributed to this consumption, prorated from the customer package price at the
    /// moment it happened. The customer paid up front when the package was sold, so this is the
    /// only sensible commission base — and freezing it keeps later package edits out of it.
    /// </summary>
    public decimal? BaseAmountSnapshot   { get; private set; }
    /// <summary>Commission rate in force at consumption. Null means this usage earns none.</summary>
    public decimal? CommissionPercentSnapshot { get; private set; }
    /// <summary>
    /// Set when the consumption was undone (wrong service, wrong customer, service not done): the
    /// units went back to the balance and its commission was reversed. The row stays as history.
    /// </summary>
    public DateTime? ReversedAt          { get; private set; }
    public string?   ReversalReason      { get; private set; }
    public Guid?     ReversedByUserId    { get; private set; }

    public bool IsReversed => ReversedAt is not null;

    public void Reverse(string reason, Guid? userId, DateTime reversedAt)
    {
        if (IsReversed) throw new ConflictException("Este consumo já foi estornado.");
        if (string.IsNullOrWhiteSpace(reason)) throw new DomainException("Informe o motivo do estorno.");
        ReversedAt = reversedAt;
        ReversalReason = reason.Trim().Length > 500 ? reason.Trim()[..500] : reason.Trim();
        ReversedByUserId = userId;
        SetUpdatedAt();
    }

    public static SvcPackageUsage Create(
        Guid tenantId, Guid customerPackageId, Guid customerPackageItemId, Guid catalogItemId,
        decimal quantity, Guid? orderId, Guid? orderItemId, string? notes,
        Guid? professionalId = null, decimal? baseAmountSnapshot = null,
        decimal? commissionPercentSnapshot = null, Guid? appointmentId = null)
    {
        if (customerPackageId == Guid.Empty)     throw new DomainException("CustomerPackageId is required.");
        if (customerPackageItemId == Guid.Empty) throw new DomainException("CustomerPackageItemId is required.");
        if (catalogItemId == Guid.Empty)         throw new DomainException("CatalogItemId is required.");
        if (quantity <= 0m)                      throw new DomainException("Quantity must be positive.");
        return new SvcPackageUsage(tenantId)
        {
            CustomerPackageId = customerPackageId, CustomerPackageItemId = customerPackageItemId,
            CatalogItemId = catalogItemId, OrderId = orderId, OrderItemId = orderItemId,
            Quantity = quantity, Notes = notes?.Trim(),
            ProfessionalId = professionalId, BaseAmountSnapshot = baseAmountSnapshot,
            CommissionPercentSnapshot = commissionPercentSnapshot, AppointmentId = appointmentId,
        };
    }
}
