using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Nexo.Application.Common;
using Nexo.Application.Features.Financial;
using Nexo.IntegrationTests.Common;
using Nexo.IntegrationTests.Helpers;
using Xunit;

namespace Nexo.IntegrationTests.Financial;

/// <summary>
/// Fluxo de caixa (PR 52): realized money by payment date, projected open títulos by due date, the
/// running balance from the saldo inicial, overdue apart, calendar buckets.
/// </summary>
[Collection("Integration")]
public class CashFlowTests
{
    private const string Fin = "/api/financial";
    private readonly TestWebApplicationFactory _factory;
    public CashFlowTests(TestWebApplicationFactory factory) => _factory = factory;

    private static DateOnly Today => DateOnly.FromDateTime(BusinessClock.ToBrazil(DateTime.UtcNow));

    [Fact]
    public async Task Open_titulos_are_projected_on_their_due_day_and_the_balance_runs_from_the_saldo_inicial()
    {
        var c = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var accounts = (await c.GetFromJsonAsync<List<FinancialAccountDto>>($"{Fin}/accounts"))!;
        var cash = accounts.First(a => a.AccountType == "Cash" && a.IsActive);
        (await c.PutAsJsonAsync($"{Fin}/accounts/{cash.Id}/opening-balance", new SetOpeningBalanceRequest(2500m, Today)))
            .EnsureSuccessStatusCode();
        var configuredElsewhere = accounts.Where(a => a.Id != cash.Id && a.OpeningBalanceDate is not null).Sum(a => a.OpeningBalance);

        var due = Today.AddDays(5);
        await InstallmentAsync(c, "Receivable", 100m, due);
        await InstallmentAsync(c, "Payable", 40m, Today.AddDays(10));

        var flow = await FlowAsync(c, Today, Today.AddDays(15), "day");
        flow.OpeningBalanceConfigured.Should().BeTrue();
        if (configuredElsewhere == 0 && accounts.Where(a => a.Id != cash.Id).All(a => a.OpeningBalanceDate is null))
            flow.OpeningBalance.Should().Be(2500m);                        // saldo inicial dated today: nothing realized before
        flow.Buckets.Should().HaveCount(16);
        flow.Buckets.Single(b => b.Start == due).ProjectedIn.Should().BeGreaterThanOrEqualTo(100m);
        flow.Buckets.Single(b => b.Start == Today.AddDays(10)).ProjectedOut.Should().BeGreaterThanOrEqualTo(40m);

        // the balance runs bucket by bucket and closes the period
        var running = flow.OpeningBalance;
        foreach (var b in flow.Buckets)
        {
            running += b.RealizedIn - b.RealizedOut + b.ProjectedIn - b.ProjectedOut;
            b.Balance.Should().Be(running);
        }
        flow.ClosingBalance.Should().Be(running);
    }

    [Fact]
    public async Task Past_due_titulos_are_shown_apart_and_not_projected()
    {
        var c = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var before = await FlowAsync(c, Today, Today.AddDays(7), "day");
        await InstallmentAsync(c, "Payable", 77m, Today.AddDays(-4));

        var after = await FlowAsync(c, Today, Today.AddDays(7), "day");
        after.OverdueOut.Should().Be(before.OverdueOut + 77m);
        after.Buckets.Sum(b => b.ProjectedOut).Should().Be(before.Buckets.Sum(b => b.ProjectedOut));
    }

    [Fact]
    public async Task Weeks_start_on_monday_and_months_follow_the_calendar()
    {
        var c = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var weeks = await FlowAsync(c, new DateOnly(2026, 10, 14), new DateOnly(2026, 10, 27), "week");   // Wed … Tue
        weeks.Buckets.Select(b => (b.Start, b.End)).Should().Equal(
            (new DateOnly(2026, 10, 14), new DateOnly(2026, 10, 18)),
            (new DateOnly(2026, 10, 19), new DateOnly(2026, 10, 25)),
            (new DateOnly(2026, 10, 26), new DateOnly(2026, 10, 27)));

        var months = await FlowAsync(c, new DateOnly(2026, 1, 20), new DateOnly(2026, 3, 5), "month");
        months.Buckets.Select(b => (b.Start, b.End)).Should().Equal(
            (new DateOnly(2026, 1, 20), new DateOnly(2026, 1, 31)),
            (new DateOnly(2026, 2, 1), new DateOnly(2026, 2, 28)),
            (new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 5)));
    }

    [Fact]
    public async Task Invalid_periods_and_opening_balances_are_rejected()
    {
        var c = await AuthClientFactory.LoginAsAdminAsync(_factory);
        (await c.GetAsync($"{Fin}/cash-flow?from=2026-10-10&to=2026-10-01")).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await c.GetAsync($"{Fin}/cash-flow?from=2025-01-01&to=2026-10-01")).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await c.GetAsync($"{Fin}/cash-flow?granularity=year")).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        var receivable = (await c.GetFromJsonAsync<List<FinancialAccountDto>>($"{Fin}/accounts"))!
            .First(a => a.AccountType == "Receivable");
        (await c.PutAsJsonAsync($"{Fin}/accounts/{receivable.Id}/opening-balance", new SetOpeningBalanceRequest(10m, Today)))
            .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    private static async Task<CashFlowDto> FlowAsync(HttpClient c, DateOnly from, DateOnly to, string granularity)
    {
        var resp = await c.GetAsync($"{Fin}/cash-flow?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}&granularity={granularity}");
        resp.StatusCode.Should().Be(HttpStatusCode.OK, await resp.Content.ReadAsStringAsync());
        return (await resp.Content.ReadFromJsonAsync<CashFlowDto>())!;
    }

    private static async Task InstallmentAsync(HttpClient c, string type, decimal amount, DateOnly due)
    {
        var resp = await c.PostAsJsonAsync($"{Fin}/transactions/installments", new CreateInstallmentsRequest(
            type, amount, $"Fluxo PR52 {Guid.NewGuid():N}"[..24], due.ToDateTime(TimeOnly.MinValue)));
        resp.IsSuccessStatusCode.Should().BeTrue(await resp.Content.ReadAsStringAsync());
    }
}
