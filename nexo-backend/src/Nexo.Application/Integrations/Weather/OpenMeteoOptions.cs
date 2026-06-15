namespace Nexo.Application.Integrations.Weather;

public sealed class OpenMeteoOptions
{
    public const string SectionKey = "Integrations:OpenMeteo";

    public string ForecastBaseUrl { get; init; } = "https://api.open-meteo.com/v1";
    public string ArchiveBaseUrl  { get; init; } = "https://archive-api.open-meteo.com/v1";
    public int    TimeoutSeconds  { get; init; } = 10;
    public string Timezone        { get; init; } = "America/Sao_Paulo";
}
