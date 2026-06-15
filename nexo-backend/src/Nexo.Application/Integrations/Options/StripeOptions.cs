namespace Nexo.Application.Integrations.Options;

public sealed class StripeOptions
{
    public const string SectionKey = "Integrations:Stripe";

    public string SecretKey     { get; init; } = string.Empty;
    public string WebhookSecret { get; init; } = string.Empty;

    /// <summary>
    /// Maps "moduleKey:billingPeriod" → Stripe Price ID.
    /// Examples: "restaurante:monthly" → "price_1ABC..."
    ///           "build:annual"        → "price_1XYZ..."
    /// Configure per environment; leave empty until prices are created in Stripe Dashboard.
    /// </summary>
    public Dictionary<string, string> PriceIds { get; init; } = new();
}
