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

namespace Nexo.IntegrationTests.Cash;

/// <summary>
/// Caixa 2.0 (PR 44): expected vs counted with the difference kept in the domain, manual movements
/// limited to suprimento/sangria, sangria bounded by the drawer, receipts by method, history.
/// Each test runs on its own manager so its drawer never mixes with other tests'.
/// </summary>
[Collection("Integration")]
public class CashSessionFlowTests
{
    private readonly TestWebApplicationFactory _factory;
    public CashSessionFlowTests(TestWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Full_shift_closes_with_the_difference_frozen_in_the_history()
    {
        var c = await FreshManagerAsync("cash2.shift");
        var session = await OpenAsync(c, 100m);

        await MoveOkAsync(c, session.Id, "Deposit", 50m, "Troco extra");
        await MoveOkAsync(c, session.Id, "Withdrawal", 30m, "Depósito no banco");
        await SellAsync(c, session.Id, 20m, new PaymentInput("Cash", "Cash", 20m));
        await SellAsync(c, session.Id, 15m, new PaymentInput("Pix", "Cash", 15m));

        var live = (await c.GetFromJsonAsync<CashSessionDto>($"/api/cash/sessions/{session.Id}"))!;
        live.Summary!.ExpectedBalance.Should().Be(140m);   // 100 + 50 − 30 + 20 (Pix stays out)
        live.Summary.CashSales.Should().Be(20m);
        live.Summary.ReceiptsByMethod.Should().Contain(r => r.Method == "Pix" && r.Amount == 15m);
        live.Summary.ReceiptsByMethod.Should().Contain(r => r.Method == "Cash" && r.Amount == 20m);

        var close = await c.PostAsJsonAsync($"/api/cash/sessions/{session.Id}/close",
            new CloseCashSessionRequest(ClosingBalance: 135m, Notes: "Faltou troco de 5"));
        close.StatusCode.Should().Be(HttpStatusCode.OK);
        var closed = (await close.Content.ReadFromJsonAsync<CashSessionDto>())!;
        closed.ExpectedBalance.Should().Be(140m);
        closed.ClosingBalance.Should().Be(135m);
        closed.Difference.Should().Be(-5m);
        closed.ClosingNotes.Should().Be("Faltou troco de 5");

        var history = (await c.GetFromJsonAsync<List<CashSessionDto>>("/api/cash/sessions"))!;
        history.Should().Contain(h => h.Id == session.Id && h.Difference == -5m && h.ExpectedBalance == 140m);

        // closed is closed
        (await c.PostAsJsonAsync($"/api/cash/sessions/{session.Id}/movements",
                new AddCashMovementRequest("Deposit", 10m, "tarde demais")))
            .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await c.PostAsJsonAsync($"/api/cash/sessions/{session.Id}/close", new CloseCashSessionRequest(135m)))
            .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Theory]
    [InlineData("SaleReceipt", 10, "forjando venda")]
    [InlineData("Opening", 10, "x")]
    [InlineData("SaleRefund", 10, "x")]
    [InlineData("Deposit", 0, "zero")]
    [InlineData("Deposit", 10.005, "centavo quebrado")]
    [InlineData("Deposit", 10, "")]
    public async Task Only_valid_suprimento_or_sangria_can_be_recorded_by_hand(string type, decimal amount, string reason)
    {
        var c = await FreshManagerAsync($"cash2.manual.{type.ToLowerInvariant()}.{amount}".Replace(",", "."));
        var session = await OpenAsync(c, 10m);

        var resp = await c.PostAsJsonAsync($"/api/cash/sessions/{session.Id}/movements",
            new AddCashMovementRequest(type, amount, reason));

        resp.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        await CloseAsync(c, session.Id);
    }

    [Fact]
    public async Task A_sangria_cannot_take_more_than_the_drawer_holds()
    {
        var c = await FreshManagerAsync("cash2.sangria");
        var session = await OpenAsync(c, 40m);

        (await c.PostAsJsonAsync($"/api/cash/sessions/{session.Id}/movements",
                new AddCashMovementRequest("Withdrawal", 40.01m, "mais do que tem")))
            .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        await MoveOkAsync(c, session.Id, "Withdrawal", 40m, "tudo");
        await CloseAsync(c, session.Id);
    }

    [Fact]
    public async Task Two_simultaneous_opens_by_the_same_user_create_one_drawer()
    {
        var c = await FreshManagerAsync("cash2.race");
        var second = _factory.CreateApiClient();
        second.DefaultRequestHeaders.Authorization = c.DefaultRequestHeaders.Authorization;

        var responses = await Task.WhenAll(
            c.PostAsJsonAsync("/api/cash/sessions/open", new OpenCashSessionRequest(0m)),
            second.PostAsJsonAsync("/api/cash/sessions/open", new OpenCashSessionRequest(0m)));

        responses.Select(r => r.StatusCode).Should().BeEquivalentTo(
            [HttpStatusCode.Created, HttpStatusCode.Conflict]);
        var created = responses.Single(r => r.StatusCode == HttpStatusCode.Created);
        await CloseAsync(c, (await created.Content.ReadFromJsonAsync<CashSessionDto>())!.Id);
    }

    [Fact]
    public async Task Cancelling_a_sale_records_a_refund_not_a_sangria()
    {
        var c = await FreshManagerAsync("cash2.refund");
        var session = await OpenAsync(c, 0m);
        var saleId = await SellAsync(c, session.Id, 30m,
            new PaymentInput("Cash", "Cash", 10m),
            new PaymentInput("Credit", "Credit", 20m, DateTime.UtcNow.AddDays(10)));

        (await c.PostAsJsonAsync($"/api/sales/{saleId}/cancel", new { })).EnsureSuccessStatusCode();

        var detail = (await c.GetFromJsonAsync<CashSessionDto>($"/api/cash/sessions/{session.Id}"))!;
        detail.Movements!.Should().Contain(m => m.MovementType == "SaleRefund" && m.Amount == 10m);
        detail.Summary!.Withdrawals.Should().Be(0m);
        detail.Summary.Refunds.Should().Be(10m);
        detail.Summary.ExpectedBalance.Should().Be(0m);
        await CloseAsync(c, session.Id);
    }

    [Fact]
    public async Task A_refund_cannot_take_more_cash_than_the_drawer_holds()
    {
        var c = await FreshManagerAsync("cash2.refund.empty");
        var session = await OpenAsync(c, 0m);
        var saleId = await SellAsync(c, session.Id, 30m,
            new PaymentInput("Cash", "Cash", 10m),
            new PaymentInput("Credit", "Credit", 20m, DateTime.UtcNow.AddDays(10)));
        await MoveOkAsync(c, session.Id, "Withdrawal", 10m, "levou tudo ao cofre");

        var cancel = await c.PostAsJsonAsync($"/api/sales/{saleId}/cancel", new { });

        cancel.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await c.GetFromJsonAsync<SaleDto>($"/api/sales/{saleId}"))!.Status.Should().Be("Confirmed");
        await CloseAsync(c, session.Id);
    }

    [Fact]
    public async Task Closing_notes_are_limited_and_the_open_check_stays_light()
    {
        var c = await FreshManagerAsync("cash2.notes");
        var session = await OpenAsync(c, 0m);

        var open = (await c.GetFromJsonAsync<CashSessionDto>("/api/cash/sessions/open"))!;
        open.Summary.Should().BeNull();   // polled by PDV/comanda: no movements, no aggregation

        (await c.PostAsJsonAsync($"/api/cash/sessions/{session.Id}/close",
                new CloseCashSessionRequest(0m, new string('x', 501))))
            .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        await CloseAsync(c, session.Id);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static async Task<CashSessionDto> OpenAsync(HttpClient c, decimal opening)
    {
        var resp = await c.PostAsJsonAsync("/api/cash/sessions/open", new OpenCashSessionRequest(opening, "PR44"));
        resp.StatusCode.Should().Be(HttpStatusCode.Created, await resp.Content.ReadAsStringAsync());
        return (await resp.Content.ReadFromJsonAsync<CashSessionDto>())!;
    }

    private static async Task CloseAsync(HttpClient c, Guid sessionId)
    {
        var detail = (await c.GetFromJsonAsync<CashSessionDto>($"/api/cash/sessions/{sessionId}"))!;
        (await c.PostAsJsonAsync($"/api/cash/sessions/{sessionId}/close",
            new CloseCashSessionRequest(detail.Summary!.ExpectedBalance))).EnsureSuccessStatusCode();
    }

    private static async Task MoveOkAsync(HttpClient c, Guid sessionId, string type, decimal amount, string reason)
    {
        var resp = await c.PostAsJsonAsync($"/api/cash/sessions/{sessionId}/movements",
            new AddCashMovementRequest(type, amount, reason));
        resp.StatusCode.Should().Be(HttpStatusCode.OK, await resp.Content.ReadAsStringAsync());
    }

    private static async Task<Guid> SellAsync(HttpClient c, Guid sessionId, decimal price, params PaymentInput[] payments)
    {
        var code = $"PR44-{Guid.NewGuid():N}"[..20];
        var product = (await (await c.PostAsJsonAsync("/api/products",
                new CreateProductRequest(Code: code, Name: code, Unit: "Un", SalePrice: price, CostPrice: 1m, TrackStock: true)))
            .Content.ReadFromJsonAsync<ProductDto>())!;
        (await c.PostAsJsonAsync("/api/stock/adjust",
            new AdjustStockRequest(product.Id, 5, "ManualEntry", "PR44"))).EnsureSuccessStatusCode();
        var sale = (await (await c.PostAsJsonAsync("/api/sales", new CreateSaleRequest(CashSessionId: sessionId)))
            .Content.ReadFromJsonAsync<SaleDto>())!;
        (await c.PostAsJsonAsync($"/api/sales/{sale.Id}/items",
            new AddSaleItemRequest(product.Id, 1, price))).EnsureSuccessStatusCode();
        var confirm = await c.PostAsJsonAsync($"/api/sales/{sale.Id}/confirm", new ConfirmSaleRequest([.. payments]));
        confirm.StatusCode.Should().Be(HttpStatusCode.OK, await confirm.Content.ReadAsStringAsync());
        return sale.Id;
    }

    private async Task<HttpClient> FreshManagerAsync(string login)
    {
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
            if (!await db.Users.IgnoreQueryFilters().AnyAsync(u => u.Login == login))
            {
                var admin = await db.Users.IgnoreQueryFilters().FirstAsync(u => u.Login == TestCredentials.AdminLogin);
                var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
                db.Users.Add(User.Create(
                    tenantId: admin.TenantId, fullName: $"Caixa {login}",
                    email: $"{login}@{TestCredentials.TestLocalDomain}", login: login,
                    passwordHash: hasher.Hash(TestCredentials.SameTenantManagerPassword), role: UserRole.Gerente));
                await db.SaveChangesAsync();
            }
        }
        return await AuthClientFactory.LoginAsync(_factory, login, TestCredentials.SameTenantManagerPassword);
    }
}
