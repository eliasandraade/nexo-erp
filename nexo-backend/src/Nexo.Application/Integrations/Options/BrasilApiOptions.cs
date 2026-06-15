namespace Nexo.Application.Integrations.Options;

public sealed class BrasilApiOptions
{
    public const string SectionKey = "Integrations:BrasilApi";
    public string BaseUrl { get; init; } = "https://brasilapi.com.br/api";
    public int TimeoutSeconds { get; init; } = 8;
}
