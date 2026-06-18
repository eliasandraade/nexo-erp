using Nexo.Domain.Common;
using Nexo.Domain.Exceptions;

namespace Nexo.Domain.Modules.Service;

/// <summary>
/// A package assigned to a customer — store-scoped aggregate. Holds the consumable balances
/// (<see cref="SvcCustomerPackageItem"/>). Status machine: Active → Consumed (auto when all
/// balances reach zero) or Cancelled; Expired is reserved for a future job (consume is blocked
/// by date here). Grants only the operational right of use — no payment/cash/financial effect.
/// </summary>
public class SvcCustomerPackage : StoreEntity
{
    private SvcCustomerPackage() { }
    private SvcCustomerPackage(Guid tenantId) : base(tenantId) { }

    public string                   Code          { get; private set; } = string.Empty;
    public Guid                     PackageId     { get; private set; }
    public Guid                     CustomerId    { get; private set; }
    public Guid?                    SubjectId     { get; private set; }
    public SvcCustomerPackageStatus Status        { get; private set; }
    public DateTime                 StartsAt      { get; private set; }
    public DateTime?                ExpiresAt     { get; private set; }
    public decimal                  PriceSnapshot { get; private set; }
    public string?                  Notes         { get; private set; }

    public ICollection<SvcCustomerPackageItem> Items { get; private set; } = [];

    public bool IsTerminal => Status is SvcCustomerPackageStatus.Consumed
                                     or SvcCustomerPackageStatus.Expired
                                     or SvcCustomerPackageStatus.Cancelled;

    public static SvcCustomerPackage Create(
        Guid tenantId, string code, Guid packageId, Guid customerId, Guid? subjectId,
        DateTime startsAt, DateTime? expiresAt, decimal priceSnapshot, string? notes)
    {
        if (string.IsNullOrWhiteSpace(code)) throw new DomainException("Code is required.");
        if (packageId == Guid.Empty)         throw new DomainException("Package is required.");
        if (customerId == Guid.Empty)        throw new DomainException("Customer is required.");
        if (priceSnapshot < 0m)              throw new DomainException("Price snapshot cannot be negative.");
        if (expiresAt is { } e && e <= startsAt) throw new DomainException("ExpiresAt must be after StartsAt.");
        return new SvcCustomerPackage(tenantId)
        {
            Code = code.Trim(), PackageId = packageId, CustomerId = customerId, SubjectId = subjectId,
            Status = SvcCustomerPackageStatus.Active, StartsAt = startsAt, ExpiresAt = expiresAt,
            PriceSnapshot = priceSnapshot, Notes = notes?.Trim(),
        };
    }

    public void Cancel()
    {
        if (IsTerminal) throw new DomainException($"Cannot cancel a {Status} customer package.");
        Status = SvcCustomerPackageStatus.Cancelled; SetUpdatedAt();
    }

    public void MarkConsumed()
    {
        if (Status != SvcCustomerPackageStatus.Active)
            throw new DomainException($"Only an active package can be marked consumed (current: {Status}).");
        Status = SvcCustomerPackageStatus.Consumed; SetUpdatedAt();
    }

    public bool IsExpiredAt(DateTime nowUtc) => ExpiresAt is { } e && e < nowUtc;
}
