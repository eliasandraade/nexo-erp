using Microsoft.Extensions.Configuration;
using Nexo.Application.Integrations.Options;

namespace Nexo.Infrastructure.Integrations;

/// <summary>
/// Kill switches of external integrations, per deployment (Integrations:Features:* — appsettings or
/// environment). Each one is read by the code it gates; flags nothing read were removed (PR 72).
/// </summary>
public sealed class IntegrationFeatureFlags : IIntegrationFeatureFlags
{
    public bool BrasilApiEnabled     { get; }
    public bool ViaCepEnabled        { get; }
    public bool OpenFoodFactsEnabled { get; }
    public bool StorageEnabled       { get; }
    public bool WeatherEnabled       { get; }
    public bool StripeEnabled        { get; }
    public bool FiscalEnabled        { get; }

    public IntegrationFeatureFlags(IConfiguration configuration)
    {
        var section = configuration.GetSection("Integrations:Features");

        // free public lookups: on unless explicitly switched off (a missing key must not silently stop them)
        BrasilApiEnabled     = section.GetValue("BrasilApiEnabled",     true);
        ViaCepEnabled        = section.GetValue("ViaCepEnabled",        true);
        OpenFoodFactsEnabled = section.GetValue("OpenFoodFactsEnabled", false);
        StorageEnabled       = section.GetValue("StorageEnabled",       false);
        WeatherEnabled       = section.GetValue("WeatherEnabled",       false);
        StripeEnabled        = section.GetValue("StripeEnabled",        false);
        FiscalEnabled        = section.GetValue("FiscalEnabled",        false);
    }
}
