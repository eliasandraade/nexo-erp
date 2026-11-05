using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Nexo.Application.Common;
using Nexo.Application.Features.Auth;
using Nexo.Application.Features.Cash;
using Nexo.Application.Features.Customers;
using Nexo.Application.Features.Products;
using Nexo.Application.Features.Sales;
using Nexo.Application.Features.Stock;
using Nexo.IntegrationTests.Common;
using Nexo.IntegrationTests.Helpers;

namespace Nexo.IntegrationTests.Sales;

/// <summary>
/// A lista paginada de vendas lê só os campos que devolve, sem carregar itens e pagamentos inteiros.
/// Estes testes fixam a resposta: cada campo calculado a partir dessas coleções.
/// </summary>
[Collection("Integration")]
public class SaleListProjectionTests
{
    private readonly HttpClient _client;

    public SaleListProjectionTests(TestWebApplicationFactory factory)
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

    private async Task OpenCashSessionAsync()
    {
        var response = await _client.PostAsJsonAsync("/api/cash/sessions/open",
            new OpenCashSessionRequest(OpeningBalance: 0, Notes: "Sessão de teste"));
        response.StatusCode.Should().BeOneOf(HttpStatusCode.Created, HttpStatusCode.Conflict);
    }

    private async Task<ProductDto> CreateProductAsync(string name)
    {
        var code = $"LST-{Guid.NewGuid():N}"[..14];
        var response = await _client.PostAsJsonAsync("/api/products", new CreateProductRequest(
            Code: code, Name: name, Unit: "Un", SalePrice: 10m, CostPrice: 5m, TrackStock: true));
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var product = (await response.Content.ReadFromJsonAsync<ProductDto>())!;

        (await _client.PostAsJsonAsync("/api/stock/adjust", new AdjustStockRequest(
            ProductId: product.Id, Quantity: 100, MovementType: "ManualEntry", Notes: "Estoque de teste")))
            .IsSuccessStatusCode.Should().BeTrue();

        return product;
    }

    private async Task<SaleDto> CreateSaleAsync(Guid? customerId = null)
    {
        var response = await _client.PostAsJsonAsync("/api/sales", new CreateSaleRequest(CustomerId: customerId));
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<SaleDto>())!;
    }

    private async Task AddItemAsync(Guid saleId, Guid productId, decimal quantity)
    {
        var response = await _client.PostAsJsonAsync($"/api/sales/{saleId}/items",
            new AddSaleItemRequest(ProductId: productId, Quantity: quantity, UnitPrice: 10m));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private async Task<SaleListItemDto> FindInListAsync(Guid saleId, int number)
    {
        var page = await _client.GetFromJsonAsync<PagedResult<SaleListItemDto>>(
            $"/api/sales/paged?page=1&pageSize=100&search={number}");
        return page!.Items.Single(s => s.Id == saleId);
    }

    [Fact]
    public async Task Confirmed_sale_returns_the_fields_computed_from_items_and_payments()
    {
        await AuthenticateAsync();
        await OpenCashSessionAsync();

        // "Z" entra primeiro: o primeiro item é o mais antigo, não o primeiro em ordem alfabética.
        var first = await CreateProductAsync($"Z primeiro {Guid.NewGuid():N}");
        var second = await CreateProductAsync($"A segundo {Guid.NewGuid():N}");

        var customerResponse = await _client.PostAsJsonAsync("/api/customers", new CreateCustomerRequest(
            PersonType: "Individual", Name: $"Cliente {Guid.NewGuid():N}", DocumentType: "Other",
            DocumentNumber: $"{Guid.NewGuid():N}"[..14]));
        customerResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var customer = (await customerResponse.Content.ReadFromJsonAsync<CustomerDto>())!;

        var sale = await CreateSaleAsync(customer.Id);
        await AddItemAsync(sale.Id, first.Id, 2m);
        await AddItemAsync(sale.Id, second.Id, 1.5m);

        // Total 35: dois pagamentos em dinheiro e um em Pix, para a lista de meios sair sem repetição.
        var confirm = await _client.PostAsJsonAsync($"/api/sales/{sale.Id}/confirm", new ConfirmSaleRequest(
            Payments:
            [
                new PaymentInput("Cash", "Cash", 10m),
                new PaymentInput("Pix", "Cash", 15m),
                new PaymentInput("Cash", "Cash", 10m),
            ]));
        confirm.StatusCode.Should().Be(HttpStatusCode.OK);
        var confirmed = (await confirm.Content.ReadFromJsonAsync<SaleDto>())!;

        var row = await FindInListAsync(sale.Id, sale.Number);

        row.Number.Should().Be(sale.Number);
        row.Status.Should().Be(confirmed.Status);
        row.CustomerId.Should().Be(customer.Id);
        row.CustomerName.Should().Be(customer.Name);
        row.SoldByName.Should().NotBeNullOrWhiteSpace();
        row.Total.Should().Be(35m);
        row.Timestamp.Should().BeCloseTo(confirmed.ConfirmedAt!.Value, TimeSpan.FromMilliseconds(1));
        row.ItemCount.Should().Be(2);
        row.TotalQuantity.Should().Be(3.5m);
        row.FirstItemName.Should().Be(first.Name);
        row.PaymentMethods.Should().BeEquivalentTo("Cash", "Pix");
    }

    [Fact]
    public async Task Draft_sale_without_items_or_payments_returns_empty_values()
    {
        await AuthenticateAsync();
        var sale = await CreateSaleAsync();

        var row = await FindInListAsync(sale.Id, sale.Number);

        row.Status.Should().Be("Draft");
        row.CustomerId.Should().BeNull();
        row.CustomerName.Should().BeNull();
        row.SoldByName.Should().NotBeNullOrWhiteSpace();
        row.Timestamp.Should().BeCloseTo(sale.CreatedAt, TimeSpan.FromMilliseconds(1));
        row.ItemCount.Should().Be(0);
        row.TotalQuantity.Should().Be(0m);
        row.FirstItemName.Should().BeNull();
        row.PaymentMethods.Should().BeEmpty();
    }
}
