using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nexo.Application.Features.Cash;
using Nexo.Application.Features.Products;
using Nexo.Application.Features.Sales;
using Nexo.Application.Features.Stock;
using Nexo.Domain.Enums;
using Nexo.Infrastructure.Persistence;
using Nexo.IntegrationTests.Common;
using Nexo.IntegrationTests.Helpers;

namespace Nexo.IntegrationTests.Sales;

/// <summary>
/// Item incluído enquanto a venda é concluída (duplo clique no PDV, duas abas): ou entra antes da conclusão, e então é
/// cobrado e baixado do estoque, ou é recusado. Nunca pode cair numa venda já paga — sem pagamento, sem baixa de
/// estoque e com o total da venda alterado depois de fechada.
/// </summary>
[Collection("Integration")]
public class SaleItemConfirmRaceTests
{
    private readonly TestWebApplicationFactory _factory;
    public SaleItemConfirmRaceTests(TestWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task An_item_added_while_the_sale_is_confirmed_never_lands_in_the_paid_sale()
    {
        var client = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var open = await client.PostAsJsonAsync("/api/cash/sessions/open", new OpenCashSessionRequest(OpeningBalance: 0, Notes: "corrida item x conclusão"));
        open.StatusCode.Should().BeOneOf(HttpStatusCode.Created, HttpStatusCode.Conflict);

        var extra = await ProductAsync(client, 7m);
        for (var round = 0; round < 6; round++)
        {
            var first = await ProductAsync(client, 10m);
            var sale = (await (await client.PostAsJsonAsync("/api/sales", new CreateSaleRequest()))
                .Content.ReadFromJsonAsync<SaleDto>())!;
            (await client.PostAsJsonAsync($"/api/sales/{sale.Id}/items", new AddSaleItemRequest(first, Quantity: 1, UnitPrice: 10m)))
                .EnsureSuccessStatusCode();

            // a conclusão e cinco inclusões ao mesmo tempo, de clientes separados
            var calls = new List<Task<HttpResponseMessage>>
            {
                Clone(client).PostAsJsonAsync($"/api/sales/{sale.Id}/confirm", new ConfirmSaleRequest([new PaymentInput("Cash", "Cash", 10m)])),
            };
            calls.AddRange(Enumerable.Range(0, 5).Select(_ =>
                Clone(client).PostAsJsonAsync($"/api/sales/{sale.Id}/items", new AddSaleItemRequest(extra, Quantity: 1, UnitPrice: 7m))));
            var responses = await Task.WhenAll(calls);
            responses.Should().OnlyContain(r => (int)r.StatusCode < 500, "uma corrida nunca é erro do servidor");

            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
            var row = await db.Sales.IgnoreQueryFilters().AsNoTracking().SingleAsync(s => s.Id == sale.Id);
            if (row.Status == SaleStatus.Draft) continue;   // a conclusão perdeu para as inclusões e foi recusada: nada a conferir

            var items = await db.SaleItems.IgnoreQueryFilters().AsNoTracking().Where(i => i.SaleId == sale.Id).ToListAsync();
            var paid = await db.SalePayments.IgnoreQueryFilters().AsNoTracking().Where(p => p.SaleId == sale.Id).SumAsync(p => p.Amount);
            var deducted = await db.StockMovements.IgnoreQueryFilters().AsNoTracking()
                .Where(m => m.ReferenceType == "Sale" && m.ReferenceId == sale.Id && m.MovementType == StockMovementType.SaleOutput)
                .SumAsync(m => m.Quantity);

            row.Total.Should().Be(paid, $"rodada {round}: o total da venda concluída é o que foi pago");
            items.Sum(i => i.Quantity).Should().Be(deducted, $"rodada {round}: todo item da venda concluída saiu do estoque");
        }
    }

    private HttpClient Clone(HttpClient authenticated)
    {
        var c = _factory.CreateApiClient();
        c.DefaultRequestHeaders.Authorization = authenticated.DefaultRequestHeaders.Authorization;
        return c;
    }

    private static async Task<Guid> ProductAsync(HttpClient client, decimal price)
    {
        var code = $"RACE-{Guid.NewGuid():N}"[..20];
        var resp = await client.PostAsJsonAsync("/api/products",
            new CreateProductRequest(Code: code, Name: $"Corrida {code}", Unit: "Un", SalePrice: price, CostPrice: price / 2, TrackStock: true));
        resp.StatusCode.Should().Be(HttpStatusCode.Created, await resp.Content.ReadAsStringAsync());
        var product = (await resp.Content.ReadFromJsonAsync<ProductDto>())!;
        (await client.PostAsJsonAsync("/api/stock/adjust", new AdjustStockRequest(product.Id, 100, "ManualEntry", "corrida")))
            .EnsureSuccessStatusCode();
        return product.Id;
    }
}
