using System.Linq.Expressions;
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
    /// <summary>When the payment started failing — the grace period counts from here.</summary>
    public DateTime? PastDueSince { get; private set; }

    /// <summary>Days a PastDue subscription keeps access while the card is retried.</summary>
    public const int PastDueGraceDays = 7;

    /// <summary>
    /// The single access rule (used by every gate and query): active or trialing within the period
    /// (null end = lifetime), or past due for less than <see cref="PastDueGraceDays"/> days.
    /// </summary>
    public static Expression<Func<ModuleSubscription, bool>> HasAccessAt(DateTime nowUtc)
    {
        var graceStart = nowUtc.AddDays(-PastDueGraceDays);
        return s =>
            ((s.Status == SubscriptionStatus.Active || s.Status == SubscriptionStatus.Trialing)
                && (s.CurrentPeriodEnd == null || s.CurrentPeriodEnd > nowUtc))
            || (s.Status == SubscriptionStatus.PastDue && s.PastDueSince != null && s.PastDueSince > graceStart);
    }
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
    /// Creates a time-limited trial subscription. Status = Trialing, expires after <paramref name="days"/> days.
    /// </summary>
    public static ModuleSubscription CreateTrial(Guid tenantId, string moduleKey, int days = 7)
    {
        return new ModuleSubscription
        {
            TenantId           = tenantId,
            ModuleKey          = moduleKey.ToLowerInvariant(),
            PlanType           = PlanType.Trial,
            Status             = SubscriptionStatus.Trialing,
            CurrentPeriodStart = DateTime.UtcNow,
            CurrentPeriodEnd   = DateTime.UtcNow.AddDays(days),
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

    public bool IsActive() => HasAccessAt(DateTime.UtcNow).Compile()(this);

    public void Renew(DateTime newPeriodEnd)
    {
        Status = SubscriptionStatus.Active;
        CurrentPeriodStart = DateTime.UtcNow;
        CurrentPeriodEnd = newPeriodEnd;
        SetUpdatedAt();
    }

    public void MarkPastDue(DateTime? nowUtc = null)
    {
        Status = SubscriptionStatus.PastDue;
        PastDueSince ??= nowUtc ?? DateTime.UtcNow;   // the grace counts from the first failure
        SetUpdatedAt();
    }

    /// <summary>A (re)checkout completed: this row now follows that Stripe subscription.</summary>
    public void AttachStripe(string subscriptionId, string priceId, PlanType planType)
    {
        StripeSubscriptionId = subscriptionId;
        StripePriceId        = priceId;
        PlanType             = planType;
        CanceledAt           = null;
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
        PastDueSince       = status == SubscriptionStatus.PastDue ? PastDueSince ?? DateTime.UtcNow : null;
        if (status == SubscriptionStatus.Canceled) CanceledAt ??= DateTime.UtcNow;
        SetUpdatedAt();
    }
}
