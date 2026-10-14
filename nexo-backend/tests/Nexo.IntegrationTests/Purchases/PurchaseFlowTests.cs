using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nexo.Application.Features.Products;
using Nexo.Application.Features.Stock;
using Nexo.Application.Features.Suppliers;
using Nexo.Application.Modules.Varejo;
using Nexo.Domain.Entities;
using Nexo.Domain.Enums;
using Nexo.Infrastructure.Persistence;
using Nexo.IntegrationTests.Common;
using Nexo.IntegrationTests.Helpers;
using Xunit;

namespace Nexo.IntegrationTests.Purchases;

/// <summary>Compras (PR 47): pedido → envio → recebimento (estoque + custo + conta a pagar) → cancelamento.</summary>
[Collection("Integration")]
public class PurchaseFlowTests
{
    private const string Base = "/api/varejo/purchases";
    private readonly TestWebApplicationFactory _factory;
    public PurchaseFlowTests(TestWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Receiving_an_order_enters_stock_updates_cost_and_creates_the_payable()
    {
        var c = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var supplier = await SupplierAsync(c, paymentTermsDays: 28);
        var product = await ProductAsync(c);

        var purchase = await DraftAsync(c, supplier, product, quantity: 10, unitCost: 3.333m);
        purchase.TotalAmount.Should().Be(33.33m);
        purchase.Items.Single().ProductName.Should().NotBeEmpty();
        purchase.SupplierName.Should().NotBeEmpty();

        var ordered = await OkAsync<PurchaseDto>(await c.PostAsync($"{Base}/{purchase.Id}/order", null));
        ordered.Status.Should().Be("Ordered");
        (await c.PostAsJsonAsync($"{Base}/{purchase.Id}/items", new AddPurchaseItemRequest(product, 1, 1m)))
            .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);   // frozen once ordered

        var received = await OkAsync<PurchaseDto>(await c.PostAsJsonAsync($"{Base}/{purchase.Id}/confirm",
            new ReceivePurchaseRequest(InvoiceNumber: "NF-123")));
        received.Status.Should().Be("Confirmed");
        received.InvoiceNumber.Should().Be("NF-123");

        (await StockAsync(c, product)).Should().Be(10);
        (await c.GetFromJsonAsync<ProductDto>($"/api/products/{product}"))!.CostPrice.Should().Be(3.333m);
        var payable = (await FinancialAsync("Purchase", purchase.Id)).Single();
        payable.TransactionType.Should().Be(TransactionType.Payable);
        payable.Status.Should().Be(TransactionStatus.Pending);
        payable.Amount.Should().Be(33.33m);
        payable.DueDate.Date.Should().Be(received.ReceivedAt!.Value.AddDays(28).Date);   // supplier terms

        (await c.PostAsJsonAsync($"{Base}/{purchase.Id}/confirm", new ReceivePurchaseRequest()))
            .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);   // received once
    }

    [Fact]
    public async Task Cancelling_a_paid_purchase_takes_goods_out_and_becomes_a_receivable()
    {
        var c = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var supplier = await SupplierAsync(c);
        var product = await ProductAsync(c);
        var purchase = await DraftAsync(c, supplier, product, quantity: 4, unitCost: 5m);
        await OkAsync<PurchaseDto>(await c.PostAsJsonAsync($"{Base}/{purchase.Id}/confirm", new ReceivePurchaseRequest(PaidNow: true)));
        (await FinancialAsync("Purchase", purchase.Id)).Single().Status.Should().Be(TransactionStatus.Paid);

        var cancelled = await OkAsync<PurchaseDto>(await c.PostAsJsonAsync($"{Base}/{purchase.Id}/cancel",
            new CancelPurchaseRequest("Mercadoria devolvida ao fornecedor")));

        cancelled.Status.Should().Be("Cancelled");
        cancelled.CancellationReason.Should().Be("Mercadoria devolvida ao fornecedor");
        (await StockAsync(c, product)).Should().Be(0);
        var refund = (await FinancialAsync("PurchaseCancellation", purchase.Id)).Single();
        refund.TransactionType.Should().Be(TransactionType.Receivable);
        refund.Status.Should().Be(TransactionStatus.Pending);
        refund.Amount.Should().Be(20m);
    }

    [Fact]
    public async Task Cancelling_a_pending_purchase_cancels_its_payable()
    {
        var c = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var purchase = await DraftAsync(c, await SupplierAsync(c), await ProductAsync(c), quantity: 2, unitCost: 7m);
        await OkAsync<PurchaseDto>(await c.PostAsJsonAsync($"{Base}/{purchase.Id}/confirm", new ReceivePurchaseRequest()));

        await OkAsync<PurchaseDto>(await c.PostAsync($"{Base}/{purchase.Id}/cancel", null));

        (await FinancialAsync("Purchase", purchase.Id)).Single().Status.Should().Be(TransactionStatus.Cancelled);
        (await FinancialAsync("PurchaseCancellation", purchase.Id)).Should().BeEmpty();
    }

    [Fact]
    public async Task A_received_purchase_whose_goods_were_sold_cannot_be_cancelled()
    {
        var c = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var product = await ProductAsync(c);
        var purchase = await DraftAsync(c, await SupplierAsync(c), product, quantity: 10, unitCost: 1m);
        await OkAsync<PurchaseDto>(await c.PostAsJsonAsync($"{Base}/{purchase.Id}/confirm", new ReceivePurchaseRequest()));
        (await c.PostAsJsonAsync("/api/stock/adjust", new AdjustStockRequest(product, 6, "ManualExit", "vendido")))
            .EnsureSuccessStatusCode();

        (await c.PostAsync($"{Base}/{purchase.Id}/cancel", null)).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        (await StockAsync(c, product)).Should().Be(4);
        (await c.GetFromJsonAsync<PurchaseDto>($"{Base}/{purchase.Id}"))!.Status.Should().Be("Confirmed");
    }

    [Fact]
    public async Task An_inactive_supplier_cannot_receive_new_orders()
    {
        var c = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var supplier = await SupplierAsync(c);
        (await c.PostAsync($"/api/suppliers/{supplier}/deactivate", null)).EnsureSuccessStatusCode();

        (await c.PostAsJsonAsync(Base, new CreatePurchaseRequest(supplier)))
            .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task The_due_date_counts_from_when_the_goods_arrive_and_dates_without_zone_are_accepted()
    {
        var c = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var purchase = await DraftAsync(c, await SupplierAsync(c, paymentTermsDays: 10), await ProductAsync(c), quantity: 1, unitCost: 9m);

        // received on a known day, sent without a time zone (as a plain date picker would)
        var resp = await c.PostAsync($"{Base}/{purchase.Id}/confirm", JsonContent.Create(new { receivedAt = "2026-11-02T10:00:00" }));
        var received = await OkAsync<PurchaseDto>(resp);

        received.ReceivedAt!.Value.Date.Should().Be(new DateTime(2026, 11, 2));
        (await FinancialAsync("Purchase", purchase.Id)).Single().DueDate.Date.Should().Be(new DateTime(2026, 11, 12));
    }

    [Fact]
    public async Task The_same_product_cannot_be_added_twice()
    {
        var c = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var product = await ProductAsync(c);
        var purchase = await DraftAsync(c, await SupplierAsync(c), product, quantity: 1, unitCost: 2m);

        (await c.PostAsJsonAsync($"{Base}/{purchase.Id}/items", new AddPurchaseItemRequest(product, 3, 2m)))
            .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Draft_items_can_be_removed_and_the_total_follows()
    {
        var c = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var purchase = await DraftAsync(c, await SupplierAsync(c), await ProductAsync(c), quantity: 2, unitCost: 4m);
        var withTwo = await OkAsync<PurchaseDto>(await c.PostAsJsonAsync($"{Base}/{purchase.Id}/items",
            new AddPurchaseItemRequest(await ProductAsync(c), 1, 10m)));
        withTwo.Items.Should().HaveCount(2);
        withTwo.TotalAmount.Should().Be(18m);

        var withOne = await OkAsync<PurchaseDto>(await c.DeleteAsync($"{Base}/{purchase.Id}/items/{withTwo.Items[0].Id}"));

        withOne.Items.Should().ContainSingle();
        withOne.TotalAmount.Should().Be(withOne.Items[0].Total);
    }

    [Theory]
    [InlineData("Other", HttpStatusCode.Created)]       // foreign / no CPF-CNPJ — offered by the UI
    [InlineData("Passport", HttpStatusCode.UnprocessableEntity)]
    public async Task Supplier_document_type_is_validated_not_crashed(string documentType, HttpStatusCode expected)
    {
        var c = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var doc = Guid.NewGuid().ToString("N")[..12];

        var resp = await c.PostAsJsonAsync("/api/suppliers",
            new CreateSupplierRequest("Company", $"Fornecedor {doc}", documentType, doc));

        resp.StatusCode.Should().Be(expected);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static async Task<T> OkAsync<T>(HttpResponseMessage resp)
    {
        resp.IsSuccessStatusCode.Should().BeTrue(await resp.Content.ReadAsStringAsync());
        return (await resp.Content.ReadFromJsonAsync<T>())!;
    }

    private static async Task<Guid> SupplierAsync(HttpClient c, int? paymentTermsDays = null)
    {
        var doc = string.Concat(Guid.NewGuid().ToByteArray().Take(7).Select(b => (b % 100).ToString("D2")));   // 14 digits, unique
        var resp = await c.PostAsJsonAsync("/api/suppliers", new CreateSupplierRequest(
            "Company", $"Fornecedor {doc}", "CNPJ", doc, PaymentTermsDays: paymentTermsDays));
        return (await OkAsync<SupplierDto>(resp)).Id;
    }

    private static async Task<Guid> ProductAsync(HttpClient c)
    {
        var code = $"PR47-{Guid.NewGuid():N}"[..20];
        var resp = await c.PostAsJsonAsync("/api/products",
            new CreateProductRequest(Code: code, Name: $"Produto {code}", Unit: "Un", SalePrice: 10m, CostPrice: 1m, TrackStock: true));
        return (await OkAsync<ProductDto>(resp)).Id;
    }

    private static async Task<PurchaseDto> DraftAsync(HttpClient c, Guid supplier, Guid product, decimal quantity, decimal unitCost)
    {
        var draft = await OkAsync<PurchaseDto>(await c.PostAsJsonAsync(Base, new CreatePurchaseRequest(supplier)));
        return await OkAsync<PurchaseDto>(await c.PostAsJsonAsync($"{Base}/{draft.Id}/items",
            new AddPurchaseItemRequest(product, quantity, unitCost)));
    }

    private static async Task<decimal> StockAsync(HttpClient c, Guid productId)
    {
        var resp = await c.GetAsync($"/api/stock/product/{productId}");
        if (resp.StatusCode == HttpStatusCode.NotFound) return 0;
        return (await resp.Content.ReadFromJsonAsync<StockItemDto>())!.CurrentQuantity;
    }

    private async Task<List<FinancialTransaction>> FinancialAsync(string referenceType, Guid id)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        return await db.FinancialTransactions.IgnoreQueryFilters()
            .Where(t => t.ReferenceType == referenceType && t.ReferenceId == id).ToListAsync();
    }
}
