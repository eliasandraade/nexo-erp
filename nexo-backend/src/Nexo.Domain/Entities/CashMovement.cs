using Nexo.Domain.Common;
using Nexo.Domain.Enums;

namespace Nexo.Domain.Entities;

/// <summary>Registro imutável de cada entrada ou saída na sessão de caixa.</summary>
public class CashMovement : TenantEntity
{
    private CashMovement() { }
    private CashMovement(Guid tenantId) : base(tenantId) { }

    public Guid CashSessionId { get; private set; }
    public CashMovementType MovementType { get; private set; }
    public decimal Amount { get; private set; }                    // sempre positivo; tipo define direção
    public string Description { get; private set; } = string.Empty;
    public string? ReferenceType { get; private set; }             // "Sale"
    public Guid? ReferenceId { get; private set; }
    public Guid CreatedByUserId { get; private set; }

    // Navigation
    public CashSession? CashSession { get; private set; }

    public static CashMovement Create(
        Guid tenantId,
        Guid cashSessionId,
        CashMovementType movementType,
        decimal amount,
        string description,
        Guid createdByUserId,
        string? referenceType = null,
        Guid? referenceId = null)
    {
        return new CashMovement(tenantId)
        {
            CashSessionId    = cashSessionId,
            MovementType     = movementType,
            Amount           = Math.Abs(amount),
            Description      = description.Trim(),
            CreatedByUserId  = createdByUserId,
            ReferenceType    = referenceType,
            ReferenceId      = referenceId,
        };
    }
}
