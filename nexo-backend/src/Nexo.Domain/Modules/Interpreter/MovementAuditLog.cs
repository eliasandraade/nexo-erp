using Nexo.Domain.Common;
using Nexo.Domain.Exceptions;

namespace Nexo.Domain.Modules.Interpreter;

public class MovementAuditLog : TenantEntity
{
    private MovementAuditLog() { }
    private MovementAuditLog(Guid tenantId) : base(tenantId) { }

    public Guid   MovementId     { get; private set; }
    public string Action         { get; private set; } = string.Empty;
    public Guid   ChangedBy      { get; private set; }
    public string PreviousState  { get; private set; } = "{}";
    public string NewState       { get; private set; } = "{}";

    public static MovementAuditLog Record(
        Guid   tenantId,
        Guid   movementId,
        string action,
        Guid   changedBy,
        string previousStateJson,
        string newStateJson)
    {
        if (movementId == Guid.Empty)
            throw new DomainException("MovementId is required.");
        if (string.IsNullOrWhiteSpace(action))
            throw new DomainException("Action is required.");
        if (changedBy == Guid.Empty)
            throw new DomainException("ChangedBy is required.");

        return new MovementAuditLog(tenantId)
        {
            MovementId    = movementId,
            Action        = action,
            ChangedBy     = changedBy,
            PreviousState = previousStateJson,
            NewState      = newStateJson
        };
    }
}
