namespace Nexo.Application.Integrations.Options;

public sealed class ViaCepOptions
{
    public const string SectionKey = "Integrations:ViaCep";
    public string BaseUrl { get; init; } = "https://viacep.com.br/ws";
    public int TimeoutSeconds { get; init; } = 6;
}
