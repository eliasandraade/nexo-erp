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

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task AuthenticateAsync(HttpClient client)
    {
        var r = await client.PostAsJsonAsync("/api/auth/login",
            new { login = "admin", password = "nexo@2026" });
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
}
