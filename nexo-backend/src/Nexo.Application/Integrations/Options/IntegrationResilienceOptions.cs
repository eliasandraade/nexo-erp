namespace Nexo.Application.Integrations.Options;

public sealed class IntegrationResilienceOptions
{
    public const string SectionKey = "Integrations:Resilience";

    public int MaxRetryAttempts { get; init; } = 3;
    public double RetryBaseDelaySeconds { get; init; } = 1.0;
    public int CircuitBreakerFailureThreshold { get; init; } = 5;
    public int CircuitBreakerSamplingDurationSeconds { get; init; } = 30;
    public int CircuitBreakerBreakDurationSeconds { get; init; } = 60;
}
