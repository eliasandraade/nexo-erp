using Nexo.Domain.Common;
using Nexo.Domain.Enums;

namespace Nexo.Domain.Entities;

/// <summary>
/// Registro imutável de cada alteração no estoque.
/// Nunca editado ou deletado — correções são feitas por novos registros de ajuste.
/// </summary>
public class StockMovement : StoreEntity
{
    private StockMovement() { }
    private StockMovement(Guid tenantId) : base(tenantId) { }

    public Guid ProductId { get; private set; }
    public StockMovementType MovementType { get; private set; }
    public decimal Quantity { get; private set; }                  // sempre positivo
    public decimal QuantityBefore { get; private set; }            // snapshot antes
    public decimal QuantityAfter { get; private set; }             // snapshot depois
    public string? ReferenceType { get; private set; }             // "Sale", "Purchase", "Order", "Manual"
    public Guid? ReferenceId { get; private set; }
    public string? Notes { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    /// <summary>
    /// Custo unitário do ingrediente/produto no momento da baixa.
    /// Preenchido em RecipeOutput para preservar histórico de CMV.
    /// </summary>
    public decimal? CostPriceSnapshot { get; private set; }

    // Navigation
    public Product? Product { get; private set; }

    public static StockMovement Create(
        Guid tenantId,
        Guid productId,
        StockMovementType movementType,
        decimal quantity,
        decimal quantityBefore,
        decimal quantityAfter,
        Guid createdByUserId,
        string? referenceType = null,
        Guid? referenceId = null,
        string? notes = null,
        decimal? costPriceSnapshot = null)
    {
        return new StockMovement(tenantId)
        {
            ProductId        = productId,
            MovementType     = movementType,
            Quantity         = Math.Abs(quantity),
            QuantityBefore   = quantityBefore,
            QuantityAfter    = quantityAfter,
            CreatedByUserId  = createdByUserId,
            ReferenceType      = referenceType,
            ReferenceId        = referenceId,
            Notes              = notes?.Trim(),
            CostPriceSnapshot  = costPriceSnapshot,
        };
    }
}
