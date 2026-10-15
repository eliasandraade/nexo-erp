using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Nexo.Application.Features.Financial;
using Nexo.Application.Features.Suppliers;
using Nexo.IntegrationTests.Common;
using Nexo.IntegrationTests.Helpers;
using Xunit;

namespace Nexo.IntegrationTests.Financial;

/// <summary>
/// Contas a pagar (PR 51): fornecedor no título, baixa com juros/multa (despesa financeira) e desconto
/// obtido (receita financeira, abatida), e lançamentos recorrentes gerados mês a mês.
/// </summary>
[Collection("Integration")]
public class PayablesTests
{
    private const string Fin = "/api/financial";
    private readonly TestWebApplicationFactory _factory;
    public PayablesTests(TestWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task A_supplier_bill_is_listed_with_the_supplier_and_paid_late_with_charges()
    {
        var c = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var supplier = await SupplierAsync(c);
        var bill = (await OkAsync<List<FinancialTransactionDto>>(await c.PostAsJsonAsync($"{Fin}/transactions/installments",
            new CreateInstallmentsRequest("Payable", 500m, "Energia PR51", DateTime.UtcNow.AddDays(-3),
                Category: "OperatingExpense", SupplierId: supplier)))).Single();
        bill.SupplierId.Should().Be(supplier);

        var listed = await OkAsync<TitlesDto>(await c.GetAsync($"{Fin}/payables?status=overdue&supplierId={supplier}"));
        var item = listed.Items.Should().ContainSingle().Subject;
        item.SupplierName.Should().StartWith("Fornecedor");
        item.DaysOverdue.Should().BeInRange(2, 4);

        var paid = await OkAsync<SettlementResultDto>(await c.PostAsJsonAsync($"{Fin}/transactions/{bill.Id}/settle",
            new SettleTransactionRequest(480m, Interest: 3m, Fine: 10m, Discount: 20m)));
        paid.Settled.Amount.Should().Be(480m);
        paid.Remainder.Should().BeNull();
        paid.Adjustments.Should().HaveCount(2);
        var charges = paid.Adjustments.Single(a => a.TransactionType == "Payable");
        charges.Category.Should().Be("FinancialExpense");
        charges.Amount.Should().Be(13m);
        charges.Status.Should().Be("Paid");
        var discount = paid.Adjustments.Single(a => a.TransactionType == "Receivable");
        discount.Category.Should().Be("FinancialIncome");
        discount.Status.Should().Be("Abated");
        discount.SupplierId.Should().Be(supplier);
    }

    [Fact]
    public async Task A_recurrence_generates_one_titulo_per_month_through_next_month_once()
    {
        var c = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var supplier = await SupplierAsync(c);
        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(-3));   // Brazil calendar
        var start = new DateOnly(today.Year, today.Month, 1).AddMonths(-1);

        var recurrence = await OkAsync<RecurrenceDto>(await c.PostAsJsonAsync($"{Fin}/recurrences",
            new CreateRecurrenceRequest("Payable", "Aluguel PR51", 1800m, 31, start, Category: "OperatingExpense", SupplierId: supplier)));
        recurrence.SupplierName.Should().StartWith("Fornecedor");
        recurrence.GeneratedThrough.Should().Be(start.AddMonths(2));            // last month, this month, next month

        async Task<List<TitleDto>> Rent() => (await OkAsync<TitlesDto>(
            await c.GetAsync($"{Fin}/payables?status=all&supplierId={supplier}"))).Items.ToList();

        var titles = await Rent();
        titles.Should().HaveCount(3);
        titles.Should().OnlyContain(t => t.Amount == 1800m && t.ReferenceType == "Recurrence" && t.SupplierId == supplier);
        titles.Select(t => t.DueDate.AddHours(-3).Day)                            // Brazil day
            .Should().OnlyContain(d => d >= 28);                                  // day 31, or the month's last day
        (await Rent()).Should().HaveCount(3);                                     // reading again generates nothing

        // a generated título is an ordinary one: it can be cancelled on its own
        var first = titles.OrderBy(t => t.DueDate).First();
        (await c.PostAsJsonAsync($"{Fin}/transactions/{first.Id}/cancel", new { })).EnsureSuccessStatusCode();

        var stopped = await OkAsync<RecurrenceDto>(await c.PostAsync($"{Fin}/recurrences/{recurrence.Id}/deactivate", null));
        stopped.IsActive.Should().BeFalse();
        (await OkAsync<List<RecurrenceDto>>(await c.GetAsync($"{Fin}/recurrences"))).Should().NotContain(r => r.Id == recurrence.Id);
    }

    [Fact]
    public async Task Invalid_recurrences_are_rejected()
    {
        var c = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var month = new DateOnly(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);
        foreach (var bad in new[]
                 {
                     new CreateRecurrenceRequest("Payable", "x", 100m, 0, month),
                     new CreateRecurrenceRequest("Payable", "x", 100m, 32, month),
                     new CreateRecurrenceRequest("Payable", "x", 0m, 10, month),
                     new CreateRecurrenceRequest("Payable", "x", 10.001m, 10, month),
                     new CreateRecurrenceRequest("Payable", "x", 100m, 10, month, EndMonth: month.AddMonths(-1)),
                     new CreateRecurrenceRequest("Payable", "x", 100m, 10, month, Category: "Revenue"),
                     new CreateRecurrenceRequest("Lunar", "x", 100m, 10, month),
                 })
            (await c.PostAsJsonAsync($"{Fin}/recurrences", bad)).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await c.PostAsJsonAsync($"{Fin}/recurrences",
                new CreateRecurrenceRequest("Payable", "x", 100m, 10, month, SupplierId: Guid.NewGuid())))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private static async Task<T> OkAsync<T>(HttpResponseMessage resp)
    {
        resp.IsSuccessStatusCode.Should().BeTrue(await resp.Content.ReadAsStringAsync());
        return (await resp.Content.ReadFromJsonAsync<T>())!;
    }

    private static async Task<Guid> SupplierAsync(HttpClient c)
    {
        var doc = Guid.NewGuid().ToString("N")[..12];
        return (await OkAsync<SupplierDto>(await c.PostAsJsonAsync("/api/suppliers",
            new CreateSupplierRequest("Company", $"Fornecedor {doc}", "Other", doc)))).Id;
    }
}
