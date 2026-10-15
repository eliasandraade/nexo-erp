using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nexo.Application.Features.Auth;
using Nexo.Domain.Entities;
using Nexo.Domain.Enums;
using Nexo.Infrastructure.Persistence;
using Nexo.Api.Controllers.Modules.Restaurante;
using Nexo.IntegrationTests.Common;
using Nexo.IntegrationTests.Helpers;

namespace Nexo.IntegrationTests.Restaurante;

[Collection("Integration")]
public class EmployeeExpenseTests : IAsyncLifetime
{
    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public EmployeeExpenseTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client  = factory.CreateApiClient();
    }

    public async Task InitializeAsync()
    {
        await AuthenticateAsync(_client);
        await EnsureModuleSubscriptionAsync("restaurante");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // ── Despesas no livro (PR 53) ─────────────────────────────────────────────

    private async Task<List<FinancialTransaction>> LedgerOfAsync(Guid expenseId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        return await db.FinancialTransactions.IgnoreQueryFilters()
            .Where(t => t.ReferenceId == expenseId).OrderBy(t => t.CreatedAt).ToListAsync();
    }

    [Fact]
    public async Task An_expense_is_a_conta_a_pagar_paid_when_it_gets_a_payment_date()
    {
        var created = await (await _client.PostAsJsonAsync("/api/restaurante/expenses",
            new CreateExpenseRequest("Conta de luz PR53", "Energia", 412.50m, new DateOnly(2026, 9, 1), null, false)))
            .Content.ReadFromJsonAsync<ExpenseDto>();

        var open = (await LedgerOfAsync(created!.Id)).Should().ContainSingle().Subject;
        open.TransactionType.Should().Be(TransactionType.Payable);
        open.Category.Should().Be(FinancialCategory.OperatingExpense);
        open.Status.Should().BeOneOf(TransactionStatus.Pending, TransactionStatus.Overdue);
        open.Amount.Should().Be(412.50m);
        open.StoreId.Should().NotBeNull();

        // paying is done on the expense itself — the financeiro refuses it there
        (await _client.PostAsJsonAsync($"/api/financial/transactions/{open.Id}/pay", new { }))
            .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        (await _client.PutAsJsonAsync($"/api/restaurante/expenses/{created.Id}",
            new CreateExpenseRequest("Conta de luz PR53", "Energia", 412.50m, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 10), false)))
            .EnsureSuccessStatusCode();
        var afterPay = await LedgerOfAsync(created.Id);
        afterPay.Should().HaveCount(2);
        afterPay[0].Status.Should().Be(TransactionStatus.Cancelled);                // the open título was replaced…
        afterPay[1].Status.Should().Be(TransactionStatus.Paid);                     // …by the paid one
        afterPay[1].PaidAt!.Value.Date.Should().Be(new DateTime(2026, 9, 10));

        // a description-only edit changes nothing in the livro
        (await _client.PutAsJsonAsync($"/api/restaurante/expenses/{created.Id}",
            new CreateExpenseRequest("Luz setembro", "Energia", 412.50m, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 10), false)))
            .EnsureSuccessStatusCode();
        (await LedgerOfAsync(created.Id)).Should().HaveCount(2);

        // deleting a paid expense reverses it — history stays
        (await _client.DeleteAsync($"/api/restaurante/expenses/{created.Id}")).EnsureSuccessStatusCode();
        var reversal = (await LedgerOfAsync(created.Id)).Single(t => t.ReferenceType == "RestExpenseReversal");
        reversal.ReversesTransactionId.Should().Be(afterPay[1].Id);
        reversal.TransactionType.Should().Be(TransactionType.Receivable);
        reversal.Amount.Should().Be(412.50m);
    }

    [Theory]
    [InlineData("Impostos", FinancialCategory.Tax)]
    [InlineData("Outros", FinancialCategory.OtherExpense)]
    [InlineData("Aluguel", FinancialCategory.OperatingExpense)]
    public async Task Expense_categories_map_to_ledger_lines(string category, FinancialCategory expected)
    {
        var created = await (await _client.PostAsJsonAsync("/api/restaurante/expenses",
            new CreateExpenseRequest($"PR53 {category}", category, 10m, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 2), false)))
            .Content.ReadFromJsonAsync<ExpenseDto>();
        (await LedgerOfAsync(created!.Id)).Single().Category.Should().Be(expected);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task AuthenticateAsync(HttpClient client)
    {
        var r = await client.PostAsJsonAsync("/api/auth/login",
            TestCredentials.AdminLoginPayload());
        r.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await r.Content.ReadFromJsonAsync<LoginResponse>();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", body!.AccessToken);
    }

    private async Task EnsureModuleSubscriptionAsync(string moduleKey)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        var tenant = await db.Tenants.FirstOrDefaultAsync();
        if (tenant is null) return;
        var exists = await db.ModuleSubscriptions
            .AnyAsync(s => s.TenantId == tenant.Id && s.ModuleKey == moduleKey);
        if (!exists)
        {
            var sub = ModuleSubscription.CreateFromStripe(
                tenantId: tenant.Id, moduleKey: moduleKey,
                stripeSubscriptionId: $"sub_test_{moduleKey}",
                stripePriceId: $"price_test_{moduleKey}",
                planType: PlanType.Lifetime,
                periodStart: DateTime.UtcNow, periodEnd: null);
            db.ModuleSubscriptions.Add(sub);
            await db.SaveChangesAsync();
        }
    }

    // ── Employee Tests ────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateEmployee_ReturnsCreatedWithCorrectFields()
    {
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var req = new CreateEmployeeRequest(
            Name:          $"João {suffix}",
            Role:          "Cozinheiro",
            AdmissionDate: new DateOnly(2024, 1, 15),
            MonthlySalary: 2500m,
            Notes:         "Turno da manhã");

        var r = await _client.PostAsJsonAsync("/api/restaurante/employees", req);

        r.StatusCode.Should().Be(HttpStatusCode.Created);
        var dto = (await r.Content.ReadFromJsonAsync<EmployeeDto>())!;
        dto.Name.Should().Be($"João {suffix}");
        dto.Role.Should().Be("Cozinheiro");
        dto.MonthlySalary.Should().Be(2500m);
        dto.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task UpdateEmployee_CanDeactivate()
    {
        // Arrange — create
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var createReq = new CreateEmployeeRequest(
            Name:          $"Maria {suffix}",
            Role:          "Garçom",
            AdmissionDate: new DateOnly(2023, 6, 1),
            MonthlySalary: 1800m,
            Notes:         null);

        var createResp = await _client.PostAsJsonAsync("/api/restaurante/employees", createReq);
        createResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = (await createResp.Content.ReadFromJsonAsync<EmployeeDto>())!;

        // Act — deactivate
        var updateReq = new UpdateEmployeeRequest(
            Name:          created.Name,
            Role:          created.Role,
            AdmissionDate: created.AdmissionDate,
            MonthlySalary: created.MonthlySalary,
            Notes:         null,
            IsActive:      false);

        var updateResp = await _client.PutAsJsonAsync($"/api/restaurante/employees/{created.Id}", updateReq);
        updateResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = (await updateResp.Content.ReadFromJsonAsync<EmployeeDto>())!;
        updated.IsActive.Should().BeFalse();
    }

    // ── Expense Tests ─────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateExpense_ReturnsCreatedWithCorrectFields()
    {
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var req = new CreateExpenseRequest(
            Description:    $"Conta de energia {suffix}",
            Category:       "Energia",
            Amount:         450.75m,
            CompetenceDate: new DateOnly(2026, 5, 1),
            PaymentDate:    new DateOnly(2026, 5, 10),
            IsRecurring:    true);

        var r = await _client.PostAsJsonAsync("/api/restaurante/expenses", req);

        r.StatusCode.Should().Be(HttpStatusCode.Created);
        var dto = (await r.Content.ReadFromJsonAsync<ExpenseDto>())!;
        dto.Description.Should().Be($"Conta de energia {suffix}");
        dto.Category.Should().Be("Energia");
        dto.Amount.Should().Be(450.75m);
        dto.IsRecurring.Should().BeTrue();
    }

    [Fact]
    public async Task ListExpenses_FiltersByPeriod()
    {
        var suffix = Guid.NewGuid().ToString("N")[..6];

        // Create expense in May 2026
        var may = new CreateExpenseRequest(
            Description:    $"Água maio {suffix}",
            Category:       "Água",
            Amount:         120m,
            CompetenceDate: new DateOnly(2026, 5, 1),
            PaymentDate:    null,
            IsRecurring:    false);
        await _client.PostAsJsonAsync("/api/restaurante/expenses", may);

        // Create expense in June 2026
        var june = new CreateExpenseRequest(
            Description:    $"Água junho {suffix}",
            Category:       "Água",
            Amount:         130m,
            CompetenceDate: new DateOnly(2026, 6, 1),
            PaymentDate:    null,
            IsRecurring:    false);
        await _client.PostAsJsonAsync("/api/restaurante/expenses", june);

        // Act — list filtered to May
        var resp = await _client.GetFromJsonAsync<IReadOnlyList<ExpenseDto>>(
            "/api/restaurante/expenses?from=2026-05-01&to=2026-05-31");

        resp.Should().NotBeNull();
        resp!.Should().Contain(e => e.Description == $"Água maio {suffix}");
        resp.Should().NotContain(e => e.Description == $"Água junho {suffix}");
    }

    [Fact]
    public async Task FinanceiroSummary_IncludesPersonnelAndExpenses()
    {
        var suffix = Guid.NewGuid().ToString("N")[..6];

        // Create employee (salary = 3000)
        await _client.PostAsJsonAsync("/api/restaurante/employees",
            new CreateEmployeeRequest(
                Name:          $"Chef {suffix}",
                Role:          "Cozinheiro",
                AdmissionDate: new DateOnly(2024, 1, 1),
                MonthlySalary: 3000m,
                Notes:         null));

        // Create expense in May 2026 (amount = 500)
        await _client.PostAsJsonAsync("/api/restaurante/expenses",
            new CreateExpenseRequest(
                Description:    $"Luz {suffix}",
                Category:       "Energia",
                Amount:         500m,
                CompetenceDate: new DateOnly(2026, 5, 1),
                PaymentDate:    null,
                IsRecurring:    false));

        // Act — summary for May 2026 (no orders, so revenue = 0)
        var resp = await _client.GetFromJsonAsync<FinanceiroSummaryDtoFase3>(
            "/api/restaurante/financeiro/summary?from=2026-05-01&to=2026-05-31");

        resp.Should().NotBeNull();
        resp!.TotalPersonnelCost.Should().BeGreaterThanOrEqualTo(3000m,
            "the new employee's salary must be included");
        resp.TotalFixedExpenses.Should().BeGreaterThanOrEqualTo(500m,
            "the May expense must be included");
        resp.OperationalProfit.Should().BeLessThanOrEqualTo(
            resp.GrossMargin - 3000m - 500m + 0.01m,
            "operational profit = gross margin - personnel - expenses");
    }
}

// Mirrors FinanceiroSummaryDto after Fase 3 extension
public record FinanceiroSummaryDtoFase3(
    int     OrdersCount,
    decimal Revenue,
    decimal TotalCostOfGoodsSold,
    decimal WeightedCmvPercent,
    decimal GrossMargin,
    decimal TotalPersonnelCost,
    decimal TotalFixedExpenses,
    decimal OperationalProfit,
    decimal BreakEvenRevenue,
    string  From,
    string  To);
