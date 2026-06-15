namespace Nexo.Application.Integrations.Options;

public interface IIntegrationFeatureFlags
{
    bool BrasilApiEnabled { get; }
    bool ViaCepEnabled { get; }
    bool OpenFoodFactsEnabled { get; }
    bool StorageEnabled { get; }
    bool PdfEnabled { get; }
    bool WeatherEnabled { get; }
    bool StripeEnabled { get; }
    bool MercadoPagoEnabled { get; }
    bool WhatsAppEnabled { get; }
    bool FiscalEnabled { get; }
}
