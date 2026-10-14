using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nexo.Application.Common.Interfaces;
using Nexo.Application.Features.Cash;
using Nexo.Application.Features.Products;
using Nexo.Application.Features.Sales;
using Nexo.Application.Features.Stock;
using Nexo.Domain.Entities;
using Nexo.Domain.Enums;
using Nexo.Infrastructure.Persistence;
using Nexo.IntegrationTests.Common;
using Nexo.IntegrationTests.Helpers;
using Xunit;

namespace Nexo.IntegrationTests.Sales;

/// <summary>
/// Devolução of paid sales and cancellation with a reason (PR 45). Every effect references the
/// devolução, nothing is rewritten; each test runs on its own manager and drawer.
/// </summary>
[Collection("Integration")]
public class SaleReturnTests
{
    private readonly TestWebApplicationFactory _factory;
    public SaleReturnTests(TestWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Partial_then_full_return_refunds_the_discounted_share_and_closes_exactly()
    {
        var (c, session) = await ManagerWithDrawerAsync("ret.partial", opening: 100m);
        // 2 × 50 with a 10 discount → total 90, paid in cash
        var (sale, productId) = await PaidSaleAsync(c, session.Id, unitPrice: 50m, quantity: 2, discount: 10m);

        var first = await ReturnOkAsync(c, sale, quantity: 1, refundMethod: "Cash");
        first.RefundAmount.Should().Be(45m);                    // half of the net 90
        (await StockOfAsync(c, productId)).Should().Be(9);      // 10 − 2 + 1
        var afterFirst = await SaleAsync(c, sale.Id);
        afterFirst.Status.Should().Be("PartiallyReturned");
        afterFirst.ReturnedAmount.Should().Be(45m);
        (await DrawerAsync(c, session.Id)).Summary!.Refunds.Should().Be(45m);

        var second = await ReturnOkAsync(c, sale, quantity: 1, refundMethod: "Pix");
        second.RefundAmount.Should().Be(45m);                   // exactly what was left
        (await SaleAsync(c, sale.Id)).Status.Should().Be("Returned");
        var counter = await FinancialForReturnAsync(second.Id);
        counter.Should().ContainSingle();
        counter[0].TransactionType.Should().Be(TransactionType.Payable);
        counter[0].Status.Should().Be(TransactionStatus.Paid);
        counter[0].Amount.Should().Be(45m);

        // Nothing left to return
        (await PostReturnAsync(c, sale, 1, "Cash")).StatusCode.Should().Be(HttpStatusCode.Conflict);
        await CloseAsync(c, session.Id);
    }

    [Fact]
    public async Task Returning_more_than_was_sold_is_rejected()
    {
        var (c, session) = await ManagerWithDrawerAsync("ret.over", opening: 100m);
        var (sale, _) = await PaidSaleAsync(c, session.Id, unitPrice: 10m, quantity: 1);

        (await PostReturnAsync(c, sale, 2, "Cash")).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        await CloseAsync(c, session.Id);
    }

    [Fact]
    public async Task The_same_idempotency_key_never_refunds_twice()
    {
        var (c, session) = await ManagerWithDrawerAsync("ret.idem", opening: 100m);
        var (sale, _) = await PaidSaleAsync(c, session.Id, unitPrice: 20m, quantity: 2);
        var key = Guid.NewGuid();

        var second = _factory.CreateApiClient();
        second.DefaultRequestHeaders.Authorization = c.DefaultRequestHeaders.Authorization;
        var responses = await Task.WhenAll(
            PostReturnAsync(c, sale, 1, "Cash", key),
            PostReturnAsync(second, sale, 1, "Cash", key));

        responses.Select(r => r.StatusCode).Should().AllBeEquivalentTo(HttpStatusCode.OK);
        var ids = await Task.WhenAll(responses.Select(r => r.Content.ReadFromJsonAsync<SaleReturnDto>()));
        ids.Select(r => r!.Id).Distinct().Should().ContainSingle();
        (await SaleAsync(c, sale.Id)).ReturnedAmount.Should().Be(20m);
        (await DrawerAsync(c, session.Id)).Summary!.Refunds.Should().Be(20m);
        await CloseAsync(c, session.Id);
    }

    [Fact]
    public async Task A_sale_with_open_credit_is_cancelled_not_returned()
    {
        var (c, session) = await ManagerWithDrawerAsync("ret.credit", opening: 0m);
        var (sale, _) = await PaidSaleAsync(c, session.Id, unitPrice: 30m, quantity: 1,
            payments: [new PaymentInput("Credit", "Credit", 30m, DateTime.UtcNow.AddDays(30))]);

        var resp = await PostReturnAsync(c, sale, 1, "Pix");

        resp.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await resp.Content.ReadAsStringAsync()).Should().Contain("cancele");
        await CloseAsync(c, session.Id);
    }

    [Fact]
    public async Task A_cash_refund_needs_money_in_the_drawer()
    {
        var (c, session) = await ManagerWithDrawerAsync("ret.empty", opening: 0m);
        var (sale, _) = await PaidSaleAsync(c, session.Id, unitPrice: 25m, quantity: 1,
            payments: [new PaymentInput("Pix", "Cash", 25m)]);

        (await PostReturnAsync(c, sale, 1, "Cash")).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await PostReturnAsync(c, sale, 1, "Pix")).StatusCode.Should().Be(HttpStatusCode.OK);
        await CloseAsync(c, session.Id);
    }

    [Fact]
    public async Task Cancelling_records_the_reason_and_a_paid_sale_cannot_be_cancelled()
    {
        var (c, session) = await ManagerWithDrawerAsync("ret.cancel", opening: 0m);
        var (credit, _) = await PaidSaleAsync(c, session.Id, unitPrice: 12m, quantity: 1,
            payments: [new PaymentInput("Credit", "Credit", 12m, DateTime.UtcNow.AddDays(5))]);
        var (paid, _) = await PaidSaleAsync(c, session.Id, unitPrice: 12m, quantity: 1);

        (await c.PostAsJsonAsync($"/api/sales/{credit.Id}/cancel", new CancelSaleRequest("Cliente desistiu")))
            .EnsureSuccessStatusCode();
        var cancelled = await SaleAsync(c, credit.Id);
        cancelled.Status.Should().Be("Cancelled");
        cancelled.CancellationReason.Should().Be("Cliente desistiu");

        (await c.PostAsJsonAsync($"/api/sales/{paid.Id}/cancel", new CancelSaleRequest("x")))
            .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        await CloseAsync(c, session.Id);
    }

    [Fact]
    public async Task Partial_returns_never_drift_and_a_free_gift_still_closes_the_sale()
    {
        var (c, session) = await ManagerWithDrawerAsync("ret.rounding", opening: 10m);
        // A: 3 × 1,00 with 1,00 item discount → 2,00 · B: free gift (5,00 − 5,00) → total 2,00
        var a = await ProductAsync(c, 1m);
        var b = await ProductAsync(c, 5m);
        var draft = (await (await c.PostAsJsonAsync("/api/sales", new CreateSaleRequest(CashSessionId: session.Id)))
            .Content.ReadFromJsonAsync<SaleDto>())!;
        (await c.PostAsJsonAsync($"/api/sales/{draft.Id}/items", new AddSaleItemRequest(a, 3, 1m, DiscountAmount: 1m))).EnsureSuccessStatusCode();
        (await c.PostAsJsonAsync($"/api/sales/{draft.Id}/items", new AddSaleItemRequest(b, 1, 5m, DiscountAmount: 5m))).EnsureSuccessStatusCode();
        var sale = (await (await c.PostAsJsonAsync($"/api/sales/{draft.Id}/confirm",
            new ConfirmSaleRequest([new PaymentInput("Cash", "Cash", 2m)]))).Content.ReadFromJsonAsync<SaleDto>())!;
        var lineA = sale.Items.Single(i => i.ProductId == a).Id;
        var lineB = sale.Items.Single(i => i.ProductId == b).Id;

        async Task<SaleReturnDto> Return(params (Guid Line, decimal Qty)[] lines)
        {
            var resp = await c.PostAsJsonAsync($"/api/sales/{sale.Id}/returns", new CreateSaleReturnRequest(
                lines.Select(l => new SaleReturnItemInput(l.Line, l.Qty)).ToList(), "Pix", "teste de arredondamento"));
            resp.StatusCode.Should().Be(HttpStatusCode.OK, await resp.Content.ReadAsStringAsync());
            return (await resp.Content.ReadFromJsonAsync<SaleReturnDto>())!;
        }

        (await Return((lineA, 1))).RefundAmount.Should().Be(0.67m);
        (await Return((lineA, 1))).RefundAmount.Should().Be(0.66m);            // cumulative share 1,33
        (await Return((lineB, 1), (lineA, 1))).RefundAmount.Should().Be(0.67m); // order-independent, closes exactly

        var final = await SaleAsync(c, sale.Id);
        final.Status.Should().Be("Returned");
        final.ReturnedAmount.Should().Be(2m);
        await CloseAsync(c, session.Id);
    }

    [Fact]
    public async Task Returning_only_a_free_gift_moves_stock_without_money()
    {
        var (c, session) = await ManagerWithDrawerAsync("ret.gift", opening: 0m);
        var a = await ProductAsync(c, 10m);
        var gift = await ProductAsync(c, 3m);
        var draft = (await (await c.PostAsJsonAsync("/api/sales", new CreateSaleRequest(CashSessionId: session.Id)))
            .Content.ReadFromJsonAsync<SaleDto>())!;
        (await c.PostAsJsonAsync($"/api/sales/{draft.Id}/items", new AddSaleItemRequest(a, 1, 10m))).EnsureSuccessStatusCode();
        (await c.PostAsJsonAsync($"/api/sales/{draft.Id}/items", new AddSaleItemRequest(gift, 1, 3m, DiscountAmount: 3m))).EnsureSuccessStatusCode();
        var sale = (await (await c.PostAsJsonAsync($"/api/sales/{draft.Id}/confirm",
            new ConfirmSaleRequest([new PaymentInput("Pix", "Cash", 10m)]))).Content.ReadFromJsonAsync<SaleDto>())!;

        var resp = await c.PostAsJsonAsync($"/api/sales/{sale.Id}/returns", new CreateSaleReturnRequest(
            [new SaleReturnItemInput(sale.Items.Single(i => i.ProductId == gift).Id, 1)], "Cash", "brinde devolvido"));

        resp.StatusCode.Should().Be(HttpStatusCode.OK, await resp.Content.ReadAsStringAsync());
        (await resp.Content.ReadFromJsonAsync<SaleReturnDto>())!.RefundAmount.Should().Be(0m);
        (await StockOfAsync(c, gift)).Should().Be(10);              // back in stock
        (await DrawerAsync(c, session.Id)).Summary!.Refunds.Should().Be(0m);
        (await SaleAsync(c, sale.Id)).Status.Should().Be("PartiallyReturned");
        await CloseAsync(c, session.Id);
    }

    private static async Task<Guid> ProductAsync(HttpClient c, decimal price)
    {
        var code = $"PR45-{Guid.NewGuid():N}"[..20];
        var product = (await (await c.PostAsJsonAsync("/api/products",
                new CreateProductRequest(Code: code, Name: code, Unit: "Un", SalePrice: price, CostPrice: 0.5m, TrackStock: true)))
            .Content.ReadFromJsonAsync<ProductDto>())!;
        (await c.PostAsJsonAsync("/api/stock/adjust",
            new AdjustStockRequest(product.Id, 10, "ManualEntry", "PR45"))).EnsureSuccessStatusCode();
        return product.Id;
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static Task<HttpResponseMessage> PostReturnAsync(
        HttpClient c, SaleDto sale, decimal quantity, string refundMethod, Guid? key = null)
        => c.PostAsJsonAsync($"/api/sales/{sale.Id}/returns", new CreateSaleReturnRequest(
            [new SaleReturnItemInput(sale.Items[0].Id, quantity)], refundMethod, "Produto com defeito", key));

    private static async Task<SaleReturnDto> ReturnOkAsync(HttpClient c, SaleDto sale, decimal quantity, string refundMethod)
    {
        var resp = await PostReturnAsync(c, sale, quantity, refundMethod);
        resp.StatusCode.Should().Be(HttpStatusCode.OK, await resp.Content.ReadAsStringAsync());
        return (await resp.Content.ReadFromJsonAsync<SaleReturnDto>())!;
    }

    private static async Task<(SaleDto Sale, Guid ProductId)> PaidSaleAsync(
        HttpClient c, Guid sessionId, decimal unitPrice, decimal quantity, decimal discount = 0,
        PaymentInput[]? payments = null)
    {
        var code = $"PR45-{Guid.NewGuid():N}"[..20];
        var product = (await (await c.PostAsJsonAsync("/api/products",
                new CreateProductRequest(Code: code, Name: code, Unit: "Un", SalePrice: unitPrice, CostPrice: 1m, TrackStock: true)))
            .Content.ReadFromJsonAsync<ProductDto>())!;
        (await c.PostAsJsonAsync("/api/stock/adjust",
            new AdjustStockRequest(product.Id, 10, "ManualEntry", "PR45"))).EnsureSuccessStatusCode();
        var draft = (await (await c.PostAsJsonAsync("/api/sales", new CreateSaleRequest(CashSessionId: sessionId)))
            .Content.ReadFromJsonAsync<SaleDto>())!;
        (await c.PostAsJsonAsync($"/api/sales/{draft.Id}/items",
            new AddSaleItemRequest(product.Id, quantity, unitPrice))).EnsureSuccessStatusCode();

        var total = unitPrice * quantity - discount;
        var confirm = await c.PostAsJsonAsync($"/api/sales/{draft.Id}/confirm", new ConfirmSaleRequest(
            payments?.ToList() ?? [new PaymentInput("Cash", "Cash", total)], DiscountAmount: discount));
        confirm.StatusCode.Should().Be(HttpStatusCode.OK, await confirm.Content.ReadAsStringAsync());
        return ((await confirm.Content.ReadFromJsonAsync<SaleDto>())!, product.Id);
    }

    private static async Task<SaleDto> SaleAsync(HttpClient c, Guid id)
        => (await c.GetFromJsonAsync<SaleDto>($"/api/sales/{id}"))!;

    private static async Task<decimal> StockOfAsync(HttpClient c, Guid productId)
        => (await c.GetFromJsonAsync<StockItemDto>($"/api/stock/product/{productId}"))!.CurrentQuantity;

    private static async Task<CashSessionDto> DrawerAsync(HttpClient c, Guid sessionId)
        => (await c.GetFromJsonAsync<CashSessionDto>($"/api/cash/sessions/{sessionId}"))!;

    private static async Task CloseAsync(HttpClient c, Guid sessionId)
    {
        var detail = await DrawerAsync(c, sessionId);
        (await c.PostAsJsonAsync($"/api/cash/sessions/{sessionId}/close",
            new CloseCashSessionRequest(detail.Summary!.ExpectedBalance))).EnsureSuccessStatusCode();
    }

    private async Task<List<FinancialTransaction>> FinancialForReturnAsync(Guid returnId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        return await db.FinancialTransactions.IgnoreQueryFilters()
            .Where(t => t.ReferenceType == "SaleReturn" && t.ReferenceId == returnId).ToListAsync();
    }

    private async Task<(HttpClient Client, CashSessionDto Session)> ManagerWithDrawerAsync(string login, decimal opening)
    {
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
            if (!await db.Users.IgnoreQueryFilters().AnyAsync(u => u.Login == login))
            {
                var admin = await db.Users.IgnoreQueryFilters().FirstAsync(u => u.Login == TestCredentials.AdminLogin);
                var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
                db.Users.Add(User.Create(
                    tenantId: admin.TenantId, fullName: $"Devolução {login}",
                    email: $"{login}@{TestCredentials.TestLocalDomain}", login: login,
                    passwordHash: hasher.Hash(TestCredentials.SameTenantManagerPassword), role: UserRole.Gerente));
                await db.SaveChangesAsync();
            }
        }
        var client = await AuthClientFactory.LoginAsync(_factory, login, TestCredentials.SameTenantManagerPassword);
        var open = await client.PostAsJsonAsync("/api/cash/sessions/open", new OpenCashSessionRequest(opening, "PR45"));
        open.StatusCode.Should().Be(HttpStatusCode.Created, await open.Content.ReadAsStringAsync());
        return (client, (await open.Content.ReadFromJsonAsync<CashSessionDto>())!);
    }
}
