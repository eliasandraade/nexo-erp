using Nexo.Domain.Common;

namespace Nexo.Domain.Entities;

/// <summary>
/// Append-only audit trail for module subscription lifecycle events.
/// Records: granted, revoked, renewed, plan_changed.
/// Never updated — one row per action.
/// </summary>
public class ModuleSubscriptionEvent : BaseEntity
{
    private ModuleSubscriptionEvent() { } // EF Core constructor

    public Guid TenantId { get; private set; }
    public string ModuleKey { get; private set; } = string.Empty;

    /// <summary>
    /// Event type: "granted" | "revoked" | "renewed" | "plan_changed"
    /// </summary>
    public string EventType { get; private set; } = string.Empty;

    /// <summary>Platform user who triggered the event (null for system events).</summary>
    public Guid? ActorId { get; private set; }

    public string? Notes { get; private set; }

    /// <summary>Plan type at time of event (null for revoke).</summary>
    public string? PlanType { get; private set; }

    /// <summary>Period end at time of event (null if lifetime or revoke).</summary>
    public DateTime? PeriodEnd { get; private set; }

    // Navigation
    public Tenant? Tenant { get; private set; }

    public static ModuleSubscriptionEvent Create(
        Guid tenantId,
        string moduleKey,
        string eventType,
        Guid? actorId = null,
        string? notes = null,
        string? planType = null,
        DateTime? periodEnd = null)
    {
        return new ModuleSubscriptionEvent
        {
            TenantId  = tenantId,
            ModuleKey = moduleKey.ToLowerInvariant(),
            EventType = eventType,
            ActorId   = actorId,
            Notes     = notes,
            PlanType  = planType,
            PeriodEnd = periodEnd,
        };
    }
}
