using FluentAssertions;
using Nexo.Application.Integrations.Options;

namespace Nexo.UnitTests.Integrations;

public class IntegrationOptionsDefaultsTests
{
    [Fact]
    public void IntegrationHttpOptions_HasCorrectSectionKey()
    {
        IntegrationHttpOptions.SectionKey.Should().Be("Integrations:Http");
    }

    [Fact]
    public void IntegrationHttpOptions_HasCorrectDefaults()
    {
        var options = new IntegrationHttpOptions();

        options.DefaultTimeoutSeconds.Should().Be(10);
        options.MaxResponseContentMb.Should().Be(5);
    }

    [Fact]
    public void IntegrationResilienceOptions_HasCorrectSectionKey()
    {
        IntegrationResilienceOptions.SectionKey.Should().Be("Integrations:Resilience");
    }

    [Fact]
    public void IntegrationResilienceOptions_HasCorrectDefaults()
    {
        var options = new IntegrationResilienceOptions();

        options.MaxRetryAttempts.Should().Be(3);
        options.RetryBaseDelaySeconds.Should().Be(1.0);
        options.CircuitBreakerFailureThreshold.Should().Be(5);
        options.CircuitBreakerSamplingDurationSeconds.Should().Be(30);
        options.CircuitBreakerBreakDurationSeconds.Should().Be(60);
    }

    [Fact]
    public void IntegrationHttpOptions_PropertiesCanBeOverriddenWithInit()
    {
        var options = new IntegrationHttpOptions { DefaultTimeoutSeconds = 30 };

        options.DefaultTimeoutSeconds.Should().Be(30);
        options.MaxResponseContentMb.Should().Be(5);
    }
}
