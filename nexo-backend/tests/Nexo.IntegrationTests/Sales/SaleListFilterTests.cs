using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Nexo.Application.Common;
using Nexo.Application.Features.Auth;
using Nexo.Application.Features.Cash;
using Nexo.Application.Features.Products;
using Nexo.Application.Features.Sales;
using Nexo.Application.Features.Stock;
using Nexo.IntegrationTests.Common;
using Nexo.IntegrationTests.Helpers;

namespace Nexo.IntegrationTests.Sales;

/// <summary>
/// Filtros de status e de forma de pagamento da lista de vendas. Comparar o enum como texto não
/// se traduz em SQL, e o filtro respondia 500 em qualquer valor.
/// </summary>
[Collection("Integration")]
public class SaleListFilterTests
{
    private readonly HttpClient _client;

    public SaleListFilterTests(TestWebApplicationFactory factory)
    {
        _client = factory.CreateApiClient();
    }

    private async Task AuthenticateAsync()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login", TestCredentials.AdminLoginPayload());
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<LoginResponse>();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body!.AccessToken);
    }

    private async Task<SaleDto> CreateDraftAsync()
    {
        var response = await _client.PostAsJsonAsync("/api/sales", new CreateSaleRequest());
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<SaleDto>())!;
    }

    /// <summary>Venda de R$ 10, paga com a forma pedida.</summary>
    private async Task<SaleDto> CreatePaidSaleAsync(string method)
    {
        var opened = await _client.PostAsJsonAsync("/api/cash/sessions/open",
            new OpenCashSessionRequest(OpeningBalance: 0, Notes: "Sessão de teste"));
        opened.StatusCode.Should().BeOneOf(HttpStatusCode.Created, HttpStatusCode.Conflict);

        var code = $"FLT-{Guid.NewGuid():N}"[..14];
        var productResponse = await _client.PostAsJsonAsync("/api/products", new CreateProductRequest(
            Code: code, Name: $"Produto {code}", Unit: "Un", SalePrice: 10m, CostPrice: 5m, TrackStock: true));
        productResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var product = (await productResponse.Content.ReadFromJsonAsync<ProductDto>())!;
        (await _client.PostAsJsonAsync("/api/stock/adjust", new AdjustStockRequest(
            ProductId: product.Id, Quantity: 10, MovementType: "ManualEntry", Notes: "Estoque de teste")))
            .IsSuccessStatusCode.Should().BeTrue();

        var sale = await CreateDraftAsync();
        (await _client.PostAsJsonAsync($"/api/sales/{sale.Id}/items",
            new AddSaleItemRequest(ProductId: product.Id, Quantity: 1m, UnitPrice: 10m)))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var confirm = await _client.PostAsJsonAsync($"/api/sales/{sale.Id}/confirm",
            new ConfirmSaleRequest(Payments: [new PaymentInput(method, "Cash", 10m)]));
        confirm.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await confirm.Content.ReadFromJsonAsync<SaleDto>())!;
    }

    /// <summary>Os ids devolvidos pelo filtro. A busca pelo número isola a venda dos demais testes.</summary>
    private async Task<IReadOnlyList<Guid>> ListAsync(SaleDto sale, string filter)
    {
        var response = await _client.GetAsync($"/api/sales/paged?page=1&pageSize=100&search={sale.Number}&{filter}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var page = await response.Content.ReadFromJsonAsync<PagedResult<SaleListItemDto>>();
        return page!.Items.Select(s => s.Id).ToList();
    }

    [Theory]
    [InlineData("status=Draft")]
    [InlineData("status=draft")]
    public async Task Status_filter_finds_sales_in_that_status(string filter)
    {
        await AuthenticateAsync();
        var draft = await CreateDraftAsync();

        (await ListAsync(draft, filter)).Should().Contain(draft.Id);
    }

    [Fact]
    public async Task Status_filter_leaves_out_other_statuses()
    {
        await AuthenticateAsync();
        var draft = await CreateDraftAsync();

        (await ListAsync(draft, "status=Cancelled")).Should().NotContain(draft.Id);
    }

    [Theory]
    [InlineData("paymentMethod=Debit")]
    [InlineData("paymentMethod=debit")]
    [InlineData("paymentMethod=Card")]
    public async Task Payment_filter_finds_a_debit_sale(string filter)
    {
        await AuthenticateAsync();
        var sale = await CreatePaidSaleAsync("Debit");

        (await ListAsync(sale, filter)).Should().Contain(sale.Id);
    }

    [Theory]
    [InlineData("paymentMethod=Pix")]
    [InlineData("paymentMethod=Credit")]
    public async Task Payment_filter_leaves_out_other_methods(string filter)
    {
        await AuthenticateAsync();
        var sale = await CreatePaidSaleAsync("Debit");

        (await ListAsync(sale, filter)).Should().NotContain(sale.Id);
    }

    [Fact]
    public async Task Card_alias_does_not_match_cash()
    {
        await AuthenticateAsync();
        var sale = await CreatePaidSaleAsync("Cash");

        (await ListAsync(sale, "paymentMethod=Card")).Should().NotContain(sale.Id);
    }

    [Theory]
    [InlineData("status=NaoExiste")]
    [InlineData("paymentMethod=NaoExiste")]
    public async Task Unknown_value_returns_an_empty_list_not_an_error(string filter)
    {
        await AuthenticateAsync();
        var draft = await CreateDraftAsync();

        (await ListAsync(draft, filter)).Should().BeEmpty();
    }
}
