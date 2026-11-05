using Nexo.Domain.Enums;

namespace Nexo.Application.Integrations.Billing;

/// <summary>Pure billing rules shared by the webhook and the checkout endpoints (unit-tested).</summary>
public static class BillingRules
{
    /// <summary>The switch for new checkouts (environment variable <c>Billing__NewCheckoutsEnabled</c>).</summary>
    public const string NewCheckoutsSetting = "Billing:NewCheckoutsEnabled";

    /// <summary>
    /// Whether a new checkout may start. Fails closed, unlike Billing:EnforceTenantAccess: missing, empty or mistyped keeps
    /// new checkouts blocked. Only the literal true opens them, which the configuration delivers as "true" from an
    /// environment variable and as "True" from a JSON boolean.
    /// </summary>
    public static bool NewCheckoutsEnabled(string? configured) => configured is "true" or "True";

    /// <summary>
    /// Stripe subscription status → ours. Anything that is not known to be paid fails closed:
    /// "incomplete" (first payment pending) and "paused" give no access; an unknown status too.
    /// </summary>
    public static SubscriptionStatus MapStripeStatus(string? stripeStatus) => stripeStatus switch
    {
        "active"             => SubscriptionStatus.Active,
        "trialing"           => SubscriptionStatus.Trialing,
        "past_due"           => SubscriptionStatus.PastDue,
        "canceled"           => SubscriptionStatus.Canceled,
        "incomplete_expired" => SubscriptionStatus.Canceled,
        _                    => SubscriptionStatus.Suspended,   // unpaid, incomplete, paused, unknown
    };

    /// <summary>
    /// A redirect the client asked for, only if it stays on the app's own origin — otherwise the
    /// default. Stops the checkout/portal from being used to bounce users to another site.
    /// </summary>
    public static string SafeRedirect(string? requested, string fallback, string frontendUrl)
    {
        if (string.IsNullOrWhiteSpace(requested)) return fallback;
        if (!Uri.TryCreate(requested, UriKind.Absolute, out var target)) return fallback;
        if (!Uri.TryCreate(frontendUrl, UriKind.Absolute, out var app)) return fallback;
        return target.Scheme == app.Scheme
               && string.Equals(target.Host, app.Host, StringComparison.OrdinalIgnoreCase)
               && target.Port == app.Port
            ? target.ToString()
            : fallback;
    }
}
