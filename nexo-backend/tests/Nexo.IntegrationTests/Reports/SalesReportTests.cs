using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nexo.Application.Common;
using Nexo.Application.Common.Interfaces;
using Nexo.Application.Features.Cash;
using Nexo.Application.Features.Products;
using Nexo.Application.Features.Reports;
using Nexo.Application.Features.Sales;
using Nexo.Application.Features.Stock;
using Nexo.Domain.Entities;
using Nexo.Domain.Enums;
using Nexo.Infrastructure.Persistence;
using Nexo.IntegrationTests.Common;
using Nexo.IntegrationTests.Helpers;
using Xunit;

namespace Nexo.IntegrationTests.Reports;

/// <summary>
/// PR 65 — the sales report counts Brazil days, nets returns out of the sale they belong to and keeps
/// cancelled sales apart; the export carries the same sales. The sales are moved to a day in 2001 so
/// nothing else the suite sells lands in the period.
/// </summary>
[Collection("Integration")]
public class SalesReportTests
{
    private readonly TestWebApplicationFactory _factory;
    public SalesReportTests(TestWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task A_late_evening_sale_belongs_to_the_brazil_day_and_returns_are_netted()
    {
        var (c, session) = await ManagerWithDrawerAsync("rep.sales", opening: 100m);
        // A: 2 × 50 with 10 off → 90 cash, then 1 returned (45). B: 30 by Pix. C: 12 on credit, cancelled.
        var a = await PaidSaleAsync(c, session.Id, 50m, 2, discount: 10m);
        (await c.PostAsJsonAsync($"/api/sales/{a.Id}/returns", new CreateSaleReturnRequest(
            [new SaleReturnItemInput(a.Items[0].Id, 1)], "Cash", "Defeito", null))).EnsureSuccessStatusCode();
        var b = await PaidSaleAsync(c, session.Id, 30m, 1, payments: [new PaymentInput("Pix", "Cash", 30m)]);
        var credit = await PaidSaleAsync(c, session.Id, 12m, 1,
            payments: [new PaymentInput("Credit", "Credit", 12m, DateTime.UtcNow.AddDays(5))]);
        (await c.PostAsJsonAsync($"/api/sales/{credit.Id}/cancel", new CancelSaleRequest("Desistiu"))).EnsureSuccessStatusCode();
        await CloseAsync(c, session.Id);

        // 23:30 in Brazil on 31/03/2001 is already 1 April in UTC
        var day = new DateOnly(2001, 3, 31);
        await MoveSalesToAsync(BusinessClock.DayStartUtc(day).AddHours(23.5), a.Id, b.Id, credit.Id);

        var report = await c.GetFromJsonAsync<SalesReportDto>("/api/reports/sales?from=2001-03-31&to=2001-03-31");
        report!.TotalSales.Should().Be(2);
        report.GrossRevenue.Should().Be(120m);
        report.ReturnedValue.Should().Be(45m);
        report.TotalRevenue.Should().Be(75m);
        report.AverageTicket.Should().Be(37.5m);
        report.CancelledSales.Should().Be(1);
        report.CancelledValue.Should().Be(12m);
        report.DiscountValue.Should().Be(10m);
        report.Days.Should().ContainSingle().Which.Should().Be(new SalesReportDayDto("2001-03-31", 2, 75m));
        report.Payments.Select(p => (p.Method, p.Amount)).Should().Equal(("Cash", 90m), ("Pix", 30m));
        report.TopProducts.Select(p => (p.Quantity, p.Revenue)).Should().Equal((1m, 45m), (1m, 30m));
        report.Sellers.Should().ContainSingle().Which.Should().Be(new SalesReportSellerDto("Relatório rep.sales", 2, 75m));

        (await c.GetFromJsonAsync<SalesReportDto>("/api/reports/sales?from=2001-04-01&to=2001-04-01"))!
            .TotalSales.Should().Be(0, "in UTC these sales would have landed on the 1st");

        var csv = await c.GetAsync("/api/reports/sales/export?from=2001-03-31&to=2001-03-31");
        csv.StatusCode.Should().Be(HttpStatusCode.OK);
        csv.Content.Headers.ContentType!.MediaType.Should().Be("text/csv");
        csv.Content.Headers.ContentDisposition!.FileName.Should().Contain("vendas_2001-03-31_a_2001-03-31.csv");
        var lines = (await csv.Content.ReadAsStringAsync()).TrimStart('\uFEFF').Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        lines[0].Should().StartWith("Número;Data;Situação");
        lines.Should().HaveCount(4, "header + the three sales, the cancelled one included");
        lines.Should().Contain(l => l.Contains("31/03/2001 23:30") && l.Contains("Devolvida em parte;") && l.Contains(";90,00;45,00;45,00;Dinheiro"));
        lines.Should().Contain(l => l.Contains("Cancelada;") && l.Contains(";12,00;0,00;0,00;Cartão de crédito"));
    }

    [Theory]
    [InlineData("from=2001-13-01")]
    [InlineData("from=2001-03-31&to=2001-03-01")]
    [InlineData("from=2000-01-01&to=2001-03-31")]
    public async Task A_bad_period_is_a_clear_400(string query)
    {
        var admin = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var r = await admin.GetAsync($"/api/reports/sales?{query}");
        r.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString().Should().NotBeNullOrEmpty();
        (await admin.GetAsync($"/api/reports/sales/export?{query}")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task The_dashboard_says_which_period_its_sales_cover()
    {
        var admin = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var summary = await admin.GetFromJsonAsync<JsonElement>("/api/dashboard/summary");
        var month = SalesWindow.MonthToDate();
        summary.GetProperty("periodFrom").GetString().Should().Be(month.From.ToString("yyyy-MM-dd"));
        summary.GetProperty("periodTo").GetString().Should().Be(month.To.ToString("yyyy-MM-dd"));
        summary.GetProperty("salesByDay").GetArrayLength().Should().Be(30, "every one of the last 30 days, sold or not");
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private async Task MoveSalesToAsync(DateTime atUtc, params Guid[] ids)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        await db.Sales.IgnoreQueryFilters().Where(s => ids.Contains(s.Id))
            .ExecuteUpdateAsync(u => u.SetProperty(s => s.ConfirmedAt, atUtc).SetProperty(s => s.CreatedAt, atUtc));
    }

    private static async Task<SaleDto> PaidSaleAsync(
        HttpClient c, Guid sessionId, decimal unitPrice, decimal quantity, decimal discount = 0, PaymentInput[]? payments = null)
    {
        var code = $"PR65-{Guid.NewGuid():N}"[..20];
        var product = (await (await c.PostAsJsonAsync("/api/products",
                new CreateProductRequest(Code: code, Name: code, Unit: "Un", SalePrice: unitPrice, CostPrice: 1m, TrackStock: true)))
            .Content.ReadFromJsonAsync<ProductDto>())!;
        (await c.PostAsJsonAsync("/api/stock/adjust", new AdjustStockRequest(product.Id, 10, "ManualEntry", "PR65"))).EnsureSuccessStatusCode();
        var draft = (await (await c.PostAsJsonAsync("/api/sales", new CreateSaleRequest(CashSessionId: sessionId)))
            .Content.ReadFromJsonAsync<SaleDto>())!;
        (await c.PostAsJsonAsync($"/api/sales/{draft.Id}/items", new AddSaleItemRequest(product.Id, quantity, unitPrice))).EnsureSuccessStatusCode();
        var confirm = await c.PostAsJsonAsync($"/api/sales/{draft.Id}/confirm", new ConfirmSaleRequest(
            payments?.ToList() ?? [new PaymentInput("Cash", "Cash", unitPrice * quantity - discount)], DiscountAmount: discount));
        confirm.StatusCode.Should().Be(HttpStatusCode.OK, await confirm.Content.ReadAsStringAsync());
        return (await confirm.Content.ReadFromJsonAsync<SaleDto>())!;
    }

    private static async Task CloseAsync(HttpClient c, Guid sessionId)
    {
        var detail = (await c.GetFromJsonAsync<CashSessionDto>($"/api/cash/sessions/{sessionId}"))!;
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
                    tenantId: admin.TenantId, fullName: $"Relatório {login}",
                    email: $"{login}@{TestCredentials.TestLocalDomain}", login: login,
                    passwordHash: hasher.Hash(TestCredentials.SameTenantManagerPassword), role: UserRole.Gerente));
                await db.SaveChangesAsync();
            }
        }
        var client = await AuthClientFactory.LoginAsync(_factory, login, TestCredentials.SameTenantManagerPassword);
        var open = await client.PostAsJsonAsync("/api/cash/sessions/open", new OpenCashSessionRequest(opening, "PR65"));
        open.StatusCode.Should().Be(HttpStatusCode.Created, await open.Content.ReadAsStringAsync());
        return (client, (await open.Content.ReadFromJsonAsync<CashSessionDto>())!);
    }
}
