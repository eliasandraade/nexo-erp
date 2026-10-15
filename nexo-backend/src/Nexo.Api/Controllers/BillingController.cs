using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nexo.Api.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Nexo.Application.Common.Interfaces;
using Nexo.Application.Integrations.Billing;
using Nexo.Application.Integrations.Options;

namespace Nexo.Api.Controllers;

/// <summary>
/// Stripe Billing endpoints.
///
/// Security rules enforced:
///   - No Stripe secrets exposed to frontend — all Stripe calls originate here.
///   - Subscription activation happens ONLY via validated webhook (not via checkout callback).
///   - Webhook validates Stripe-Signature before any processing.
///   - Webhook is idempotent — duplicate events are silently accepted (200 OK).
///   - No card data stored in Orken — Stripe handles payment methods.
/// </summary>
[ApiController]
[Route("api/billing")]
public class BillingController : ControllerBase
{
    private readonly IBillingProvider              _billing;
    private readonly ITenantRepository             _tenants;
    private readonly IModuleSubscriptionRepository _subscriptions;
    private readonly IStripeWebhookService         _webhookService;
    private readonly IIntegrationFeatureFlags      _flags;
    private readonly StripeOptions                 _stripeOptions;
    private readonly ICurrentTenant                _currentTenant;
    private readonly IConfiguration                _config;
    private readonly ILogger<BillingController>   _logger;

    public BillingController(
        IBillingProvider billing,
        ITenantRepository tenants,
        IModuleSubscriptionRepository subscriptions,
        IStripeWebhookService webhookService,
        IIntegrationFeatureFlags flags,
        IOptions<StripeOptions> stripeOptions,
        ICurrentTenant currentTenant,
        IConfiguration config,
        ILogger<BillingController> logger)
    {
        _billing        = billing;
        _tenants        = tenants;
        _subscriptions  = subscriptions;
        _webhookService = webhookService;
        _flags          = flags;
        _stripeOptions  = stripeOptions.Value;
        _currentTenant  = currentTenant;
        _config         = config;
        _logger         = logger;
    }

    /// <summary>
    /// Creates a Stripe Checkout Session for subscribing to a module.
    /// Returns { checkoutUrl } — frontend should redirect to this URL.
    /// Activation happens via webhook, NOT via the success_url callback.
    /// </summary>
    [HttpPost("checkout")]
    [Authorize(Roles = AppRoles.Management)]
    public async Task<IActionResult> CreateCheckout([FromBody] CreateCheckoutBody body, CancellationToken ct)
    {
        if (!_flags.StripeEnabled)
            return NotFound(new { error = "Billing não está habilitado." });

        var moduleKey     = body.ModuleKey?.Trim().ToLowerInvariant();
        var billingPeriod = body.BillingPeriod?.Trim().ToLowerInvariant();

        if (string.IsNullOrEmpty(moduleKey) || string.IsNullOrEmpty(billingPeriod))
            return BadRequest(new { error = "moduleKey e billingPeriod são obrigatórios." });

        var priceKey = $"{moduleKey}_{billingPeriod}";
        if (!_stripeOptions.PriceIds.TryGetValue(priceKey, out var priceId))
            return BadRequest(new { error = $"Plano não encontrado: {priceKey}. Configure o PriceId no servidor." });

        var tenant = await _tenants.GetByIdAsync(_currentTenant.Id, ct);
        if (tenant == null) return NotFound(new { error = "Tenant não encontrado." });

        // A second checkout for a module already paid in Stripe would create a second subscription
        // that keeps charging: changes go through the customer portal.
        var current = (await _subscriptions.GetByTenantIdAsync(tenant.Id, ct))
            .FirstOrDefault(s => s.ModuleKey == moduleKey && !string.IsNullOrEmpty(s.StripeSubscriptionId) && s.IsActive());
        if (current is not null)
            return Conflict(new { error = "Este módulo já tem uma assinatura ativa. Use \"Gerenciar assinatura\" para trocar de plano ou cancelar." });

        try
        {
            var stripeCustomerId = string.IsNullOrEmpty(tenant.StripeCustomerId)
                ? await _billing.GetOrCreateCustomerAsync(tenant.Id, tenant.Email, tenant.CompanyName, ct)
                : tenant.StripeCustomerId;

            if (tenant.StripeCustomerId != stripeCustomerId)
            {
                tenant.SetStripeCustomerId(stripeCustomerId);
                await _tenants.SaveChangesAsync(ct);
            }

            var frontendUrl = _config["App:FrontendUrl"] ?? "http://localhost:5173";
            var successUrl = BillingRules.SafeRedirect(body.SuccessUrl, $"{frontendUrl}/assinatura?sucesso=1", frontendUrl);
            var cancelUrl  = BillingRules.SafeRedirect(body.CancelUrl, $"{frontendUrl}/assinatura", frontendUrl);

            var result = await _billing.CreateCheckoutSessionAsync(new CreateCheckoutRequest(
                tenant.Id, stripeCustomerId, moduleKey, priceId, successUrl, cancelUrl), ct);

            return Ok(new { sessionId = result.SessionId, checkoutUrl = result.CheckoutUrl });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Billing] Checkout failed — tenant={TenantId}, module={Module}", tenant.Id, moduleKey);
            return StatusCode(502, new { error = "Erro ao comunicar com Stripe. Tente novamente." });
        }
    }

    /// <summary>
    /// Creates a Stripe Customer Portal session for managing existing subscriptions.
    /// Returns { portalUrl } — frontend should redirect to this URL.
    /// </summary>
    [HttpPost("portal")]
    [Authorize(Roles = AppRoles.Management)]
    public async Task<IActionResult> CreatePortal([FromBody] CreatePortalBody? body, CancellationToken ct)
    {
        if (!_flags.StripeEnabled)
            return NotFound(new { error = "Billing não está habilitado." });

        var tenant = await _tenants.GetByIdAsync(_currentTenant.Id, ct);
        if (tenant == null) return NotFound(new { error = "Tenant não encontrado." });

        if (string.IsNullOrEmpty(tenant.StripeCustomerId))
            return BadRequest(new { error = "Nenhuma assinatura Stripe encontrada para este tenant." });

        try
        {
            var frontendUrl = _config["App:FrontendUrl"] ?? "http://localhost:5173";
            var returnUrl = BillingRules.SafeRedirect(body?.ReturnUrl, $"{frontendUrl}/assinatura", frontendUrl);
            var result    = await _billing.CreatePortalSessionAsync(tenant.StripeCustomerId, returnUrl, ct);
            return Ok(new { portalUrl = result.PortalUrl });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Billing] Portal session failed — tenant={TenantId}", tenant.Id);
            return StatusCode(502, new { error = "Erro ao comunicar com Stripe. Tente novamente." });
        }
    }

    /// <summary>
    /// Returns the list of module subscriptions for the current tenant.
    /// </summary>
    [HttpGet("subscriptions")]
    [Authorize]
    public async Task<IActionResult> GetSubscriptions(CancellationToken ct)
    {
        if (!_flags.StripeEnabled)
            return NotFound(new { error = "Billing não está habilitado." });

        var subs = await _subscriptions.GetByTenantIdAsync(_currentTenant.Id, ct);
        var result = subs
            .Select(s => new SubscriptionDetail(
                s.ModuleKey,
                s.Status.ToString(),
                s.PlanType.ToString(),
                s.CurrentPeriodStart,
                s.CurrentPeriodEnd,
                s.CancelAtPeriodEnd,
                s.StripeSubscriptionId))
            .ToList();

        return Ok(result);
    }

    /// <summary>
    /// Stripe webhook endpoint — no authentication, validates Stripe-Signature instead.
    /// Idempotent: already-processed events return 200 immediately.
    /// Plan activation happens HERE, not in the checkout success callback.
    /// </summary>
    [HttpPost("webhook")]
    [AllowAnonymous]
    public async Task<IActionResult> Webhook(CancellationToken ct)
    {
        if (!_flags.StripeEnabled)
            return NotFound();

        string rawBody;
        try
        {
            using var reader = new StreamReader(Request.Body);
            rawBody = await reader.ReadToEndAsync(ct);
        }
        catch
        {
            return BadRequest(new { error = "Falha ao ler corpo da requisição." });
        }

        var signature = Request.Headers["Stripe-Signature"].ToString();
        if (string.IsNullOrEmpty(signature))
            return BadRequest(new { error = "Cabeçalho Stripe-Signature ausente." });

        try
        {
            var result = await _webhookService.HandleAsync(rawBody, signature, ct);

            if (!result.SignatureValid)
                return BadRequest(new { error = "Assinatura do webhook inválida." });

            return Ok();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Billing] Webhook processing failed unexpectedly");
            return StatusCode(500, new { error = "Falha ao processar evento." });
        }
    }

    // ── Request body types ────────────────────────────────────────────────────

    public sealed record CreateCheckoutBody(
        string? ModuleKey,
        string? BillingPeriod,
        string? SuccessUrl,
        string? CancelUrl);

    public sealed record CreatePortalBody(string? ReturnUrl);
}
