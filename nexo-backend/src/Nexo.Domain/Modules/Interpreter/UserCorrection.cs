using Nexo.Domain.Common;
using Nexo.Domain.Exceptions;

namespace Nexo.Domain.Modules.Interpreter;

// Explicit learning dataset. Each corrected field is a separate record
// to allow granular analytics per correction type and source.
public class UserCorrection : TenantEntity
{
    private UserCorrection() { }
    private UserCorrection(Guid tenantId) : base(tenantId) { }

    public Guid           SuggestionId    { get; private set; }
    public Guid           MovementId      { get; private set; }
    public Guid           UserId          { get; private set; }
    public CorrectionType CorrectionType  { get; private set; }
    public string         OriginalValue   { get; private set; } = string.Empty;
    public string         CorrectedValue  { get; private set; } = string.Empty;
    public string?        RawUserText     { get; private set; }

    public static UserCorrection Create(
        Guid           tenantId,
        Guid           suggestionId,
        Guid           movementId,
        Guid           userId,
        CorrectionType correctionType,
        string         originalValue,
        string         correctedValue,
        string?        rawUserText)
    {
        if (suggestionId == Guid.Empty)
            throw new DomainException("SuggestionId is required.");
        if (movementId == Guid.Empty)
            throw new DomainException("MovementId is required.");
        if (userId == Guid.Empty)
            throw new DomainException("UserId is required.");

        return new UserCorrection(tenantId)
        {
            SuggestionId   = suggestionId,
            MovementId     = movementId,
            UserId         = userId,
            CorrectionType = correctionType,
            OriginalValue  = originalValue,
            CorrectedValue = correctedValue,
            RawUserText    = rawUserText
        };
    }
}
