using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nexo.Application.Common.Interfaces;
using Nexo.Application.Features.Cash;
using Nexo.Application.Features.Financial;
using Nexo.Application.Features.Products;
using Nexo.Application.Features.Sales;
using Nexo.Application.Features.Stock;
using Nexo.Domain.Entities;
using Nexo.Domain.Enums;
using Nexo.Infrastructure.Persistence;
using Nexo.IntegrationTests.Common;
using Nexo.IntegrationTests.Helpers;
using Xunit;

namespace Nexo.IntegrationTests.Financial;

/// <summary>
/// Livro financeiro unificado (PR 49): every sale's money — cash included — lands in the ledger with
/// store, category and competência; estornos are revenue deductions; the two regimes read the same
/// lançamentos; settled entries are never cancelled or edited.
/// </summary>
[Collection("Integration")]
public class FinancialLedgerTests
{
    private const string Ledger = "/api/financial/ledger";
    private readonly TestWebApplicationFactory _factory;
    public FinancialLedgerTests(TestWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task A_cash_sale_and_its_cash_refund_are_in_the_ledger_as_revenue_and_deduction()
    {
        var since = DateTime.UtcNow.AddSeconds(-5);
        var (c, session) = await ManagerWithDrawerAsync("ledger.cash", opening: 50m);
        var sale = await PaidSaleAsync(c, session.Id, unitPrice: 40m, [new PaymentInput("Cash", "Cash", 40m)]);

        var revenue = (await EntriesAsync("Sale", sale.Id)).Should().ContainSingle().Subject;
        revenue.Category.Should().Be(FinancialCategory.Revenue);
        revenue.TransactionType.Should().Be(TransactionType.Receivable);
        revenue.Status.Should().Be(TransactionStatus.Paid);
        revenue.Amount.Should().Be(40m);
        revenue.StoreId.Should().NotBeNull();

        var ret = await c.PostAsJsonAsync($"/api/sales/{sale.Id}/returns", new CreateSaleReturnRequest(
            [new SaleReturnItemInput(sale.Items[0].Id, 1)], "Cash", "Defeito", null));
        ret.StatusCode.Should().Be(HttpStatusCode.OK, await ret.Content.ReadAsStringAsync());
        var saleReturn = (await ret.Content.ReadFromJsonAsync<SaleReturnDto>())!;

        var deduction = (await EntriesAsync("SaleReturn", saleReturn.Id)).Should().ContainSingle().Subject;
        deduction.Category.Should().Be(FinancialCategory.RevenueDeduction);
        deduction.TransactionType.Should().Be(TransactionType.Payable);
        deduction.Amount.Should().Be(40m);
        deduction.StoreId.Should().Be(revenue.StoreId);
        (await DrawerAsync(c, session.Id)).Summary!.Refunds.Should().Be(40m);   // the gaveta still sees it

        var ledger = await LedgerAsync(c, "cash", since);
        ledger.Entries.Select(e => e.Id).Should().Contain([revenue.Id, deduction.Id]);
        ledger.ByCategory.Single(x => x.Category == "Revenue").In.Should().BeGreaterThanOrEqualTo(40m);
        ledger.ByCategory.Single(x => x.Category == "RevenueDeduction").Out.Should().BeGreaterThanOrEqualTo(40m);
        ledger.Net.Should().Be(ledger.TotalIn - ledger.TotalOut);

        await CloseAsync(c, session.Id);
    }

    [Fact]
    public async Task A_credit_sale_counts_on_accrual_now_and_on_cash_only_once_paid()
    {
        var since = DateTime.UtcNow.AddSeconds(-5);
        var (c, session) = await ManagerWithDrawerAsync("ledger.credit", opening: 0m);
        var sale = await PaidSaleAsync(c, session.Id, unitPrice: 30m,
            [new PaymentInput("Other", "Credit", 30m, DateTime.UtcNow.AddDays(30))]);
        var receivable = (await EntriesAsync("Sale", sale.Id)).Should().ContainSingle().Subject;
        receivable.Status.Should().Be(TransactionStatus.Pending);

        (await LedgerAsync(c, "accrual", since)).Entries.Should().Contain(e => e.Id == receivable.Id);
        (await LedgerAsync(c, "cash", since)).Entries.Should().NotContain(e => e.Id == receivable.Id);

        // Receiving a sale's receivable is allowed; cancelling it is not (cancel the sale)
        (await c.PostAsJsonAsync($"/api/financial/transactions/{receivable.Id}/cancel", new { }))
            .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await c.PostAsJsonAsync($"/api/financial/transactions/{receivable.Id}/pay", new MarkTransactionPaidRequest()))
            .EnsureSuccessStatusCode();
        (await LedgerAsync(c, "cash", since)).Entries.Should().Contain(e => e.Id == receivable.Id);

        await CloseAsync(c, session.Id);
    }

    [Fact]
    public async Task Settled_entries_are_neither_paid_twice_nor_cancelled_nor_edited()
    {
        var c = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var account = await PayableAccountAsync(c);
        var created = await c.PostAsJsonAsync("/api/financial/transactions", new CreateTransactionRequest(
            account, "Payable", 120m, "Energia PR49", DateTime.UtcNow, Category: "OperatingExpense"));
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var tx = (await created.Content.ReadFromJsonAsync<FinancialTransactionDto>())!;
        tx.Category.Should().Be("OperatingExpense");

        (await c.PostAsJsonAsync($"/api/financial/transactions/{tx.Id}/pay", new MarkTransactionPaidRequest()))
            .EnsureSuccessStatusCode();
        (await c.PostAsJsonAsync($"/api/financial/transactions/{tx.Id}/pay", new MarkTransactionPaidRequest()))
            .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await c.PostAsJsonAsync($"/api/financial/transactions/{tx.Id}/cancel", new { }))
            .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await c.PutAsJsonAsync($"/api/financial/transactions/{tx.Id}",
            new UpdateTransactionRequest(1m, "Editado", DateTime.UtcNow)))
            .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Theory]
    [InlineData("Payable", "Revenue", "100", null)]          // revenue is an inflow
    [InlineData("Receivable", "Tax", "100", null)]           // a tax is an outflow
    [InlineData("Payable", "OperatingExpense", "0", null)]   // nothing to record
    [InlineData("Payable", "OperatingExpense", "10", "Sale")] // module references can't be forged
    [InlineData("Payable", "Nonsense", "10", null)]
    public async Task Invalid_manual_entries_are_rejected(string type, string category, string amount, string? referenceType)
    {
        var c = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var account = await PayableAccountAsync(c);
        (await c.PostAsJsonAsync("/api/financial/transactions", new CreateTransactionRequest(
            account, type, decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture), "PR49 inválido",
            DateTime.UtcNow, ReferenceType: referenceType, ReferenceId: referenceType is null ? null : Guid.NewGuid(),
            Category: category)))
            .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task The_ledger_rejects_an_unknown_regime()
    {
        var c = await AuthClientFactory.LoginAsAdminAsync(_factory);
        (await c.GetAsync($"{Ledger}?regime=lunar")).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static async Task<LedgerDto> LedgerAsync(HttpClient c, string regime, DateTime since)
    {
        var from = Uri.EscapeDataString(since.ToString("O"));
        var to = Uri.EscapeDataString(DateTime.UtcNow.AddMinutes(1).ToString("O"));
        var resp = await c.GetAsync($"{Ledger}?regime={regime}&from={from}&to={to}");
        resp.StatusCode.Should().Be(HttpStatusCode.OK, await resp.Content.ReadAsStringAsync());
        return (await resp.Content.ReadFromJsonAsync<LedgerDto>())!;
    }

    private static async Task<Guid> PayableAccountAsync(HttpClient c)
        => (await c.GetFromJsonAsync<List<FinancialAccountDto>>("/api/financial/accounts"))!
            .First(a => a.AccountType == "Payable" && a.IsActive).Id;

    private async Task<List<FinancialTransaction>> EntriesAsync(string referenceType, Guid referenceId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        return await db.FinancialTransactions.IgnoreQueryFilters()
            .Where(t => t.ReferenceType == referenceType && t.ReferenceId == referenceId).ToListAsync();
    }

    private static async Task<SaleDto> PaidSaleAsync(HttpClient c, Guid sessionId, decimal unitPrice, PaymentInput[] payments)
    {
        var code = $"PR49-{Guid.NewGuid():N}"[..20];
        var product = (await (await c.PostAsJsonAsync("/api/products",
                new CreateProductRequest(Code: code, Name: code, Unit: "Un", SalePrice: unitPrice, CostPrice: 1m, TrackStock: true)))
            .Content.ReadFromJsonAsync<ProductDto>())!;
        (await c.PostAsJsonAsync("/api/stock/adjust",
            new AdjustStockRequest(product.Id, 10, "ManualEntry", "PR49"))).EnsureSuccessStatusCode();
        var draft = (await (await c.PostAsJsonAsync("/api/sales", new CreateSaleRequest(CashSessionId: sessionId)))
            .Content.ReadFromJsonAsync<SaleDto>())!;
        (await c.PostAsJsonAsync($"/api/sales/{draft.Id}/items",
            new AddSaleItemRequest(product.Id, 1, unitPrice))).EnsureSuccessStatusCode();
        var confirm = await c.PostAsJsonAsync($"/api/sales/{draft.Id}/confirm", new ConfirmSaleRequest(payments.ToList()));
        confirm.StatusCode.Should().Be(HttpStatusCode.OK, await confirm.Content.ReadAsStringAsync());
        return (await confirm.Content.ReadFromJsonAsync<SaleDto>())!;
    }

    private static async Task<CashSessionDto> DrawerAsync(HttpClient c, Guid sessionId)
        => (await c.GetFromJsonAsync<CashSessionDto>($"/api/cash/sessions/{sessionId}"))!;

    private static async Task CloseAsync(HttpClient c, Guid sessionId)
    {
        var detail = await DrawerAsync(c, sessionId);
        (await c.PostAsJsonAsync($"/api/cash/sessions/{sessionId}/close",
            new CloseCashSessionRequest(detail.Summary!.ExpectedBalance))).EnsureSuccessStatusCode();
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
                    tenantId: admin.TenantId, fullName: $"Livro {login}",
                    email: $"{login}@{TestCredentials.TestLocalDomain}", login: login,
                    passwordHash: hasher.Hash(TestCredentials.SameTenantManagerPassword), role: UserRole.Gerente));
                await db.SaveChangesAsync();
            }
        }
        var client = await AuthClientFactory.LoginAsync(_factory, login, TestCredentials.SameTenantManagerPassword);
        var open = await client.PostAsJsonAsync("/api/cash/sessions/open", new OpenCashSessionRequest(opening, "PR49"));
        open.StatusCode.Should().Be(HttpStatusCode.Created, await open.Content.ReadAsStringAsync());
        return (client, (await open.Content.ReadFromJsonAsync<CashSessionDto>())!);
    }
}
