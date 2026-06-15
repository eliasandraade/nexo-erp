using FluentAssertions;
using Nexo.Application.Integrations.Options;

namespace Nexo.UnitTests.Integrations;

public class Phase2OptionsTests
{
    // ── BrasilApiOptions ──────────────────────────────────────────────────────

    [Fact]
    public void BrasilApiOptions_HasCorrectSectionKey()
    {
        BrasilApiOptions.SectionKey.Should().Be("Integrations:BrasilApi");
    }

    [Fact]
    public void BrasilApiOptions_DefaultBaseUrl_IsCorrect()
    {
        var options = new BrasilApiOptions();

        options.BaseUrl.Should().Be("https://brasilapi.com.br/api");
    }

    [Fact]
    public void BrasilApiOptions_DefaultTimeoutSeconds_IsEight()
    {
        var options = new BrasilApiOptions();

        options.TimeoutSeconds.Should().Be(8);
    }

    // ── ViaCepOptions ─────────────────────────────────────────────────────────

    [Fact]
    public void ViaCepOptions_HasCorrectSectionKey()
    {
        ViaCepOptions.SectionKey.Should().Be("Integrations:ViaCep");
    }

    [Fact]
    public void ViaCepOptions_DefaultBaseUrl_IsCorrect()
    {
        var options = new ViaCepOptions();

        options.BaseUrl.Should().Be("https://viacep.com.br/ws");
    }

    [Fact]
    public void ViaCepOptions_DefaultTimeoutSeconds_IsSix()
    {
        var options = new ViaCepOptions();

        options.TimeoutSeconds.Should().Be(6);
    }
}
