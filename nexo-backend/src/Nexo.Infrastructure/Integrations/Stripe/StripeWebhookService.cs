using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Stripe;
using Nexo.Application.Common.Interfaces;
using Nexo.Application.Integrations.Billing;
using Nexo.Application.Integrations.Options;
using Nexo.Domain.Entities;
using Nexo.Domain.Enums;

namespace Nexo.Infrastructure.Integrations.Stripe;

/// <summary>
/// Validates Stripe webhooks, enforces idempotency, and routes to per-event handlers.
///
/// Stripe does not order events, so a handler never applies the state in the event body: it locks the row, reads the
/// subscription as it is now, and writes that. A late event then just repeats the current state. A failed read answers
/// an error, so Stripe delivers the event again.
/// </summary>
public sealed class StripeWebhookService : IStripeWebhookService
{
    private readonly StripeOptions                      _options;
    private readonly IModuleSubscriptionRepository      _subscriptions;
    private readonly IStripeProcessedEventRepository    _processedEvents;
    private readonly ITenantRepository                  _tenants;
    private readonly IModuleAccessService               _access;
    private readonly IStripeSubscriptionFetcher         _fetcher;
    private readonly ICacheService                      _cache;
    private readonly IUnitOfWork                        _unitOfWork;
    private readonly ILogger<StripeWebhookService>     _logger;

    /// <summary>Rows this delivery changed: their access caches are cleared only after the commit.</summary>
    private readonly List<(Guid TenantId, string ModuleKey)> _changed = [];

    public StripeWebhookService(
        IOptions<StripeOptions> options,
        IModuleSubscriptionRepository subscriptions,
        IStripeProcessedEventRepository processedEvents,
        ITenantRepository tenants,
        IModuleAccessService access,
        IStripeSubscriptionFetcher fetcher,
        ICacheService cache,
        IUnitOfWork unitOfWork,
        ILogger<StripeWebhookService> logger)
    {
        _access          = access;
        _fetcher         = fetcher;
        _cache           = cache;
        _unitOfWork      = unitOfWork;
        _options         = options.Value;
        _subscriptions   = subscriptions;
        _processedEvents = processedEvents;
        _tenants         = tenants;
        _logger          = logger;
    }

    public async Task<WebhookHandleResult> HandleAsync(
        string rawBody,
        string signature,
        CancellationToken ct)
    {
        Event stripeEvent;
        try
        {
            stripeEvent = EventUtility.ConstructEvent(
                rawBody,
                signature,
                _options.WebhookSecret,
                throwOnApiVersionMismatch: false);
        }
        catch (StripeException ex)
        {
            _logger.LogWarning("[Billing] Webhook signature invalid: {Message}", ex.Message);
            return new WebhookHandleResult(SignatureValid: false, AlreadyProcessed: false);
        }

        // The record of the event and its effects commit together. A simultaneous delivery of the same event waits for
        // this transaction on the unique index: if it commits, the other has nothing to do; if it fails, the other takes over.
        var isFirstDelivery = await _unitOfWork.ExecuteInTransactionAsync(async transactionCt =>
        {
            if (!await _processedEvents.TryRecordAsync(StripeProcessedEvent.Create(stripeEvent.Id, stripeEvent.Type), transactionCt))
                return false;

            _logger.LogInformation("[Billing] Processing event {EventId} type={Type}", stripeEvent.Id, stripeEvent.Type);
            await DispatchAsync(stripeEvent, transactionCt);
            return true;
        }, ct);

        if (!isFirstDelivery)
        {
            _logger.LogInformation("[Billing] Webhook already processed: {EventId}", stripeEvent.Id);
            return new WebhookHandleResult(SignatureValid: true, AlreadyProcessed: true);
        }

        // After the commit: a gate that refills its cache from here on reads the new state. Already committed, so a client
        // that hung up does not get to skip this.
        foreach (var (tenantId, moduleKey) in _changed)
            await InvalidateAsync(tenantId, moduleKey, CancellationToken.None);

        return new WebhookHandleResult(SignatureValid: true, AlreadyProcessed: false);
    }

    private Task DispatchAsync(Event stripeEvent, CancellationToken ct)
        => stripeEvent.Type switch
        {
            EventTypes.CheckoutSessionCompleted    => HandleCheckoutCompletedAsync((global::Stripe.Checkout.Session)stripeEvent.Data.Object!, ct),
            EventTypes.CustomerSubscriptionUpdated => SyncFromStripeAsync(((Subscription)stripeEvent.Data.Object!).Id, ct),
            EventTypes.CustomerSubscriptionDeleted => HandleSubscriptionDeletedAsync((Subscription)stripeEvent.Data.Object!, ct),
            EventTypes.InvoicePaymentFailed        => HandleInvoicePaymentFailedAsync((Invoice)stripeEvent.Data.Object!, ct),
            _                                      => Task.CompletedTask,
        };

    // ── Event handlers ────────────────────────────────────────────────────────

    private async Task HandleCheckoutCompletedAsync(global::Stripe.Checkout.Session session, CancellationToken ct)
    {
        var tenantIdStr = session.Metadata?.GetValueOrDefault("tenantId");
        var moduleKey   = session.Metadata?.GetValueOrDefault("moduleKey");

        if (!Guid.TryParse(tenantIdStr, out var tenantId) || string.IsNullOrEmpty(moduleKey))
        {
            _logger.LogWarning("[Billing] checkout.session.completed missing metadata — sessionId={Id}", session.Id);
            return;
        }

        var tenant = await _tenants.GetByIdAsync(tenantId, ct);
        if (tenant == null)
        {
            _logger.LogError("[Billing] Tenant not found — tenantId={TenantId}", tenantId);
            return;
        }

        if (!string.IsNullOrEmpty(session.CustomerId) && tenant.StripeCustomerId != session.CustomerId)
            await _tenants.SetStripeCustomerIdAsync(tenant.Id, session.CustomerId, ct);

        var stripeSubId = session.SubscriptionId;
        if (string.IsNullOrEmpty(stripeSubId)) return;

        // Read live: the event order doesn't matter, the subscription's current state does.
        var stripeSub = await _fetcher.GetAsync(stripeSubId, ct);
        var (periodStart, periodEnd) = (stripeSub.PeriodStart, stripeSub.PeriodEnd);
        var status   = BillingRules.MapStripeStatus(stripeSub.Status);

        var planType = MapPlanType(stripeSub.Interval);
        var priceId  = stripeSub.PriceId;
        var existing = await FindExistingAsync(stripeSubId, tenantId, moduleKey, ct);

        if (existing != null)
        {
            if (existing.StripeSubscriptionId != stripeSubId && existing.IsLiveInStripe)
                _logger.LogError(
                    "[Billing] Second Stripe subscription for an active module — tenant={TenantId}, module={Module}, old={Old}, new={New}. Cancel the old one in Stripe.",
                    tenantId, moduleKey, existing.StripeSubscriptionId, stripeSubId);
            // the row now follows the subscription this checkout created (before: it kept the old
            // id, so later updates/cancellations of the new one never reached it)
            existing.AttachStripe(stripeSubId, priceId, planType);
            existing.SyncFromStripe(status, periodStart, periodEnd, stripeSub.CancelAtPeriodEnd);
        }
        else
        {
            var newSub = ModuleSubscription.CreateFromStripe(
                tenantId, moduleKey, stripeSubId, priceId, planType, periodStart, periodEnd);
            newSub.SyncFromStripe(status, periodStart, periodEnd, stripeSub.CancelAtPeriodEnd);   // fail closed here too
            await _subscriptions.AddAsync(newSub, ct);
        }

        await _subscriptions.SaveChangesAsync(ct);
        _changed.Add((tenantId, moduleKey));
        _logger.LogInformation("[Billing] Subscription activated — tenant={TenantId}, module={Module}", tenantId, moduleKey);
    }

    /// <summary>
    /// The row takes the subscription's current state in Stripe. The lock comes before the read, so whoever writes last
    /// has read after the previous write: an overlapping event cannot overwrite a newer state with what it read earlier.
    /// </summary>
    private async Task<ModuleSubscription?> SyncFromStripeAsync(string stripeSubscriptionId, CancellationToken ct)
    {
        var sub = await _subscriptions.GetByStripeSubscriptionIdForUpdateAsync(stripeSubscriptionId, ct);
        if (sub == null)
        {
            _logger.LogWarning("[Billing] No local record for subscription {StripeId}", stripeSubscriptionId);
            return null;
        }

        var live = await _fetcher.GetAsync(stripeSubscriptionId, ct);
        if (!string.IsNullOrEmpty(live.PriceId) && live.PriceId != sub.StripePriceId)
            sub.ChangePlan(live.PriceId, MapPlanType(live.Interval));
        sub.SyncFromStripe(BillingRules.MapStripeStatus(live.Status), live.PeriodStart, live.PeriodEnd, live.CancelAtPeriodEnd);

        await _subscriptions.SaveChangesAsync(ct);
        _changed.Add((sub.TenantId, sub.ModuleKey));
        return sub;
    }

    private async Task HandleSubscriptionDeletedAsync(Subscription stripeSub, CancellationToken ct)
    {
        // Final in Stripe: a canceled subscription never comes back, so there is nothing newer to read.
        var sub = await _subscriptions.GetByStripeSubscriptionIdForUpdateAsync(stripeSub.Id, ct);
        if (sub == null) return;

        sub.Cancel();
        await _subscriptions.SaveChangesAsync(ct);
        _changed.Add((sub.TenantId, sub.ModuleKey));
        _logger.LogInformation("[Billing] Subscription canceled — stripeId={StripeId}", stripeSub.Id);
    }

    private async Task HandleInvoicePaymentFailedAsync(Invoice invoice, CancellationToken ct)
    {
        // In Stripe.net v52, subscription ID is at invoice.Parent.SubscriptionDetails.SubscriptionId
        var subscriptionId = invoice.Parent?.SubscriptionDetails?.SubscriptionId;
        if (string.IsNullOrEmpty(subscriptionId)) return;

        // This invoice may be old: only the subscription's current state says whether it is still unpaid.
        var sub = await SyncFromStripeAsync(subscriptionId, ct);
        if (sub != null)
            _logger.LogWarning("[Billing] Payment failed — stripeId={StripeId}, status now {Status}", subscriptionId, sub.Status);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Every cache the module gates read: the per-module flag (in-memory), and the tenant's module
    /// list and info in Redis (RequireModuleAttribute, tenant resolution → ICurrentTenant.ActiveModules).
    /// </summary>
    private async Task InvalidateAsync(Guid tenantId, string moduleKey, CancellationToken ct)
    {
        _access.InvalidateCache(tenantId, moduleKey);
        await _cache.RemoveAsync($"tenant:{tenantId}:modules", ct);
        await _cache.RemoveAsync($"tenant:{tenantId}:info", ct);
    }

    private async Task<ModuleSubscription?> FindExistingAsync(
        string stripeSubId,
        Guid tenantId,
        string moduleKey,
        CancellationToken ct)
    {
        var byStripeId = await _subscriptions.GetByStripeSubscriptionIdForUpdateAsync(stripeSubId, ct);
        if (byStripeId != null) return byStripeId;

        var allForTenant = await _subscriptions.GetByTenantIdAsync(tenantId, ct);
        return allForTenant.FirstOrDefault(s => s.ModuleKey == moduleKey.ToLowerInvariant());
    }

    private static PlanType MapPlanType(string? interval)
        => interval switch
        {
            "month" => PlanType.Monthly,
            "year"  => PlanType.Annual,
            _       => PlanType.Monthly,
        };
}
