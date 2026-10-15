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
/// Uses Stripe.net v52 API — period dates are on SubscriptionItem, not on Subscription directly.
/// </summary>
public sealed class StripeWebhookService : IStripeWebhookService
{
    private readonly StripeOptions                      _options;
    private readonly IModuleSubscriptionRepository      _subscriptions;
    private readonly IStripeProcessedEventRepository    _processedEvents;
    private readonly ITenantRepository                  _tenants;
    private readonly IModuleAccessService               _access;
    private readonly ILogger<StripeWebhookService>     _logger;

    public StripeWebhookService(
        IOptions<StripeOptions> options,
        IModuleSubscriptionRepository subscriptions,
        IStripeProcessedEventRepository processedEvents,
        ITenantRepository tenants,
        IModuleAccessService access,
        ILogger<StripeWebhookService> logger)
    {
        _access          = access;
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

        if (await _processedEvents.ExistsAsync(stripeEvent.Id, ct))
        {
            _logger.LogInformation("[Billing] Webhook already processed: {EventId}", stripeEvent.Id);
            return new WebhookHandleResult(SignatureValid: true, AlreadyProcessed: true);
        }

        _logger.LogInformation("[Billing] Processing event {EventId} type={Type}", stripeEvent.Id, stripeEvent.Type);

        await DispatchAsync(stripeEvent, ct);

        await _processedEvents.AddAsync(
            StripeProcessedEvent.Create(stripeEvent.Id, stripeEvent.Type), ct);
        await _processedEvents.SaveChangesAsync(ct);

        return new WebhookHandleResult(SignatureValid: true, AlreadyProcessed: false);
    }

    private Task DispatchAsync(Event stripeEvent, CancellationToken ct)
        => stripeEvent.Type switch
        {
            EventTypes.CheckoutSessionCompleted    => HandleCheckoutCompletedAsync((global::Stripe.Checkout.Session)stripeEvent.Data.Object!, ct),
            EventTypes.CustomerSubscriptionUpdated => HandleSubscriptionUpdatedAsync((Subscription)stripeEvent.Data.Object!, ct),
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
        {
            tenant.SetStripeCustomerId(session.CustomerId);
            await _tenants.SaveChangesAsync(ct);
        }

        var stripeSubId = session.SubscriptionId;
        if (string.IsNullOrEmpty(stripeSubId)) return;

        // Fetch subscription to get period dates (v52: period is on SubscriptionItem, not Subscription root)
        var subService = new SubscriptionService();
        var stripeSub  = await subService.GetAsync(stripeSubId, cancellationToken: ct);
        var (periodStart, periodEnd) = GetPeriodFromSubscription(stripeSub);

        var planType = MapPlanType(stripeSub.Items?.Data?.FirstOrDefault()?.Price?.Recurring?.Interval);
        var priceId  = stripeSub.Items?.Data?.FirstOrDefault()?.Price?.Id ?? string.Empty;
        var existing = await FindExistingAsync(stripeSubId, tenantId, moduleKey, ct);

        if (existing != null)
        {
            if (!string.IsNullOrEmpty(existing.StripeSubscriptionId) && existing.StripeSubscriptionId != stripeSubId
                && existing.IsActive())
                _logger.LogError(
                    "[Billing] Second Stripe subscription for an active module — tenant={TenantId}, module={Module}, old={Old}, new={New}. Cancel the old one in Stripe.",
                    tenantId, moduleKey, existing.StripeSubscriptionId, stripeSubId);
            // the row now follows the subscription this checkout created (before: it kept the old
            // id, so later updates/cancellations of the new one never reached it)
            existing.AttachStripe(stripeSubId, priceId, planType);
            existing.SyncFromStripe(BillingRules.MapStripeStatus(stripeSub.Status), periodStart, periodEnd, stripeSub.CancelAtPeriodEnd);
        }
        else
        {
            var newSub = ModuleSubscription.CreateFromStripe(
                tenantId, moduleKey, stripeSubId, priceId, planType, periodStart, periodEnd);
            await _subscriptions.AddAsync(newSub, ct);
        }

        await _subscriptions.SaveChangesAsync(ct);
        _access.InvalidateCache(tenantId, moduleKey);
        _logger.LogInformation("[Billing] Subscription activated — tenant={TenantId}, module={Module}", tenantId, moduleKey);
    }

    private async Task HandleSubscriptionUpdatedAsync(Subscription stripeSub, CancellationToken ct)
    {
        var sub = await _subscriptions.GetByStripeSubscriptionIdAsync(stripeSub.Id, ct);
        if (sub == null)
        {
            _logger.LogWarning("[Billing] subscription.updated — no local record for {StripeId}", stripeSub.Id);
            return;
        }

        var (periodStart, periodEnd) = GetPeriodFromSubscription(stripeSub);
        sub.SyncFromStripe(BillingRules.MapStripeStatus(stripeSub.Status), periodStart, periodEnd, stripeSub.CancelAtPeriodEnd);
        await _subscriptions.SaveChangesAsync(ct);
        _access.InvalidateCache(sub.TenantId, sub.ModuleKey);
    }

    private async Task HandleSubscriptionDeletedAsync(Subscription stripeSub, CancellationToken ct)
    {
        var sub = await _subscriptions.GetByStripeSubscriptionIdAsync(stripeSub.Id, ct);
        if (sub == null) return;

        sub.Cancel();
        await _subscriptions.SaveChangesAsync(ct);
        _access.InvalidateCache(sub.TenantId, sub.ModuleKey);
        _logger.LogInformation("[Billing] Subscription canceled — stripeId={StripeId}", stripeSub.Id);
    }

    private async Task HandleInvoicePaymentFailedAsync(Invoice invoice, CancellationToken ct)
    {
        // In Stripe.net v52, subscription ID is at invoice.Parent.SubscriptionDetails.SubscriptionId
        var subscriptionId = invoice.Parent?.SubscriptionDetails?.SubscriptionId;
        if (string.IsNullOrEmpty(subscriptionId)) return;

        var sub = await _subscriptions.GetByStripeSubscriptionIdAsync(subscriptionId, ct);
        if (sub == null) return;

        sub.MarkPastDue();
        await _subscriptions.SaveChangesAsync(ct);
        _access.InvalidateCache(sub.TenantId, sub.ModuleKey);
        _logger.LogWarning("[Billing] Payment failed — marked past_due for stripeId={StripeId}", subscriptionId);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// In Stripe.net v52, period dates live on SubscriptionItem, not on the Subscription root.
    /// Falls back to DateTime.UtcNow if no item is found (defensive).
    /// </summary>
    private static (DateTime PeriodStart, DateTime? PeriodEnd) GetPeriodFromSubscription(Subscription stripeSub)
    {
        var item = stripeSub.Items?.Data?.FirstOrDefault();
        var start = item?.CurrentPeriodStart ?? DateTime.UtcNow;
        var end   = item?.CurrentPeriodEnd;
        return (start, end);
    }

    private async Task<ModuleSubscription?> FindExistingAsync(
        string stripeSubId,
        Guid tenantId,
        string moduleKey,
        CancellationToken ct)
    {
        var byStripeId = await _subscriptions.GetByStripeSubscriptionIdAsync(stripeSubId, ct);
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
