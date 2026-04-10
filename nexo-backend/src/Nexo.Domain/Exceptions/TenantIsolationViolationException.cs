namespace Nexo.Domain.Exceptions;

/// <summary>
/// Thrown when an entity's TenantId does not match the current tenant context.
/// This indicates an attempt to write data belonging to tenant A while authenticated as tenant B.
/// Always treated as a critical security event and logged to the audit trail.
/// </summary>
public class TenantIsolationViolationException : Exception
{
    public Guid AttemptedTenantId { get; }
    public Guid CurrentTenantId { get; }
    public string EntityType { get; }

    public TenantIsolationViolationException(
        string entityType,
        Guid attemptedTenantId,
        Guid currentTenantId)
        : base(
            $"Tenant isolation violation: attempted to write '{entityType}' " +
            $"with TenantId '{attemptedTenantId}' " +
            $"while current tenant is '{currentTenantId}'.")
    {
        EntityType = entityType;
        AttemptedTenantId = attemptedTenantId;
        CurrentTenantId = currentTenantId;
    }
}
