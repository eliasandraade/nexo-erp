namespace Nexo.Application.Integrations.Options;

public interface IIntegrationFeatureFlags
{
    bool BrasilApiEnabled { get; }
    bool ViaCepEnabled { get; }
    bool OpenFoodFactsEnabled { get; }
    bool StorageEnabled { get; }
    bool WeatherEnabled { get; }
    bool StripeEnabled { get; }
    bool FiscalEnabled { get; }
}
