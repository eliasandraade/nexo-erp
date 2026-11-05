using Microsoft.Extensions.Configuration;
using Nexo.Application.Integrations.Options;

namespace Nexo.Infrastructure.Integrations.Stripe;

/// <summary>
/// Startup guard for the Stripe configuration. Two mistakes must stop the host before it serves anything:
/// Stripe switched on without its secrets (an empty webhook secret is a key anyone can reproduce, so forged events
/// would be accepted), and a live key outside production (a staging or local host would charge real customers).
/// Production with a live key, and any environment with Stripe off and no live key, start as before.
/// Messages name the setting and never include its value.
/// </summary>
public static class StripeStartupPolicy
{
    private const string FeatureFlagKey = "Integrations:Features:StripeEnabled";
    private static readonly string[] LiveKeyPrefixes = ["sk_live_", "rk_live_"];

    /// <summary>Returns the reason the configuration is unsafe, or null when it is acceptable.</summary>
    public static string? Problem(bool stripeEnabled, string? secretKey, string? webhookSecret, string environmentName)
    {
        if (IsLiveKey(secretKey) && !IsProduction(environmentName))
            return "Integrations:Stripe:SecretKey is a live Stripe key and this environment is not Production. "
                 + "Use a test mode key (env var Integrations__Stripe__SecretKey).";

        if (!stripeEnabled)
            return null;

        if (string.IsNullOrWhiteSpace(webhookSecret))
            return "Integrations:Features:StripeEnabled is on and Integrations:Stripe:WebhookSecret is empty. "
                 + "Set it (env var Integrations__Stripe__WebhookSecret) or switch Stripe off.";

        if (string.IsNullOrWhiteSpace(secretKey))
            return "Integrations:Features:StripeEnabled is on and Integrations:Stripe:SecretKey is empty. "
                 + "Set it (env var Integrations__Stripe__SecretKey) or switch Stripe off.";

        return null;
    }

    /// <summary>Throws (failing startup) when the configuration is unsafe for the environment.</summary>
    public static void EnsureSafe(IConfiguration configuration, string environmentName)
    {
        var stripe = configuration.GetSection(StripeOptions.SectionKey);
        var problem = Problem(
            configuration.GetValue(FeatureFlagKey, false),
            stripe[nameof(StripeOptions.SecretKey)],
            stripe[nameof(StripeOptions.WebhookSecret)],
            environmentName);

        if (problem is not null)
            throw new InvalidOperationException($"Refusing to start: {problem}");
    }

    private static bool IsLiveKey(string? secretKey)
    {
        var key = secretKey?.Trim();
        return !string.IsNullOrEmpty(key)
            && LiveKeyPrefixes.Any(prefix => key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The same comparison as IHostEnvironment.IsProduction.</summary>
    private static bool IsProduction(string environmentName)
        => string.Equals(environmentName, "Production", StringComparison.OrdinalIgnoreCase);
}
