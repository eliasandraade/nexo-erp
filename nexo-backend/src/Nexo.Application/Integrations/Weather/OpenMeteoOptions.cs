namespace Nexo.Application.Integrations.Weather;

public sealed class OpenMeteoOptions
{
    public const string SectionKey = "Integrations:OpenMeteo";

    // Free tier — non-commercial use only.
    // For Orken (commercial SaaS), activate UseCustomerApi and set ApiKey when a paid plan is contracted.
    public string ForecastBaseUrl    { get; init; } = "https://api.open-meteo.com/v1";
    public string ArchiveBaseUrl     { get; init; } = "https://archive-api.open-meteo.com/v1";
    public int    TimeoutSeconds     { get; init; } = 10;
    public string Timezone           { get; init; } = "America/Sao_Paulo";

    // Commercial plan fields (Open-Meteo Customer API)
    // Set UseCustomerApi=true + ApiKey when a commercial plan is active.
    public bool   UseCustomerApi     { get; init; } = false;
    public string ApiKey             { get; init; } = string.Empty;
    public string CustomerBaseUrl    { get; init; } = "https://customer-api.open-meteo.com/v1";
}
