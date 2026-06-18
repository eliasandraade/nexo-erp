using Nexo.Domain.Common;
using Nexo.Domain.Exceptions;

namespace Nexo.Domain.Modules.Service;

/// <summary>
/// A package template (pacote) — store-scoped aggregate. A bundle of catalog services with an
/// included quantity each (<see cref="SvcPackageItem"/>). When assigned to a customer the items
/// become consumable balances (<see cref="SvcCustomerPackage"/>). Price/validity are templates;
/// assignment snapshots them so later edits never touch assigned packages.
/// </summary>
public class SvcPackage : StoreEntity
{
    private SvcPackage() { }
    private SvcPackage(Guid tenantId) : base(tenantId) { }

    public string  Name         { get; private set; } = string.Empty;
    public string? Description  { get; private set; }
    public decimal Price        { get; private set; }
    public int?    ValidityDays { get; private set; }
    public bool    IsActive     { get; private set; }

    public ICollection<SvcPackageItem> Items { get; private set; } = [];

    public static SvcPackage Create(Guid tenantId, string name, decimal price, string? description = null, int? validityDays = null)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new DomainException("Package name is required.");
        EnsurePriceNonNegative(price);
        EnsureValidityPositive(validityDays);
        return new SvcPackage(tenantId)
        {
            Name = name.Trim(), Description = description?.Trim(), Price = price,
            ValidityDays = validityDays, IsActive = true,
        };
    }

    public void UpdateDetails(string name, string? description, int? validityDays)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new DomainException("Package name is required.");
        EnsureValidityPositive(validityDays);
        Name = name.Trim(); Description = description?.Trim(); ValidityDays = validityDays; SetUpdatedAt();
    }

    public void UpdatePrice(decimal price) { EnsurePriceNonNegative(price); Price = price; SetUpdatedAt(); }
    public void Activate()   { IsActive = true;  SetUpdatedAt(); }
    public void Deactivate() { IsActive = false; SetUpdatedAt(); }

    private static void EnsurePriceNonNegative(decimal p) { if (p < 0m) throw new DomainException("Price cannot be negative."); }
    private static void EnsureValidityPositive(int? d) { if (d is <= 0) throw new DomainException("ValidityDays must be positive when set."); }
}
