using Microsoft.Extensions.Configuration;
using Nexo.Application.Integrations.Options;

namespace Nexo.Infrastructure.Integrations;

public sealed class IntegrationFeatureFlags : IIntegrationFeatureFlags
{
    public bool BrasilApiEnabled     { get; }
    public bool ViaCepEnabled        { get; }
    public bool OpenFoodFactsEnabled { get; }
    public bool StorageEnabled       { get; }
    public bool PdfEnabled           { get; }
    public bool WeatherEnabled       { get; }
    public bool StripeEnabled        { get; }
    public bool MercadoPagoEnabled   { get; }
    public bool WhatsAppEnabled      { get; }
    public bool FiscalEnabled        { get; }

    public IntegrationFeatureFlags(IConfiguration configuration)
    {
        var section = configuration.GetSection("Integrations:Features");

        BrasilApiEnabled     = section.GetValue("BrasilApiEnabled",     false);
        ViaCepEnabled        = section.GetValue("ViaCepEnabled",        false);
        OpenFoodFactsEnabled = section.GetValue("OpenFoodFactsEnabled", false);
        StorageEnabled       = section.GetValue("StorageEnabled",       false);
        PdfEnabled           = section.GetValue("PdfEnabled",           false);
        WeatherEnabled       = section.GetValue("WeatherEnabled",       false);
        StripeEnabled        = section.GetValue("StripeEnabled",        false);
        MercadoPagoEnabled   = section.GetValue("MercadoPagoEnabled",   false);
        WhatsAppEnabled      = section.GetValue("WhatsAppEnabled",      false);
        FiscalEnabled        = section.GetValue("FiscalEnabled",        false);
    }
}
