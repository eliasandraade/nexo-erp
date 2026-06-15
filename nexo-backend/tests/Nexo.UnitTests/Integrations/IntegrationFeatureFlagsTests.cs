using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Nexo.Infrastructure.Integrations;

namespace Nexo.UnitTests.Integrations;

public class IntegrationFeatureFlagsTests
{
    [Fact]
    public void AllFlags_DefaultToFalse_WhenConfigSectionIsEmpty()
    {
        var configuration = new ConfigurationBuilder().Build();

        var flags = new IntegrationFeatureFlags(configuration);

        flags.BrasilApiEnabled.Should().BeFalse();
        flags.ViaCepEnabled.Should().BeFalse();
        flags.OpenFoodFactsEnabled.Should().BeFalse();
        flags.StorageEnabled.Should().BeFalse();
        flags.PdfEnabled.Should().BeFalse();
        flags.WeatherEnabled.Should().BeFalse();
        flags.StripeEnabled.Should().BeFalse();
        flags.MercadoPagoEnabled.Should().BeFalse();
        flags.WhatsAppEnabled.Should().BeFalse();
        flags.FiscalEnabled.Should().BeFalse();
    }

    [Fact]
    public void BrasilApiEnabled_ReadsTrue_WhenConfigKeyIsSetToTrue()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Integrations:Features:BrasilApiEnabled"] = "true"
            })
            .Build();

        var flags = new IntegrationFeatureFlags(configuration);

        flags.BrasilApiEnabled.Should().BeTrue();
        flags.ViaCepEnabled.Should().BeFalse();
        flags.OpenFoodFactsEnabled.Should().BeFalse();
        flags.StorageEnabled.Should().BeFalse();
        flags.PdfEnabled.Should().BeFalse();
        flags.WeatherEnabled.Should().BeFalse();
        flags.StripeEnabled.Should().BeFalse();
        flags.MercadoPagoEnabled.Should().BeFalse();
        flags.WhatsAppEnabled.Should().BeFalse();
        flags.FiscalEnabled.Should().BeFalse();
    }

    [Fact]
    public void AllFlags_ReadTrue_WhenAllConfigKeysSetToTrue()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Integrations:Features:BrasilApiEnabled"]     = "true",
                ["Integrations:Features:ViaCepEnabled"]        = "true",
                ["Integrations:Features:OpenFoodFactsEnabled"] = "true",
                ["Integrations:Features:StorageEnabled"]       = "true",
                ["Integrations:Features:PdfEnabled"]           = "true",
                ["Integrations:Features:WeatherEnabled"]       = "true",
                ["Integrations:Features:StripeEnabled"]        = "true",
                ["Integrations:Features:MercadoPagoEnabled"]   = "true",
                ["Integrations:Features:WhatsAppEnabled"]      = "true",
                ["Integrations:Features:FiscalEnabled"]        = "true"
            })
            .Build();

        var flags = new IntegrationFeatureFlags(configuration);

        flags.BrasilApiEnabled.Should().BeTrue();
        flags.ViaCepEnabled.Should().BeTrue();
        flags.OpenFoodFactsEnabled.Should().BeTrue();
        flags.StorageEnabled.Should().BeTrue();
        flags.PdfEnabled.Should().BeTrue();
        flags.WeatherEnabled.Should().BeTrue();
        flags.StripeEnabled.Should().BeTrue();
        flags.MercadoPagoEnabled.Should().BeTrue();
        flags.WhatsAppEnabled.Should().BeTrue();
        flags.FiscalEnabled.Should().BeTrue();
    }

    [Fact]
    public void UnsetFlag_StaysFalse_WhenOtherFlagsAreSet()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Integrations:Features:BrasilApiEnabled"] = "true"
            })
            .Build();

        var flags = new IntegrationFeatureFlags(configuration);

        flags.FiscalEnabled.Should().BeFalse();
    }
}
