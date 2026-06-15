using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Nexo.Api.Controllers.Integrations;
using Nexo.Application.Integrations.Contracts;
using Nexo.Application.Integrations.DTOs;

namespace Nexo.UnitTests.Integrations;

public class LookupControllerTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static readonly CepLookupResult SampleCep = new(
        Cep: "01310100",
        Street: "Avenida Paulista",
        Neighborhood: "Bela Vista",
        City: "São Paulo",
        State: "SP",
        IbgeCode: null,
        Provider: "BrasilApi"
    );

    private static readonly CnpjLookupResult SampleCnpj = new(
        Cnpj: "00000000000191",
        CompanyName: "Banco do Brasil S.A.",
        TradeName: "Banco do Brasil",
        Status: "ATIVA",
        ActivityCode: "6422100",
        ActivityDescription: "Bancos comerciais",
        Address: null,
        Provider: "BrasilApi"
    );

    private static LookupController BuildController(
        ICepLookupProvider? cepProvider = null,
        ICnpjLookupProvider? cnpjProvider = null)
    {
        return new LookupController(
            cepProvider  ?? Substitute.For<ICepLookupProvider>(),
            cnpjProvider ?? Substitute.For<ICnpjLookupProvider>(),
            NullLogger<LookupController>.Instance);
    }

    // ── CEP endpoint ──────────────────────────────────────────────────────────

    [Fact]
    public async Task LookupCep_ReturnsBadRequest_WhenCepHasSevenDigits()
    {
        var sut = BuildController();

        var result = await sut.LookupCep("1234567", CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task LookupCep_NormalizesFormattedCep_AndReturnsOk()
    {
        // "12345-678" strips to "12345678" (8 digits) — valid
        var cepProvider = Substitute.For<ICepLookupProvider>();
        cepProvider.LookupAsync("12345678", Arg.Any<CancellationToken>())
                   .Returns(SampleCep);

        var sut = BuildController(cepProvider: cepProvider);

        var result = await sut.LookupCep("12345-678", CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
        var ok = (OkObjectResult)result;
        var body = ok.Value!;
        body.Should().BeEquivalentTo(new { found = true, data = SampleCep });
    }

    [Fact]
    public async Task LookupCep_ReturnsOkFound_WhenProviderReturnsResult()
    {
        var cepProvider = Substitute.For<ICepLookupProvider>();
        cepProvider.LookupAsync("01310100", Arg.Any<CancellationToken>())
                   .Returns(SampleCep);

        var sut = BuildController(cepProvider: cepProvider);

        var result = await sut.LookupCep("01310100", CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
        var ok = (OkObjectResult)result;
        ok.Value.Should().BeEquivalentTo(new { found = true, data = SampleCep });
    }

    [Fact]
    public async Task LookupCep_ReturnsOkNotFound_WhenProviderReturnsNull()
    {
        var cepProvider = Substitute.For<ICepLookupProvider>();
        cepProvider.LookupAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                   .Returns((CepLookupResult?)null);

        var sut = BuildController(cepProvider: cepProvider);

        var result = await sut.LookupCep("01310100", CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
        var ok = (OkObjectResult)result;
        ok.Value.Should().BeEquivalentTo(new { found = false, data = (object?)null });
    }

    [Fact]
    public async Task LookupCep_ReturnsOkUnavailable_WhenProviderThrows()
    {
        var cepProvider = Substitute.For<ICepLookupProvider>();
        cepProvider.LookupAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                   .ThrowsAsync(new HttpRequestException("Simulated failure"));

        var sut = BuildController(cepProvider: cepProvider);

        var result = await sut.LookupCep("01310100", CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
        var ok = (OkObjectResult)result;
        ok.Value.Should().BeEquivalentTo(new { found = false, data = (object?)null, unavailable = true });
    }

    // ── CNPJ endpoint ─────────────────────────────────────────────────────────

    [Fact]
    public async Task LookupCnpj_ReturnsBadRequest_WhenCnpjHasThirteenDigits()
    {
        var sut = BuildController();

        var result = await sut.LookupCnpj("1234567890123", CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task LookupCnpj_ReturnsOkFound_WhenProviderReturnsResult()
    {
        var cnpjProvider = Substitute.For<ICnpjLookupProvider>();
        cnpjProvider.LookupAsync("00000000000191", Arg.Any<CancellationToken>())
                    .Returns(SampleCnpj);

        var sut = BuildController(cnpjProvider: cnpjProvider);

        var result = await sut.LookupCnpj("00000000000191", CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
        var ok = (OkObjectResult)result;
        ok.Value.Should().BeEquivalentTo(new { found = true, data = SampleCnpj });
    }

    [Fact]
    public async Task LookupCnpj_ReturnsOkNotFound_WhenProviderReturnsNull()
    {
        var cnpjProvider = Substitute.For<ICnpjLookupProvider>();
        cnpjProvider.LookupAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                    .Returns((CnpjLookupResult?)null);

        var sut = BuildController(cnpjProvider: cnpjProvider);

        var result = await sut.LookupCnpj("00000000000191", CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
        var ok = (OkObjectResult)result;
        ok.Value.Should().BeEquivalentTo(new { found = false, data = (object?)null });
    }

    [Fact]
    public async Task LookupCnpj_ReturnsOkUnavailable_WhenProviderThrows()
    {
        var cnpjProvider = Substitute.For<ICnpjLookupProvider>();
        cnpjProvider.LookupAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                    .ThrowsAsync(new HttpRequestException("Simulated failure"));

        var sut = BuildController(cnpjProvider: cnpjProvider);

        var result = await sut.LookupCnpj("00000000000191", CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
        var ok = (OkObjectResult)result;
        ok.Value.Should().BeEquivalentTo(new { found = false, data = (object?)null, unavailable = true });
    }
}
