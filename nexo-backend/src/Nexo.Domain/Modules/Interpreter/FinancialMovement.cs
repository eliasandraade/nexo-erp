using Nexo.Domain.Common;
using Nexo.Domain.Exceptions;

namespace Nexo.Domain.Modules.Interpreter;

public class FinancialMovement : TenantEntity
{
    private FinancialMovement() { }
    private FinancialMovement(Guid tenantId) : base(tenantId) { }

    public MovementDirection    Direction             { get; private set; }
    public MovementNature       Nature                { get; private set; }
    public decimal              Amount                { get; private set; }
    public DateOnly             Date                  { get; private set; }
    public string               Description           { get; private set; } = string.Empty;
    public string               NormalizedDescription { get; private set; } = string.Empty;
    public Guid?                CategoryId            { get; private set; }
    public FinancialContextType ContextType           { get; private set; }
    public Guid?                ContextId             { get; private set; }
    public Guid?                AccountId             { get; private set; }
    /// <summary>Optional link to a registered Supplier (counterparty). Additive — null for legacy movements.</summary>
    public Guid?                SupplierId            { get; private set; }
    public MovementStatus       Status                { get; private set; }
    /// <summary>
    /// Optional stage of the construction project (ContextType Obra) this cost belongs to — cost per stage
    /// without a parallel ledger. Additive: null for legacy movements and for every other context.
    /// </summary>
    public Guid?                StageId               { get; private set; }
    public Guid                 CreatedBy             { get; private set; }

    public static FinancialMovement CreateDraft(
        Guid                tenantId,
        Guid                createdBy,
        MovementDirection   direction,
        MovementNature      nature,
        decimal             amount,
        DateOnly            date,
        string              description,
        string              normalizedDescription,
        FinancialContextType contextType,
        Guid?               contextId,
        Guid?               categoryId,
        Guid?               accountId,
        Guid?               supplierId = null)
    {
        if (createdBy == Guid.Empty)
            throw new DomainException("CreatedBy is required.");
        if (amount < 0)
            throw new DomainException("Amount cannot be negative.");
        if (string.IsNullOrWhiteSpace(description))
            throw new DomainException("Description is required.");

        return new FinancialMovement(tenantId)
        {
            Direction             = direction,
            Nature                = nature,
            Amount                = amount,
            Date                  = date,
            Description           = description.Trim(),
            NormalizedDescription = normalizedDescription,
            ContextType           = contextType,
            ContextId             = contextId,
            CategoryId            = categoryId,
            AccountId             = accountId,
            SupplierId            = supplierId,
            Status                = MovementStatus.Draft,
            CreatedBy             = createdBy
        };
    }

    public void Confirm()
    {
        if (Status != MovementStatus.Draft)
            throw new DomainException($"Only Draft movements can be confirmed. Current status: {Status}.");

        Status = MovementStatus.Confirmed;
        SetUpdatedAt();
    }

    public void Void()
    {
        if (Status == MovementStatus.Voided)
            throw new DomainException("Esta despesa já foi estornada.");

        Status = MovementStatus.Voided;
        SetUpdatedAt();
    }

    // Returns to Draft for reprocessing. A new ExtractionResult + Suggestion will be generated.
    public void ResetToDraft()
    {
        if (Status == MovementStatus.Voided)
            throw new DomainException("Voided movements cannot be reprocessed.");

        Status = MovementStatus.Draft;
        SetUpdatedAt();
    }

    public void UpdateFields(
        MovementDirection    direction,
        MovementNature       nature,
        decimal              amount,
        DateOnly             date,
        string               description,
        string               normalizedDescription,
        FinancialContextType contextType,
        Guid?                contextId,
        Guid?                categoryId,
        Guid?                accountId,
        Guid?                supplierId = null)
    {
        if (Status != MovementStatus.Draft)
            throw new DomainException("Only Draft movements can be edited.");
        if (amount < 0)
            throw new DomainException("Amount cannot be negative.");
        if (string.IsNullOrWhiteSpace(description))
            throw new DomainException("Description is required.");

        Direction             = direction;
        Nature                = nature;
        Amount                = amount;
        Date                  = date;
        Description           = description.Trim();
        NormalizedDescription = normalizedDescription;
        ContextType           = contextType;
        ContextId             = contextId;
        CategoryId            = categoryId;
        AccountId             = accountId;
        SupplierId            = supplierId;
        SetUpdatedAt();
    }

    /// <summary>
    /// Assigns (or clears, with null) the project stage of a construction cost. Allowed on draft and
    /// confirmed movements of a project (ContextType Obra); the amount and the ledger are untouched.
    /// </summary>
    public void AssignStage(Guid? stageId)
    {
        if (ContextType != FinancialContextType.Obra || ContextId is null)
            throw new DomainException("Só lançamentos de uma obra podem ser ligados a uma etapa.");
        if (Status == MovementStatus.Voided)
            throw new DomainException("Este lançamento foi estornado e não pode mais ser ligado a uma etapa.");
        StageId = stageId == Guid.Empty ? null : stageId;
        SetUpdatedAt();
    }
}
