using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Nexo.Application.Common.Interfaces;
using Nexo.Application.Integrations.Options;
using Nexo.Infrastructure.Integrations.BrasilApi;
using Nexo.Infrastructure.Integrations.OpenFoodFacts;
using Nexo.Infrastructure.Integrations.ViaCep;

namespace Nexo.UnitTests.Integrations;

/// <summary>
/// O pedido vai ao endereço público de cada API, com a URL base configurada por padrão: um caminho
/// relativo começando em "/" descarta o caminho da base, e os serviços respondem 404 sem o "/api" ou o "/ws".
///
/// O valor consultado entra na URL como um único segmento de caminho, em qualquer provedor.
/// Os controllers já restringem CEP, CNPJ e código de barras a dígitos; o escape é a segunda
/// barreira, para o caso de um novo chamador não validar da mesma forma.
/// </summary>
public class LookupProviderUrlEscapingTests
{
    // Barra, ponto, interrogação, sustenta e porcentagem: sem escape, mudariam o destino do pedido.
    private const string Hostile = "123/../admin?x=1#y%2F";
    private const string HostileEscaped = "123%2F..%2Fadmin%3Fx%3D1%23y%252F";

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public Uri? RequestedUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            RequestedUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            });
        }
    }

    private static (HttpClient Client, CapturingHandler Handler) ClientFor(string baseAddress)
    {
        var handler = new CapturingHandler();
        return (new HttpClient(handler) { BaseAddress = new Uri(baseAddress) }, handler);
    }

    private static async Task<Uri> ViaCepRequest(string cep)
    {
        var (client, handler) = ClientFor(new ViaCepOptions().BaseUrl);
        var result = await new ViaCepProvider(client, NullLogger<ViaCepProvider>.Instance)
            .LookupAsync(cep, CancellationToken.None);
        result.Should().BeNull();
        return handler.RequestedUri!;
    }

    private static async Task<Uri> BrasilApiCepRequest(string cep)
    {
        var (client, handler) = ClientFor(new BrasilApiOptions().BaseUrl);
        var result = await new BrasilApiCepProvider(client, NullLogger<BrasilApiCepProvider>.Instance)
            .LookupAsync(cep, CancellationToken.None);
        result.Should().BeNull();
        return handler.RequestedUri!;
    }

    private static async Task<Uri> BrasilApiCnpjRequest(string cnpj)
    {
        var (client, handler) = ClientFor(new BrasilApiOptions().BaseUrl);
        var result = await new BrasilApiCnpjProvider(client, NullLogger<BrasilApiCnpjProvider>.Instance)
            .LookupAsync(cnpj, CancellationToken.None);
        result.Should().BeNull();
        return handler.RequestedUri!;
    }

    private static async Task<Uri> OpenFoodFactsRequest(string barcode)
    {
        var (client, handler) = ClientFor(new OpenFoodFactsOptions().BaseUrl);
        var provider = new OpenFoodFactsProvider(
            client, Substitute.For<ICacheService>(), NullLogger<OpenFoodFactsProvider>.Instance);

        // O provedor trata resposta diferente de 2xx como falha; o que importa aqui é o pedido feito.
        await provider.Invoking(p => p.LookupAsync(barcode, CancellationToken.None))
            .Should().ThrowAsync<HttpRequestException>();

        return handler.RequestedUri!;
    }

    [Fact]
    public async Task ViaCep_DigitsOnly_KeepsTheUrlUnchanged()
    {
        (await ViaCepRequest("01310100")).AbsoluteUri
            .Should().Be("https://viacep.com.br/ws/01310100/json/");
    }

    [Fact]
    public async Task ViaCep_ReservedCharacters_StayInsideOnePathSegment()
    {
        var uri = await ViaCepRequest(Hostile);

        uri.AbsoluteUri.Should().Be($"https://viacep.com.br/ws/{HostileEscaped}/json/");
        uri.Query.Should().BeEmpty();
        uri.Fragment.Should().BeEmpty();
    }

    [Fact]
    public async Task ViaCep_LeadingSlash_DoesNotChangeTheHost()
    {
        var uri = await ViaCepRequest("/outro-host.example/x");

        uri.Host.Should().Be("viacep.com.br");
        uri.AbsoluteUri.Should().Be("https://viacep.com.br/ws/%2Foutro-host.example%2Fx/json/");
    }

    [Fact]
    public async Task BrasilApiCep_DigitsOnly_KeepsTheUrlUnchanged()
    {
        (await BrasilApiCepRequest("01310100")).AbsoluteUri
            .Should().Be("https://brasilapi.com.br/api/cep/v2/01310100");
    }

    [Fact]
    public async Task BrasilApiCep_ReservedCharacters_StayInsideOnePathSegment()
    {
        var uri = await BrasilApiCepRequest(Hostile);

        uri.AbsoluteUri.Should().Be($"https://brasilapi.com.br/api/cep/v2/{HostileEscaped}");
        uri.Query.Should().BeEmpty();
        uri.Fragment.Should().BeEmpty();
    }

    [Fact]
    public async Task BrasilApiCnpj_DigitsOnly_KeepsTheUrlUnchanged()
    {
        (await BrasilApiCnpjRequest("11222333000181")).AbsoluteUri
            .Should().Be("https://brasilapi.com.br/api/cnpj/v1/11222333000181");
    }

    [Fact]
    public async Task BrasilApiCnpj_ReservedCharacters_StayInsideOnePathSegment()
    {
        var uri = await BrasilApiCnpjRequest(Hostile);

        uri.AbsoluteUri.Should().Be($"https://brasilapi.com.br/api/cnpj/v1/{HostileEscaped}");
        uri.Query.Should().BeEmpty();
        uri.Fragment.Should().BeEmpty();
    }

    [Fact]
    public async Task OpenFoodFacts_DigitsOnly_KeepsTheUrlUnchanged()
    {
        (await OpenFoodFactsRequest("7891000100103")).AbsoluteUri
            .Should().Be("https://world.openfoodfacts.org/api/v0/product/7891000100103.json");
    }

    [Fact]
    public async Task OpenFoodFacts_ReservedCharacters_StayInsideOnePathSegment()
    {
        var uri = await OpenFoodFactsRequest(Hostile);

        uri.AbsoluteUri.Should().Be($"https://world.openfoodfacts.org/api/v0/product/{HostileEscaped}.json");
        uri.Query.Should().BeEmpty();
        uri.Fragment.Should().BeEmpty();
    }
}
