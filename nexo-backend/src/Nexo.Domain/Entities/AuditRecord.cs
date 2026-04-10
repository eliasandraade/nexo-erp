using Nexo.Domain.Common;

namespace Nexo.Domain.Entities;

/// <summary>
/// Immutable audit log entry.
/// Written synchronously inside business transactions (same DbContext scope).
/// Never modified or deleted after creation.
///
/// TenantId is nullable:
///   - Non-null: action performed within a tenant context (most records)
///   - Null: platform-level action (Stripe webhook, admin login, system job)
/// </summary>
public class AuditRecord : BaseEntity
{
    private AuditRecord() { } // EF Core constructor

    /// <summary>
    /// Tenant context. Null for platform-level actions.
    /// Intentionally NOT a TenantEntity subclass — audit records are never
    /// filtered by tenant at the DB level; they are accessed through tenant-aware
    /// queries explicitly.
    /// </summary>
    public Guid? TenantId { get; private set; }

    /// <summary>
    /// Semantic action type. Examples: "sale_completed", "cash_open", "module_activated"
    /// </summary>
    public string ActionType { get; private set; } = string.Empty;

    /// <summary>"info" | "warning" | "critical"</summary>
    public string Severity { get; private set; } = string.Empty;

    /// <summary>User or system actor who performed the action.</summary>
    public Guid? ActorId { get; private set; }

    /// <summary>Denormalized actor name for display without a JOIN.</summary>
    public string? ActorName { get; private set; }

    /// <summary>"user" | "system" | "stripe_webhook"</summary>
    public string ActorType { get; private set; } = "user";

    /// <summary>The type of entity affected, e.g. "Sale", "ModuleSubscription".</summary>
    public string EntityType { get; private set; } = string.Empty;

    /// <summary>UUID or displayId of the affected entity.</summary>
    public string EntityId { get; private set; } = string.Empty;

    public string Description { get; private set; } = string.Empty;

    /// <summary>Optional JSON blob with operation-specific context.</summary>
    public string? MetadataJson { get; private set; }

    public string? IpAddress { get; private set; }

    public static AuditRecord Create(
        string actionType,
        string severity,
        string entityType,
        string entityId,
        string description,
        Guid? tenantId = null,
        Guid? actorId = null,
        string? actorName = null,
        string actorType = "user",
        string? metadataJson = null,
        string? ipAddress = null)
    {
        return new AuditRecord
        {
            TenantId = tenantId,
            ActionType = actionType,
            Severity = severity,
            EntityType = entityType,
            EntityId = entityId,
            Description = description,
            ActorId = actorId,
            ActorName = actorName,
            ActorType = actorType,
            MetadataJson = metadataJson,
            IpAddress = ipAddress,
            // BaseEntity sets Id and CreatedAt; UpdatedAt not used (immutable)
        };
    }
}
