using Nexo.Domain.Common;

namespace Nexo.Domain.Entities;

/// <summary>
/// Saldo atual de estoque por produto. Uma linha por produto por tenant.
/// Atualizado pelo StockMovementService a cada movimentação.
/// </summary>
public class StockItem : TenantEntity
{
    private StockItem() { }
    private StockItem(Guid tenantId) : base(tenantId) { }

    public Guid ProductId { get; private set; }
    public decimal CurrentQuantity { get; private set; }
    public decimal ReservedQuantity { get; private set; }          // reservado para vendas pendentes
    public DateTime? LastMovementAt { get; private set; }

    /// <summary>
    /// PostgreSQL xmin system column — EF concurrency token.
    /// Guards against race conditions in simultaneous sale confirmations.
    /// Never set manually — owned by the database engine.
    /// </summary>
    public uint RowVersion { get; private set; }

    // Computed
    public decimal AvailableQuantity => CurrentQuantity - ReservedQuantity;

    // Navigation
    public Product? Product { get; private set; }

    public static StockItem Create(Guid tenantId, Guid productId, decimal initialQuantity = 0)
    {
        return new StockItem(tenantId)
        {
            ProductId       = productId,
            CurrentQuantity = initialQuantity,
            ReservedQuantity = 0,
        };
    }

    public void ApplyMovement(decimal quantityChange)
    {
        CurrentQuantity += quantityChange;
        LastMovementAt   = DateTime.UtcNow;
        SetUpdatedAt();
    }

    public void Reserve(decimal quantity)
    {
        ReservedQuantity += quantity;
        SetUpdatedAt();
    }

    public void Unreserve(decimal quantity)
    {
        ReservedQuantity = Math.Max(0, ReservedQuantity - quantity);
        SetUpdatedAt();
    }
}
