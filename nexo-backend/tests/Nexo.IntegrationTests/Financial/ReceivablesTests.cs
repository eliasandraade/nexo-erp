using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nexo.Application.Common;
using Nexo.Application.Common.Interfaces;
using Nexo.Application.Features.Cash;
using Nexo.Application.Features.Customers;
using Nexo.Application.Features.Financial;
using Nexo.Application.Features.Products;
using Nexo.Application.Features.Sales;
using Nexo.Domain.Entities;
using Nexo.Domain.Enums;
using Nexo.Infrastructure.Jobs;
using Nexo.Infrastructure.Persistence;
using Nexo.IntegrationTests.Common;
using Nexo.IntegrationTests.Helpers;
using Xunit;

namespace Nexo.IntegrationTests.Financial;

/// <summary>
/// Contas a receber (PR 50): baixa total/parcial with juros, multa and desconto, parcelamento,
/// títulos vencidos and the aging, customer on every título.
/// </summary>
[Collection("Integration")]
public class ReceivablesTests
{
    private const string Fin = "/api/financial";
    private readonly TestWebApplicationFactory _factory;
    public ReceivablesTests(TestWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task A_partial_baixa_splits_the_titulo_and_the_cash_regime_sees_only_what_came_in()
    {
        var since = DateTime.UtcNow.AddSeconds(-5);
        var c = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var customer = await CustomerAsync(c);
        var titulo = (await InstallmentsAsync(c, 100m, 1, DateTime.UtcNow.AddDays(10), customer)).Single();
        titulo.CustomerId.Should().Be(customer);

        var result = await SettleOkAsync(c, titulo.Id, new SettleTransactionRequest(40m));

        result.Settled.Status.Should().Be("Paid");
        result.Settled.Amount.Should().Be(40m);
        result.Remainder.Should().NotBeNull();
        result.Remainder!.Amount.Should().Be(60m);
        result.Remainder.Status.Should().Be("Pending");
        result.Remainder.ParentTransactionId.Should().Be(titulo.Id);
        result.Remainder.CustomerId.Should().Be(customer);
        result.Adjustments.Should().BeEmpty();

        var open = await TitlesAsync(c, $"status=open&customerId={customer}");
        open.Items.Should().ContainSingle(t => t.Id == result.Remainder.Id).Which.CustomerName.Should().StartWith("Cliente");
        open.Items.Should().NotContain(t => t.Id == titulo.Id);

        var cash = await c.GetFromJsonAsync<LedgerDto>(
            $"{Fin}/ledger?regime=cash&from={Uri.EscapeDataString(since.ToString("O"))}&to={Uri.EscapeDataString(DateTime.UtcNow.AddMinutes(1).ToString("O"))}");
        cash!.Entries.Should().Contain(e => e.Id == titulo.Id && e.Amount == 40m);
        cash.Entries.Should().NotContain(e => e.Id == result.Remainder.Id);
    }

    [Fact]
    public async Task Installments_add_up_to_the_total_and_baixa_records_interest_and_discount_apart()
    {
        var c = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var parcels = await InstallmentsAsync(c, 100m, 3, DateTime.UtcNow.AddDays(5), null);
        parcels.Select(p => p.Amount).Should().Equal(33.33m, 33.33m, 33.34m);
        parcels.Select(p => p.Description).Should().OnlyContain(d => d.Contains("/3)"));
        (parcels[1].DueDate - parcels[0].DueDate).TotalDays.Should().BeInRange(28, 31);   // monthly

        // late: R$ 33,33 + R$ 1,00 juros + R$ 0,50 multa
        var late = await SettleOkAsync(c, parcels[0].Id, new SettleTransactionRequest(33.33m, Interest: 1m, Fine: 0.5m));
        late.Remainder.Should().BeNull();
        var charge = late.Adjustments.Should().ContainSingle().Subject;
        charge.Category.Should().Be("FinancialIncome");
        charge.TransactionType.Should().Be("Receivable");
        charge.Amount.Should().Be(1.5m);
        charge.Status.Should().Be("Paid");

        // early: R$ 30,00 in money + R$ 3,33 desconto settles the whole parcela
        var early = await SettleOkAsync(c, parcels[1].Id, new SettleTransactionRequest(30m, Discount: 3.33m));
        early.Remainder.Should().BeNull();
        var discount = early.Adjustments.Should().ContainSingle().Subject;
        discount.Category.Should().Be("FinancialExpense");
        discount.TransactionType.Should().Be("Payable");
        discount.Amount.Should().Be(3.33m);
        discount.Status.Should().Be("Abated");               // no money moved: competência only
        early.Settled.Amount.Should().Be(30m);               // the money part
        early.Settled.Status.Should().Be("Paid");

        // more than the balance, negative values, or settling twice are refused
        (await SettleAsync(c, parcels[2].Id, new SettleTransactionRequest(40m)))
            .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await SettleAsync(c, parcels[2].Id, new SettleTransactionRequest(-1m)))
            .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await SettleAsync(c, parcels[0].Id, new SettleTransactionRequest(1m)))
            .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        // adjustments are module-owned: not editable or cancellable by hand
        (await c.PostAsJsonAsync($"{Fin}/transactions/{charge.Id}/cancel", new { }))
            .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Late_titulos_show_as_overdue_in_the_aging_and_the_job_flags_them()
    {
        var c = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var customer = await CustomerAsync(c);
        var late = (await InstallmentsAsync(c, 70m, 1, DateTime.UtcNow.AddDays(-40), customer)).Single();
        var current = (await InstallmentsAsync(c, 30m, 1, DateTime.UtcNow.AddDays(3), customer)).Single();

        var overdue = await TitlesAsync(c, $"status=overdue&customerId={customer}");
        var item = overdue.Items.Should().ContainSingle().Subject;
        item.Id.Should().Be(late.Id);
        item.Status.Should().Be("Overdue");
        item.DaysOverdue.Should().BeInRange(39, 41);
        overdue.Aging.Overdue31To60.Should().Be(70m);
        overdue.Aging.NotDue.Should().Be(30m);
        overdue.Aging.TotalOpen.Should().Be(100m);
        overdue.Aging.OverdueCount.Should().Be(1);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
            (await OverdueTitlesJob.MarkOverdueAsync(db, BusinessClock.TodayStartUtc())).Should().BeGreaterThan(0);
            (await db.FinancialTransactions.IgnoreQueryFilters().SingleAsync(t => t.Id == late.Id))
                .Status.Should().Be(TransactionStatus.Overdue);
            (await db.FinancialTransactions.IgnoreQueryFilters().SingleAsync(t => t.Id == current.Id))
                .Status.Should().Be(TransactionStatus.Pending);
        }

        // renegotiated to a date ahead: no longer overdue
        (await c.PutAsJsonAsync($"{Fin}/transactions/{late.Id}",
            new UpdateTransactionRequest(70m, late.Description, DateTime.UtcNow.AddDays(15)))).EnsureSuccessStatusCode();
        (await TitlesAsync(c, $"status=overdue&customerId={customer}")).Items.Should().BeEmpty();
    }

    [Fact]
    public async Task Cancelling_a_credit_sale_cancels_the_open_remainder_and_reverses_what_was_received()
    {
        var c = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var customer = await CustomerAsync(c);
        var sale = await CreditSaleAsync(c, customer, 80m);
        var receivable = (await TitlesAsync(c, $"status=open&customerId={customer}")).Items.Should().ContainSingle().Subject;
        receivable.ReferenceId.Should().Be(sale.Id);

        var part = await SettleOkAsync(c, receivable.Id, new SettleTransactionRequest(30m));
        (await c.PostAsJsonAsync($"/api/sales/{sale.Id}/cancel", new { reason = "Cliente desistiu" })).EnsureSuccessStatusCode();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        (await db.FinancialTransactions.IgnoreQueryFilters().SingleAsync(t => t.Id == part.Remainder!.Id))
            .Status.Should().Be(TransactionStatus.Cancelled);
        var reversal = await db.FinancialTransactions.IgnoreQueryFilters()
            .SingleAsync(t => t.ReversesTransactionId == receivable.Id);
        reversal.Amount.Should().Be(30m);
        reversal.Category.Should().Be(FinancialCategory.RevenueDeduction);
        reversal.CustomerId.Should().Be(customer);
    }

    [Fact]
    public async Task A_discounted_baixa_then_cancelling_the_sale_nets_to_zero_in_both_regimes()
    {
        var c = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var customer = await CustomerAsync(c);
        var sale = await CreditSaleAsync(c, customer, 100m);
        var receivable = (await TitlesAsync(c, $"status=open&customerId={customer}")).Items.Single();

        await SettleOkAsync(c, receivable.Id, new SettleTransactionRequest(90m, Discount: 10m));
        (await c.PostAsJsonAsync($"/api/sales/{sale.Id}/cancel", new { reason = "Devolvido" })).EnsureSuccessStatusCode();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        var ids = await db.FinancialTransactions.IgnoreQueryFilters()
            .Where(t => (t.ReferenceType == "Sale" || t.ReferenceType == "SaleCancellation") && t.ReferenceId == sale.Id)
            .Select(t => t.Id).ToListAsync();
        var all = await db.FinancialTransactions.IgnoreQueryFilters()
            .Where(t => ids.Contains(t.Id) || (t.ReferenceType == FinancialTransaction.SettlementReference && ids.Contains(t.ReferenceId!.Value)))
            .ToListAsync();
        static decimal Signed(FinancialTransaction t) => t.TransactionType == TransactionType.Receivable ? t.Amount : -t.Amount;

        all.Where(t => t.Status == TransactionStatus.Paid).Sum(Signed).Should().Be(0m);              // caixa: 90 in, 90 back
        all.Where(t => t.Status != TransactionStatus.Cancelled).Sum(Signed).Should().Be(0m);         // competência
        all.Where(t => t.Status == TransactionStatus.Paid && t.Category == FinancialCategory.Revenue)
            .Sum(t => t.Amount).Should().Be(90m);                                                    // receitas recebidas = money only
    }

    [Fact]
    public async Task Invalid_installment_plans_are_rejected()
    {
        var c = await AuthClientFactory.LoginAsAdminAsync(_factory);
        foreach (var bad in new[]
                 {
                     new CreateInstallmentsRequest("Receivable", 100m, "x", DateTime.UtcNow, Installments: 0),
                     new CreateInstallmentsRequest("Receivable", 100m, "x", DateTime.UtcNow, Installments: 61),
                     new CreateInstallmentsRequest("Receivable", 0.02m, "x", DateTime.UtcNow, Installments: 3),
                     new CreateInstallmentsRequest("Receivable", 10.001m, "x", DateTime.UtcNow),
                     new CreateInstallmentsRequest("Receivable", 10m, "x", DateTime.UtcNow, Category: "Tax"),
                 })
            (await c.PostAsJsonAsync($"{Fin}/transactions/installments", bad))
                .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await c.PostAsJsonAsync($"{Fin}/transactions/installments",
                new CreateInstallmentsRequest("Receivable", 10m, "x", DateTime.UtcNow, CustomerId: Guid.NewGuid())))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await c.GetAsync($"{Fin}/receivables?status=lost")).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var payable = (await c.GetFromJsonAsync<List<FinancialAccountDto>>($"{Fin}/accounts"))!.First(a => a.AccountType == "Payable");
        (await c.PostAsJsonAsync($"{Fin}/transactions/installments",
                new CreateInstallmentsRequest("Receivable", 10m, "x", DateTime.UtcNow, FinancialAccountId: payable.Id)))
            .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);   // a receivable can't land on Contas a Pagar
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static async Task<T> OkAsync<T>(HttpResponseMessage resp)
    {
        resp.IsSuccessStatusCode.Should().BeTrue(await resp.Content.ReadAsStringAsync());
        return (await resp.Content.ReadFromJsonAsync<T>())!;
    }

    private static Task<HttpResponseMessage> SettleAsync(HttpClient c, Guid id, SettleTransactionRequest r)
        => c.PostAsJsonAsync($"{Fin}/transactions/{id}/settle", r);

    private static async Task<SettlementResultDto> SettleOkAsync(HttpClient c, Guid id, SettleTransactionRequest r)
        => await OkAsync<SettlementResultDto>(await SettleAsync(c, id, r));

    private static async Task<TitlesDto> TitlesAsync(HttpClient c, string query)
        => await OkAsync<TitlesDto>(await c.GetAsync($"{Fin}/receivables?{query}"));

    private static async Task<List<FinancialTransactionDto>> InstallmentsAsync(
        HttpClient c, decimal total, int n, DateTime firstDue, Guid? customer)
        => await OkAsync<List<FinancialTransactionDto>>(await c.PostAsJsonAsync($"{Fin}/transactions/installments",
            new CreateInstallmentsRequest("Receivable", total, "Mensalidade PR50", firstDue, n, CustomerId: customer)));

    private static async Task<Guid> CustomerAsync(HttpClient c)
    {
        var doc = Guid.NewGuid().ToString("N")[..12];
        return (await OkAsync<CustomerDto>(await c.PostAsJsonAsync("/api/customers",
            new CreateCustomerRequest("Individual", $"Cliente {doc}", "Other", doc)))).Id;
    }

    private static async Task<SaleDto> CreditSaleAsync(HttpClient c, Guid customer, decimal price)
    {
        var code = $"PR50-{Guid.NewGuid():N}"[..20];
        var product = await OkAsync<ProductDto>(await c.PostAsJsonAsync("/api/products",
            new CreateProductRequest(Code: code, Name: code, Unit: "Un", SalePrice: price, CostPrice: 1m, TrackStock: false)));
        var draft = await OkAsync<SaleDto>(await c.PostAsJsonAsync("/api/sales", new CreateSaleRequest(CustomerId: customer)));
        (await c.PostAsJsonAsync($"/api/sales/{draft.Id}/items", new AddSaleItemRequest(product.Id, 1, price))).EnsureSuccessStatusCode();
        return await OkAsync<SaleDto>(await c.PostAsJsonAsync($"/api/sales/{draft.Id}/confirm", new ConfirmSaleRequest(
            [new PaymentInput("Other", "Credit", price, DateTime.UtcNow.AddDays(30))])));
    }
}
