using Nexo.Domain.Common;
using Nexo.Domain.Exceptions;

namespace Nexo.Domain.Modules.Interpreter;

public class InterpretationSuggestion : TenantEntity
{
    private InterpretationSuggestion() { }
    private InterpretationSuggestion(Guid tenantId) : base(tenantId) { }

    public Guid MovementId { get; private set; }

    public MovementDirection  SuggestedDirection  { get; private set; }
    public SuggestionSource   DirectionSource     { get; private set; }

    public MovementNature     SuggestedNature     { get; private set; }
    public SuggestionSource   NatureSource        { get; private set; }

    public Guid?              SuggestedCategoryId { get; private set; }
    public SuggestionSource   CategorySource      { get; private set; }

    public FinancialContextType? SuggestedContextType { get; private set; }
    public Guid?                 SuggestedContextId   { get; private set; }
    public SuggestionSource      ContextSource        { get; private set; }

    public Guid?            SuggestedAccountId  { get; private set; }
    public SuggestionSource AccountSource       { get; private set; }

    public bool? WasAccepted { get; private set; }

    public static InterpretationSuggestion Create(
        Guid                 tenantId,
        Guid                 movementId,
        MovementDirection    direction,    SuggestionSource directionSource,
        MovementNature       nature,       SuggestionSource natureSource,
        Guid?                categoryId,   SuggestionSource categorySource,
        FinancialContextType? contextType, Guid? contextId, SuggestionSource contextSource,
        Guid?                accountId,   SuggestionSource accountSource)
    {
        if (movementId == Guid.Empty)
            throw new DomainException("MovementId is required.");

        return new InterpretationSuggestion(tenantId)
        {
            MovementId           = movementId,
            SuggestedDirection   = direction,
            DirectionSource      = directionSource,
            SuggestedNature      = nature,
            NatureSource         = natureSource,
            SuggestedCategoryId  = categoryId,
            CategorySource       = categorySource,
            SuggestedContextType = contextType,
            SuggestedContextId   = contextId,
            ContextSource        = contextSource,
            SuggestedAccountId   = accountId,
            AccountSource        = accountSource,
            WasAccepted          = null
        };
    }

    public void MarkAccepted()
    {
        WasAccepted = true;
        SetUpdatedAt();
    }

    public void MarkRejected()
    {
        WasAccepted = false;
        SetUpdatedAt();
    }
}
