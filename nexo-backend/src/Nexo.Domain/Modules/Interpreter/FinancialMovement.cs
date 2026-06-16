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
            throw new DomainException("Movement is already voided.");

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
}
