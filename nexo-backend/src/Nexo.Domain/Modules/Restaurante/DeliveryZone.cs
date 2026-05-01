using Nexo.Domain.Common;
using Nexo.Domain.Exceptions;

namespace Nexo.Domain.Modules.Restaurante;

/// <summary>
/// Represents a delivery neighborhood configured by the restaurant owner.
/// Existence = active (no IsActive flag — owners delete to deactivate).
/// </summary>
public class DeliveryZone : StoreEntity
{
    private DeliveryZone() { }
    private DeliveryZone(Guid tenantId) : base(tenantId) { }

    public string  Neighborhood { get; private set; } = string.Empty;
    public decimal Fee          { get; private set; }

    public static DeliveryZone Create(Guid tenantId, string neighborhood, decimal fee)
    {
        if (string.IsNullOrWhiteSpace(neighborhood))
            throw new DomainException("Neighborhood is required.");
        if (fee < 0)
            throw new DomainException("Fee cannot be negative.");

        return new DeliveryZone(tenantId)
        {
            Neighborhood = neighborhood.Trim(),
            Fee          = fee,
        };
    }

    public void Update(string neighborhood, decimal fee)
    {
        if (string.IsNullOrWhiteSpace(neighborhood))
            throw new DomainException("Neighborhood is required.");
        if (fee < 0)
            throw new DomainException("Fee cannot be negative.");

        Neighborhood = neighborhood.Trim();
        Fee          = fee;
        SetUpdatedAt();
    }
}
