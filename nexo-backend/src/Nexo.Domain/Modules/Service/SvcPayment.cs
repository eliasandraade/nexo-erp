using Nexo.Domain.Common;
using Nexo.Domain.Exceptions;

namespace Nexo.Domain.Modules.Service;

/// <summary>
/// A manual record that a payment was received in the Service context, against exactly one target —
/// an <c>SvcOrder</c> OR an <c>SvcCustomerPackage</c>. Store-scoped. This is ONLY a record: it never
/// touches the order total/status, the package balance/status, nor any global financial/cash entity.
/// Status: Paid → Voided (operational correction; no delete — history is preserved).
/// </summary>
public class SvcPayment : StoreEntity
{
    private SvcPayment() { }
    private SvcPayment(Guid tenantId) : base(tenantId) { }

    public Guid             CustomerId        { get; private set; }
    public Guid?            OrderId           { get; private set; }
    public Guid?            CustomerPackageId { get; private set; }
    public decimal          Amount            { get; private set; }
    public SvcPaymentMethod Method            { get; private set; }
    public SvcPaymentStatus Status            { get; private set; }
    public DateTime         PaidAt            { get; private set; }
    public string?          ExternalReference { get; private set; }
    public string?          Notes             { get; private set; }
    public string?          VoidReason        { get; private set; }
    public DateTime?        VoidedAt          { get; private set; }

    public static SvcPayment CreateForOrder(
        Guid tenantId, Guid customerId, Guid orderId, decimal amount, SvcPaymentMethod method,
        DateTime paidAt, string? externalReference, string? notes)
        => Create(tenantId, customerId, orderId, null, amount, method, paidAt, externalReference, notes);

    public static SvcPayment CreateForCustomerPackage(
        Guid tenantId, Guid customerId, Guid customerPackageId, decimal amount, SvcPaymentMethod method,
        DateTime paidAt, string? externalReference, string? notes)
        => Create(tenantId, customerId, null, customerPackageId, amount, method, paidAt, externalReference, notes);

    private static SvcPayment Create(
        Guid tenantId, Guid customerId, Guid? orderId, Guid? customerPackageId, decimal amount,
        SvcPaymentMethod method, DateTime paidAt, string? externalReference, string? notes)
    {
        if (customerId == Guid.Empty) throw new DomainException("Customer is required.");
        if (amount <= 0m)             throw new DomainException("Amount must be positive.");
        if ((orderId is null) == (customerPackageId is null))
            throw new DomainException("Exactly one of OrderId or CustomerPackageId must be set.");

        return new SvcPayment(tenantId)
        {
            CustomerId = customerId, OrderId = orderId, CustomerPackageId = customerPackageId,
            Amount = amount, Method = method, Status = SvcPaymentStatus.Paid, PaidAt = paidAt,
            ExternalReference = externalReference?.Trim(), Notes = notes?.Trim(),
        };
    }

    public void Void(string? reason)
    {
        if (Status != SvcPaymentStatus.Paid)
            throw new DomainException($"Only a Paid payment can be voided (current: {Status}).");
        Status     = SvcPaymentStatus.Voided;
        VoidReason = reason?.Trim();
        VoidedAt   = DateTime.UtcNow;
        SetUpdatedAt();
    }
}
