using Nexo.Domain.Enums;
using Nexo.Domain.Common;

namespace Nexo.Domain.Entities;

/// <summary>
/// Represents an active (or historical) subscription of a tenant to a specific module.
/// One record per (tenant, module_key) — updated in place as status changes.
///
/// Billing rules:
///   - Lifetime: CurrentPeriodEnd = null → access never expires.
///   - AdminGrant: StripeSubscriptionId = null, GrantedById set.
///   - PastDue: 7-day grace period, then Canceled.
/// </summary>
public class ModuleSubscription : BaseEntity
{
    private ModuleSubscription() { } // EF Core constructor

    public Guid TenantId { get; private set; }
    public string ModuleKey { get; private set; } = string.Empty;

    // Stripe references (null for admin_grant and lifetime manual)
    public string? StripeSubscriptionId { get; private set; }
    public string? StripePriceId { get; private set; }

    public PlanType PlanType { get; private set; }
    public SubscriptionStatus Status { get; private set; }

    public DateTime CurrentPeriodStart { get; private set; }
    public DateTime? CurrentPeriodEnd { get; private set; }     // null = lifetime

    public bool CancelAtPeriodEnd { get; private set; }
    public DateTime? CanceledAt { get; private set; }

    // For AdminGrant plans
    public Guid? GrantedById { get; private set; }
    public string? Notes { get; private set; }

    // Navigation
    public Tenant? Tenant { get; private set; }
    public PlatformUser? GrantedBy { get; private set; }

    /// <summary>
    /// Creates a subscription activated via Stripe Checkout.
    /// </summary>
    public static ModuleSubscription CreateFromStripe(
        Guid tenantId,
        string moduleKey,
        string stripeSubscriptionId,
        string stripePriceId,
        PlanType planType,
        DateTime periodStart,
        DateTime? periodEnd)
    {
        return new ModuleSubscription
        {
            TenantId = tenantId,
            ModuleKey = moduleKey.ToLowerInvariant(),
            StripeSubscriptionId = stripeSubscriptionId,
            StripePriceId = stripePriceId,
            PlanType = planType,
            Status = SubscriptionStatus.Active,
            CurrentPeriodStart = periodStart,
            CurrentPeriodEnd = periodEnd,   // null for lifetime
            CancelAtPeriodEnd = false,
        };
    }

    /// <summary>
    /// Creates a subscription granted by a platform admin (no Stripe involved).
    /// </summary>
    public static ModuleSubscription CreateAdminGrant(
        Guid tenantId,
        string moduleKey,
        Guid? grantedById = null,
        DateTime? expiresAt = null,
        string? notes = null)
    {
        return new ModuleSubscription
        {
            TenantId = tenantId,
            ModuleKey = moduleKey.ToLowerInvariant(),
            PlanType = PlanType.AdminGrant,
            Status = SubscriptionStatus.Active,
            CurrentPeriodStart = DateTime.UtcNow,
            CurrentPeriodEnd = expiresAt,
            GrantedById = grantedById,
            Notes = notes,
        };
    }

    public bool IsActive()
    {
        if (Status != SubscriptionStatus.Active && Status != SubscriptionStatus.Trialing)
            return false;

        // Lifetime subscriptions never expire
        if (CurrentPeriodEnd == null) return true;

        return CurrentPeriodEnd > DateTime.UtcNow;
    }

    public void Renew(DateTime newPeriodEnd)
    {
        Status = SubscriptionStatus.Active;
        CurrentPeriodStart = DateTime.UtcNow;
        CurrentPeriodEnd = newPeriodEnd;
        SetUpdatedAt();
    }

    public void MarkPastDue()
    {
        Status = SubscriptionStatus.PastDue;
        SetUpdatedAt();
    }

    public void Cancel()
    {
        Status = SubscriptionStatus.Canceled;
        CanceledAt = DateTime.UtcNow;
        SetUpdatedAt();
    }

    public void ScheduleCancellation()
    {
        CancelAtPeriodEnd = true;
        SetUpdatedAt();
    }

    public void UpdateStripeData(string subscriptionId, string priceId)
    {
        StripeSubscriptionId = subscriptionId;
        StripePriceId = priceId;
        SetUpdatedAt();
    }

    /// <summary>
    /// Synchronises local state from a Stripe subscription event
    /// (customer.subscription.updated / checkout.session.completed).
    /// </summary>
    public void SyncFromStripe(
        SubscriptionStatus status,
        DateTime periodStart,
        DateTime? periodEnd,
        bool cancelAtPeriodEnd)
    {
        Status             = status;
        CurrentPeriodStart = periodStart;
        CurrentPeriodEnd   = periodEnd;
        CancelAtPeriodEnd  = cancelAtPeriodEnd;
        SetUpdatedAt();
    }
}
