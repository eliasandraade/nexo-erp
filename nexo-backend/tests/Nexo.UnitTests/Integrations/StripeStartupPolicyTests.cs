using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Nexo.Infrastructure.Integrations.Stripe;
using Xunit;

namespace Nexo.UnitTests.Integrations;

public class StripeStartupPolicyTests
{
    // Built at runtime and obviously fake: never a literal that looks like a real key.
    private static readonly string LiveSecretKey = "sk_" + "live_" + "valor-de-teste-que-nao-pode-vazar";
    private static readonly string LiveRestrictedKey = "rk_" + "live_" + "valor-de-teste-que-nao-pode-vazar";
    private const string TestModeKey = "sk_test_valor-de-teste";
    private const string WebhookSecret = "whsec_valor-de-teste-que-nao-pode-vazar";

    public static IEnumerable<object[]> NotProduction =>
        new[] { "Development", "Testing", "Staging", "staging", "Homologacao", "" }.Select(e => new object[] { e });

    public static IEnumerable<object[]> ProductionSpellings =>
        new[] { "Production", "production", "PRODUCTION" }.Select(e => new object[] { e });

    [Theory]
    [MemberData(nameof(NotProduction))]
    public void A_live_key_outside_production_is_refused_with_stripe_on_or_off(string environment)
    {
        foreach (var key in new[] { LiveSecretKey, LiveRestrictedKey, "  " + LiveSecretKey, LiveSecretKey.ToUpperInvariant() })
        foreach (var enabled in new[] { true, false })
            StripeStartupPolicy.Problem(enabled, key, WebhookSecret, environment)
                .Should().Contain("live Stripe key").And.Contain("not Production");
    }

    [Theory]
    [MemberData(nameof(ProductionSpellings))]
    public void Production_with_a_live_key_keeps_starting(string environment)
    {
        StripeStartupPolicy.Problem(true, LiveSecretKey, WebhookSecret, environment).Should().BeNull();
        StripeStartupPolicy.Problem(true, LiveRestrictedKey, WebhookSecret, environment).Should().BeNull();
        StripeStartupPolicy.Problem(false, LiveSecretKey, null, environment).Should().BeNull();
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("Development")]
    [InlineData("Testing")]
    public void A_test_mode_key_starts_in_any_environment(string environment)
        => StripeStartupPolicy.Problem(true, TestModeKey, WebhookSecret, environment).Should().BeNull();

    [Theory]
    [InlineData("Production", null)]
    [InlineData("Production", "")]
    [InlineData("Staging", null)]
    [InlineData("Staging", "   ")]
    [InlineData("Testing", "")]
    public void Stripe_on_without_the_webhook_secret_is_refused(string environment, string? webhookSecret)
        => StripeStartupPolicy.Problem(true, TestModeKey, webhookSecret, environment)
            .Should().Contain("WebhookSecret");

    [Theory]
    [InlineData("Production", null)]
    [InlineData("Production", "")]
    [InlineData("Staging", null)]
    [InlineData("Staging", "   ")]
    [InlineData("Testing", "")]
    public void Stripe_on_without_the_secret_key_is_refused(string environment, string? secretKey)
        => StripeStartupPolicy.Problem(true, secretKey, WebhookSecret, environment)
            .Should().Contain("SecretKey");

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("Development")]
    [InlineData("Testing")]
    public void Stripe_off_with_no_keys_starts_as_before(string environment)
    {
        StripeStartupPolicy.Problem(false, null, null, environment).Should().BeNull();
        StripeStartupPolicy.Problem(false, "", "", environment).Should().BeNull();
    }

    [Fact]
    public void Failing_names_the_setting_and_never_prints_a_value()
    {
        var live = Configuration(enabled: "true", secretKey: LiveSecretKey, webhookSecret: WebhookSecret);
        var noWebhook = Configuration(enabled: "true", secretKey: TestModeKey, webhookSecret: "");

        foreach (var (configuration, setting) in new[] { (live, "SecretKey"), (noWebhook, "WebhookSecret") })
        {
            var act = () => StripeStartupPolicy.EnsureSafe(configuration, "Staging");

            var message = act.Should().Throw<InvalidOperationException>().Which.Message;
            message.Should().StartWith("Refusing to start").And.Contain(setting);
            message.Should().NotContain("valor-de-teste", "the message goes to the logs");
        }
    }

    [Fact]
    public void The_guard_reads_the_settings_the_application_reads()
    {
        var off = Configuration(enabled: null, secretKey: null, webhookSecret: null);
        ((Action)(() => StripeStartupPolicy.EnsureSafe(off, "Production"))).Should().NotThrow("off is the default");

        var on = Configuration(enabled: "true", secretKey: null, webhookSecret: null);
        ((Action)(() => StripeStartupPolicy.EnsureSafe(on, "Production"))).Should().Throw<InvalidOperationException>();

        var liveInProduction = Configuration(enabled: "true", secretKey: LiveSecretKey, webhookSecret: WebhookSecret);
        ((Action)(() => StripeStartupPolicy.EnsureSafe(liveInProduction, "Production"))).Should().NotThrow();
    }

    private static IConfiguration Configuration(string? enabled, string? secretKey, string? webhookSecret)
        => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Integrations:Features:StripeEnabled"] = enabled,
            ["Integrations:Stripe:SecretKey"] = secretKey,
            ["Integrations:Stripe:WebhookSecret"] = webhookSecret,
        }).Build();
}
