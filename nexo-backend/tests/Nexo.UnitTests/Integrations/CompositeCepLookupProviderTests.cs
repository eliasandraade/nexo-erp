using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Nexo.Application.Common.Interfaces;
using Nexo.Application.Integrations.DTOs;
using Nexo.Infrastructure.Integrations.BrasilApi;
using Nexo.Infrastructure.Integrations.Composite;
using Nexo.Infrastructure.Integrations.ViaCep;
using System.Net;
using System.Text;

namespace Nexo.UnitTests.Integrations;

/// <summary>
/// Tests for CompositeCepLookupProvider.
///
/// Because BrasilApiCepProvider and ViaCepProvider are sealed classes that depend on
/// HttpClient, we supply them with a fake HttpMessageHandler so the composite's
/// orchestration logic (cache, fallback, error-handling) can be exercised end-to-end.
/// </summary>
public class CompositeCepLookupProviderTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static readonly CepLookupResult SampleResult = new(
        Cep: "01310100",
        Street: "Avenida Paulista",
        Neighborhood: "Bela Vista",
        City: "São Paulo",
        State: "SP",
        IbgeCode: null,
        Provider: "BrasilApi"
    );

    /// <summary>Returns a BrasilApiCepProvider whose HTTP client always responds with the given status and body.</summary>
    private static BrasilApiCepProvider MakeBrasilApiProvider(HttpStatusCode status, string body)
    {
        var handler = new StaticHttpMessageHandler(status, body);
        var client  = new HttpClient(handler) { BaseAddress = new Uri("https://brasilapi.com.br") };
        return new BrasilApiCepProvider(client, NullLogger<BrasilApiCepProvider>.Instance);
    }

    /// <summary>Returns a BrasilApiCepProvider whose HTTP client always throws.</summary>
    private static BrasilApiCepProvider MakeBrasilApiProviderThatThrows()
    {
        var handler = new ThrowingHttpMessageHandler();
        var client  = new HttpClient(handler) { BaseAddress = new Uri("https://brasilapi.com.br") };
        return new BrasilApiCepProvider(client, NullLogger<BrasilApiCepProvider>.Instance);
    }

    /// <summary>Returns a ViaCepProvider whose HTTP client always responds with the given status and body.</summary>
    private static ViaCepProvider MakeViaCepProvider(HttpStatusCode status, string body)
    {
        var handler = new StaticHttpMessageHandler(status, body);
        var client  = new HttpClient(handler) { BaseAddress = new Uri("https://viacep.com.br") };
        return new ViaCepProvider(client, NullLogger<ViaCepProvider>.Instance);
    }

    /// <summary>Returns a ViaCepProvider whose HTTP client always throws.</summary>
    private static ViaCepProvider MakeViaCepProviderThatThrows()
    {
        var handler = new ThrowingHttpMessageHandler();
        var client  = new HttpClient(handler) { BaseAddress = new Uri("https://viacep.com.br") };
        return new ViaCepProvider(client, NullLogger<ViaCepProvider>.Instance);
    }

    private static CompositeCepLookupProvider BuildComposite(
        BrasilApiCepProvider brasilApi,
        ViaCepProvider       viaCep,
        ICacheService        cache)
        => new(brasilApi, viaCep, cache, NullLogger<CompositeCepLookupProvider>.Instance);

    // ── JSON stubs ────────────────────────────────────────────────────────────

    private const string BrasilApiFoundJson = """
        {
          "cep": "01310100",
          "state": "SP",
          "city": "São Paulo",
          "neighborhood": "Bela Vista",
          "street": "Avenida Paulista"
        }
        """;

    private const string ViaCepFoundJson = """
        {
          "cep": "01310-100",
          "logradouro": "Avenida Paulista",
          "bairro": "Bela Vista",
          "localidade": "São Paulo",
          "uf": "SP",
          "ibge": "3550308"
        }
        """;

    // ── Tests: cache hit ──────────────────────────────────────────────────────

    [Fact]
    public async Task LookupAsync_ReturnsCachedResult_WithoutCallingProviders()
    {
        var cache = Substitute.For<ICacheService>();
        cache.GetAsync<CepLookupResult>(Arg.Any<string>(), Arg.Any<CancellationToken>())
             .Returns(SampleResult);

        // Providers that would throw if actually called
        var brasilApi = MakeBrasilApiProviderThatThrows();
        var viaCep    = MakeViaCepProviderThatThrows();

        var sut = BuildComposite(brasilApi, viaCep, cache);

        var result = await sut.LookupAsync("01310100", CancellationToken.None);

        result.Should().BeEquivalentTo(SampleResult);
        // SetAsync must NOT be called when returning from cache
        await cache.DidNotReceive().SetAsync(
            Arg.Any<string>(), Arg.Any<CepLookupResult>(),
            Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>());
    }

    // ── Tests: BrasilAPI primary ──────────────────────────────────────────────

    [Fact]
    public async Task LookupAsync_ReturnsBrasilApiResult_WhenPrimarySucceeds()
    {
        var cache = Substitute.For<ICacheService>();
        cache.GetAsync<CepLookupResult>(Arg.Any<string>(), Arg.Any<CancellationToken>())
             .Returns((CepLookupResult?)null);

        var brasilApi = MakeBrasilApiProvider(HttpStatusCode.OK, BrasilApiFoundJson);
        var viaCep    = MakeViaCepProviderThatThrows(); // must not be called

        var sut = BuildComposite(brasilApi, viaCep, cache);

        var result = await sut.LookupAsync("01310100", CancellationToken.None);

        result.Should().NotBeNull();
        result!.Provider.Should().Be("BrasilApi");
        result.City.Should().Be("São Paulo");

        // Result must be cached
        await cache.Received(1).SetAsync(
            Arg.Is<string>(k => k.Contains("01310100")),
            Arg.Any<CepLookupResult>(),
            Arg.Any<TimeSpan?>(),
            Arg.Any<CancellationToken>());
    }

    // ── Tests: fallback to ViaCEP ─────────────────────────────────────────────

    [Fact]
    public async Task LookupAsync_FallsBackToViaCep_WhenBrasilApiReturnsNull()
    {
        var cache = Substitute.For<ICacheService>();
        cache.GetAsync<CepLookupResult>(Arg.Any<string>(), Arg.Any<CancellationToken>())
             .Returns((CepLookupResult?)null);

        // BrasilAPI returns 404 → provider returns null
        var brasilApi = MakeBrasilApiProvider(HttpStatusCode.NotFound, "");
        var viaCep    = MakeViaCepProvider(HttpStatusCode.OK, ViaCepFoundJson);

        var sut = BuildComposite(brasilApi, viaCep, cache);

        var result = await sut.LookupAsync("01310100", CancellationToken.None);

        result.Should().NotBeNull();
        result!.Provider.Should().Be("ViaCep");
    }

    // ── Tests: both providers return null ─────────────────────────────────────

    [Fact]
    public async Task LookupAsync_ReturnsNull_WhenBothProvidersReturnNull()
    {
        var cache = Substitute.For<ICacheService>();
        cache.GetAsync<CepLookupResult>(Arg.Any<string>(), Arg.Any<CancellationToken>())
             .Returns((CepLookupResult?)null);

        var brasilApi = MakeBrasilApiProvider(HttpStatusCode.NotFound, "");
        var viaCep    = MakeViaCepProvider(HttpStatusCode.OK, """{"erro":true}""");

        var sut = BuildComposite(brasilApi, viaCep, cache);

        var result = await sut.LookupAsync("00000000", CancellationToken.None);

        result.Should().BeNull();
    }

    // ── Tests: BrasilAPI throws, ViaCEP succeeds ──────────────────────────────

    [Fact]
    public async Task LookupAsync_FallsBackToViaCep_WhenBrasilApiThrows()
    {
        var cache = Substitute.For<ICacheService>();
        cache.GetAsync<CepLookupResult>(Arg.Any<string>(), Arg.Any<CancellationToken>())
             .Returns((CepLookupResult?)null);

        var brasilApi = MakeBrasilApiProviderThatThrows();
        var viaCep    = MakeViaCepProvider(HttpStatusCode.OK, ViaCepFoundJson);

        var sut = BuildComposite(brasilApi, viaCep, cache);

        var result = await sut.LookupAsync("01310100", CancellationToken.None);

        result.Should().NotBeNull();
        result!.Provider.Should().Be("ViaCep");
    }

    // ── Tests: both providers fail ────────────────────────────────────────────

    [Fact]
    public async Task LookupAsync_ReturnsNull_WhenBothProvidersFail()
    {
        var cache = Substitute.For<ICacheService>();
        cache.GetAsync<CepLookupResult>(Arg.Any<string>(), Arg.Any<CancellationToken>())
             .Returns((CepLookupResult?)null);

        var brasilApi = MakeBrasilApiProviderThatThrows();
        var viaCep    = MakeViaCepProviderThatThrows();

        var sut = BuildComposite(brasilApi, viaCep, cache);

        // Must not throw
        var act = async () => await sut.LookupAsync("01310100", CancellationToken.None);
        await act.Should().NotThrowAsync();

        var result = await sut.LookupAsync("01310100", CancellationToken.None);
        result.Should().BeNull();
    }

    // ── Fake HTTP handlers ────────────────────────────────────────────────────

    private sealed class StaticHttpMessageHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string         _body;

        public StaticHttpMessageHandler(HttpStatusCode status, string body)
        {
            _status = status;
            _body   = body;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(_status)
            {
                Content = new StringContent(_body, Encoding.UTF8, "application/json")
            };
            return Task.FromResult(response);
        }
    }

    private sealed class ThrowingHttpMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => throw new HttpRequestException("Simulated network failure");
    }
}
