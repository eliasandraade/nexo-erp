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
/// PDV money rules (PR 43): troco, only physical cash in the drawer, Pix/card as settled revenue,
/// idempotent / concurrent confirmation and no cash into a closed drawer.
/// </summary>
[Collection("Integration")]
public class PdvPaymentIntegrityTests
{
    private const string ManagerLogin = "pdv.integrity.manager";
    private readonly TestWebApplicationFactory _factory;
    public PdvPaymentIntegrityTests(TestWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Cash_above_the_total_gives_change_and_the_drawer_keeps_only_the_total()
    {
        var client = await AdminWithOpenCashAsync();
        var sale = await DraftSaleAsync(client, price: 42.30m);

        var confirmed = await ConfirmOkAsync(client, sale.Id, new PaymentInput("Cash", "Cash", 50m));

        confirmed.Status.Should().Be("Paid");
        confirmed.Payments.Should().ContainSingle();
        confirmed.Payments[0].Amount.Should().Be(42.30m);
        confirmed.Payments[0].TenderedAmount.Should().Be(50m);
        confirmed.Payments[0].ChangeAmount.Should().Be(7.70m);

        var drawer = await CashMovementsForAsync(sale.Id);
        drawer.Should().ContainSingle().Which.Amount.Should().Be(42.30m);
    }

    [Theory]
    [InlineData("Pix")]
    [InlineData("Debit")]
    [InlineData("Credit")]
    public async Task Pix_and_cards_never_enter_the_drawer_and_are_settled_revenue(string method)
    {
        var client = await AdminWithOpenCashAsync();
        var sale = await DraftSaleAsync(client, price: 80m);

        var confirmed = await ConfirmOkAsync(client, sale.Id, new PaymentInput(method, "Cash", 80m));

        confirmed.Status.Should().Be("Paid");
        (await CashMovementsForAsync(sale.Id)).Should().BeEmpty();
        var revenue = await FinancialFor(sale.Id);
        revenue.Should().ContainSingle();
        revenue[0].Amount.Should().Be(80m);
        revenue[0].Status.Should().Be(TransactionStatus.Paid);
        revenue[0].TransactionType.Should().Be(TransactionType.Receivable);
    }

    [Fact]
    public async Task Pix_above_the_total_is_rejected_and_nothing_is_recorded()
    {
        var client = await AdminWithOpenCashAsync();
        var sale = await DraftSaleAsync(client, price: 42m);

        var resp = await client.PostAsJsonAsync($"/api/sales/{sale.Id}/confirm",
            new ConfirmSaleRequest([new PaymentInput("Pix", "Cash", 50m)]));

        resp.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await client.GetFromJsonAsync<SaleDto>($"/api/sales/{sale.Id}"))!.Status.Should().Be("Draft");
        (await FinancialFor(sale.Id)).Should().BeEmpty();
    }

    [Fact]
    public async Task Mixed_pix_and_cash_puts_only_the_cash_part_net_of_change_in_the_drawer()
    {
        var client = await AdminWithOpenCashAsync();
        var sale = await DraftSaleAsync(client, price: 100m);

        var confirmed = await ConfirmOkAsync(client, sale.Id,
            new PaymentInput("Pix", "Cash", 60m), new PaymentInput("Cash", "Cash", 50m));

        confirmed.Payments.Sum(p => p.Amount).Should().Be(100m);
        (await CashMovementsForAsync(sale.Id)).Should().ContainSingle().Which.Amount.Should().Be(40m);
        (await FinancialFor(sale.Id)).Should().ContainSingle().Which.Amount.Should().Be(60m);
    }

    [Fact]
    public async Task Retrying_a_confirmation_returns_the_sale_without_recording_twice()
    {
        var client = await AdminWithOpenCashAsync();
        var (sale, productId) = await DraftSaleWithProductAsync(client, price: 25m);
        var payment = new PaymentInput("Cash", "Cash", 30m);

        var first  = await ConfirmOkAsync(client, sale.Id, payment);
        var replay = await ConfirmOkAsync(client, sale.Id, payment);

        replay.Id.Should().Be(first.Id);
        replay.Status.Should().Be("Paid");
        (await PaymentsForAsync(sale.Id)).Should().ContainSingle();
        (await CashMovementsForAsync(sale.Id)).Should().ContainSingle();
        (await client.GetFromJsonAsync<StockItemDto>($"/api/stock/product/{productId}"))!
            .CurrentQuantity.Should().Be(9);

        // A different payment for an already paid sale is a conflict, not a replay.
        var other = await client.PostAsJsonAsync($"/api/sales/{sale.Id}/confirm",
            new ConfirmSaleRequest([new PaymentInput("Pix", "Cash", 25m)]));
        other.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Concurrent_confirmations_record_the_sale_once()
    {
        var client = await AdminWithOpenCashAsync();
        var (sale, productId) = await DraftSaleWithProductAsync(client, price: 10m);
        var body = new ConfirmSaleRequest([new PaymentInput("Cash", "Cash", 10m)]);

        var second = _factory.CreateApiClient();
        second.DefaultRequestHeaders.Authorization = client.DefaultRequestHeaders.Authorization;

        var responses = await Task.WhenAll(
            client.PostAsJsonAsync($"/api/sales/{sale.Id}/confirm", body),
            second.PostAsJsonAsync($"/api/sales/{sale.Id}/confirm", body));

        responses.Select(r => r.StatusCode).Should().AllBeEquivalentTo(HttpStatusCode.OK);
        (await PaymentsForAsync(sale.Id)).Should().ContainSingle();
        (await CashMovementsForAsync(sale.Id)).Should().ContainSingle();
        (await client.GetFromJsonAsync<StockItemDto>($"/api/stock/product/{productId}"))!
            .CurrentQuantity.Should().Be(9);
    }

    [Fact]
    public async Task Cash_cannot_land_in_a_closed_drawer_but_pix_still_sells()
    {
        var manager = await ManagerClientAsync();
        var open = await manager.PostAsJsonAsync("/api/cash/sessions/open",
            new OpenCashSessionRequest(OpeningBalance: 0, Notes: "PR43 closed-drawer test"));
        var session = open.StatusCode == HttpStatusCode.Created
            ? (await open.Content.ReadFromJsonAsync<CashSessionDto>())!
            : (await manager.GetFromJsonAsync<CashSessionDto>("/api/cash/sessions/open"))!;

        var cashSale = await DraftSaleAsync(manager, price: 15m, cashSessionId: session.Id);
        var pixSale  = await DraftSaleAsync(manager, price: 15m, cashSessionId: session.Id);

        (await manager.PostAsJsonAsync($"/api/cash/sessions/{session.Id}/close",
            new CloseCashSessionRequest(ClosingBalance: 0))).StatusCode.Should().Be(HttpStatusCode.OK);

        var cash = await manager.PostAsJsonAsync($"/api/sales/{cashSale.Id}/confirm",
            new ConfirmSaleRequest([new PaymentInput("Cash", "Cash", 15m)]));
        cash.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await CashMovementsForAsync(cashSale.Id)).Should().BeEmpty();

        await ConfirmOkAsync(manager, pixSale.Id, new PaymentInput("Pix", "Cash", 15m));
    }

    [Fact]
    public async Task A_fractional_discount_is_kept_in_cents_so_pix_can_pay_the_exact_total()
    {
        var client = await AdminWithOpenCashAsync();
        var sale = await DraftSaleAsync(client, price: 33.33m);

        // 10% of 33,33 sent unrounded by an older client: 3.333 → total 30.00
        var resp = await client.PostAsJsonAsync($"/api/sales/{sale.Id}/confirm",
            new ConfirmSaleRequest([new PaymentInput("Pix", "Cash", 30.00m)], DiscountAmount: 3.333m));

        resp.StatusCode.Should().Be(HttpStatusCode.OK, await resp.Content.ReadAsStringAsync());
        (await resp.Content.ReadFromJsonAsync<SaleDto>())!.Total.Should().Be(30.00m);
    }

    [Fact]
    public async Task A_negative_discount_is_rejected()
    {
        var client = await AdminWithOpenCashAsync();
        var sale = await DraftSaleAsync(client, price: 10m);

        var resp = await client.PostAsJsonAsync($"/api/sales/{sale.Id}/confirm",
            new ConfirmSaleRequest([new PaymentInput("Cash", "Cash", 15m)], DiscountAmount: -5m));

        resp.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task A_retry_with_another_method_is_a_conflict_not_a_replay()
    {
        var client = await AdminWithOpenCashAsync();
        var sale = await DraftSaleAsync(client, price: 20m);
        await ConfirmOkAsync(client, sale.Id, new PaymentInput("Cash", "Cash", 20m));

        var resp = await client.PostAsJsonAsync($"/api/sales/{sale.Id}/confirm",
            new ConfirmSaleRequest([new PaymentInput("Pix", "Cash", 20m)]));

        resp.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Cancelling_reverses_only_the_cash_in_the_drawer_and_counters_settled_pix()
    {
        var client = await AdminWithOpenCashAsync();
        var sale = await DraftSaleAsync(client, price: 100m);
        // 30 cash + 30 Pix + 40 a prazo → Confirmed (cancellable)
        var confirmed = await ConfirmOkAsync(client, sale.Id,
            new PaymentInput("Cash", "Cash", 30m),
            new PaymentInput("Pix", "Cash", 30m),
            new PaymentInput("Credit", "Credit", 40m, DateTime.UtcNow.AddDays(30)));
        confirmed.Status.Should().Be("Confirmed");

        (await client.PostAsJsonAsync($"/api/sales/{sale.Id}/cancel", new { })).EnsureSuccessStatusCode();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        var refunds = await db.CashMovements.IgnoreQueryFilters()
            .Where(m => m.ReferenceType == "SaleCancellation" && m.ReferenceId == sale.Id).ToListAsync();
        refunds.Should().ContainSingle().Which.Amount.Should().Be(30m);

        var counter = await db.FinancialTransactions.IgnoreQueryFilters()
            .Where(t => t.ReferenceType == "SaleCancellation" && t.ReferenceId == sale.Id).ToListAsync();
        counter.Should().ContainSingle();
        counter[0].Amount.Should().Be(30m);
        counter[0].TransactionType.Should().Be(TransactionType.Payable);

        var receivable = await db.FinancialTransactions.IgnoreQueryFilters()
            .SingleAsync(t => t.ReferenceType == "Sale" && t.ReferenceId == sale.Id && t.Amount == 40m);
        receivable.Status.Should().Be(TransactionStatus.Cancelled);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private async Task<HttpClient> AdminWithOpenCashAsync()
    {
        var client = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var resp = await client.PostAsJsonAsync("/api/cash/sessions/open",
            new OpenCashSessionRequest(OpeningBalance: 0, Notes: "PR43 test session"));
        resp.StatusCode.Should().BeOneOf(HttpStatusCode.Created, HttpStatusCode.Conflict);
        return client;
    }

    private static async Task<SaleDto> DraftSaleAsync(HttpClient client, decimal price, Guid? cashSessionId = null)
        => (await DraftSaleWithProductAsync(client, price, cashSessionId)).Sale;

    private static async Task<(SaleDto Sale, Guid ProductId)> DraftSaleWithProductAsync(
        HttpClient client, decimal price, Guid? cashSessionId = null)
    {
        var code = $"PR43-{Guid.NewGuid():N}"[..20];
        var productResp = await client.PostAsJsonAsync("/api/products",
            new CreateProductRequest(Code: code, Name: $"PR43 {code}", Unit: "Un",
                SalePrice: price, CostPrice: price / 2, TrackStock: true));
        productResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var product = (await productResp.Content.ReadFromJsonAsync<ProductDto>())!;

        (await client.PostAsJsonAsync("/api/stock/adjust",
            new AdjustStockRequest(product.Id, 10, "ManualEntry", "PR43 stock"))).EnsureSuccessStatusCode();

        var sale = (await (await client.PostAsJsonAsync("/api/sales", new CreateSaleRequest(CashSessionId: cashSessionId)))
            .Content.ReadFromJsonAsync<SaleDto>())!;
        (await client.PostAsJsonAsync($"/api/sales/{sale.Id}/items",
            new AddSaleItemRequest(product.Id, Quantity: 1, UnitPrice: price))).EnsureSuccessStatusCode();
        return (sale, product.Id);
    }

    private static async Task<SaleDto> ConfirmOkAsync(HttpClient client, Guid saleId, params PaymentInput[] payments)
    {
        var resp = await client.PostAsJsonAsync($"/api/sales/{saleId}/confirm", new ConfirmSaleRequest([.. payments]));
        resp.StatusCode.Should().Be(HttpStatusCode.OK, await resp.Content.ReadAsStringAsync());
        return (await resp.Content.ReadFromJsonAsync<SaleDto>())!;
    }

    private async Task<List<CashMovement>> CashMovementsForAsync(Guid saleId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        return await db.CashMovements.IgnoreQueryFilters()
            .Where(m => m.ReferenceType == "Sale" && m.ReferenceId == saleId).ToListAsync();
    }

    private async Task<List<FinancialTransaction>> FinancialFor(Guid saleId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        return await db.FinancialTransactions.IgnoreQueryFilters()
            .Where(t => t.ReferenceType == "Sale" && t.ReferenceId == saleId).ToListAsync();
    }

    private async Task<List<SalePayment>> PaymentsForAsync(Guid saleId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        return await db.SalePayments.IgnoreQueryFilters().Where(p => p.SaleId == saleId).ToListAsync();
    }

    private async Task<HttpClient> ManagerClientAsync()
    {
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
            if (!await db.Users.IgnoreQueryFilters().AnyAsync(u => u.Login == ManagerLogin))
            {
                var admin = await db.Users.IgnoreQueryFilters().FirstAsync(u => u.Login == TestCredentials.AdminLogin);
                var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
                db.Users.Add(User.Create(
                    tenantId: admin.TenantId, fullName: "PR43 Manager",
                    email: $"{ManagerLogin}@{TestCredentials.TestLocalDomain}", login: ManagerLogin,
                    passwordHash: hasher.Hash(TestCredentials.SameTenantManagerPassword), role: UserRole.Gerente));
                await db.SaveChangesAsync();
            }
        }
        return await AuthClientFactory.LoginAsync(_factory, ManagerLogin, TestCredentials.SameTenantManagerPassword);
    }
}
