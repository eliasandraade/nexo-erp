namespace Nexo.Application.Integrations.Options;

public sealed class IntegrationHttpOptions
{
    public const string SectionKey = "Integrations:Http";

    public int DefaultTimeoutSeconds { get; init; } = 10;
    public int MaxResponseContentMb { get; init; } = 5;
}
