namespace Nexo.Application.Integrations.Options;

public sealed class OpenFoodFactsOptions
{
    public const string SectionKey = "Integrations:OpenFoodFacts";

    public string BaseUrl        { get; init; } = "https://world.openfoodfacts.org/api/v0";
    public int    TimeoutSeconds { get; init; } = 10;
    public string UserAgent      { get; init; } = "Orken/1.0 (contato@orken.com.br)";
}
