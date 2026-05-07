# CMV Restaurante Fase 3 — Pessoal, Despesas e KPIs Avançados

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Adicionar funcionários, despesas gerais e KPIs avançados (lucro operacional, ponto de equilíbrio) ao painel financeiro do restaurante.

**Architecture:** Duas novas entidades `StoreEntity` (`RestEmployee`, `RestExpense`) com CRUD próprio. O endpoint `GET /api/restaurante/financeiro/summary` é estendido para incluir custo de pessoal, despesas do período, lucro operacional e ponto de equilíbrio. O frontend estende `FinanceiroPage.tsx` com novas seções inline (funcionários, despesas) e cards de KPIs/insights adicionais.

**Tech Stack:** .NET 8 + EF Core + Npgsql (backend); React + TypeScript + TanStack Query v5 + TailwindCSS + shadcn/ui (frontend); xUnit + Testcontainers (integration tests).

---

## Key Domain Facts (zero-context reference)

- `StoreEntity` — base para entidades por loja: auto-injeta `TenantId` + `StoreId` via interceptor; global query filter automático — NUNCA usar `IgnoreQueryFilters()`.
- `TenantEntity` — base para entidades por tenant (sem filtro de loja).
- `BaseEntity` — campos `Id (Guid)`, `CreatedAt (DateTime)`, `UpdatedAt (DateTime)`.
- `NexoDbContext` — registrar DbSets na região `// ── Módulo Restaurante` (linhas ~77-93). Configurações EF ficam em `src/Nexo.Infrastructure/Persistence/Configurations/Modules/Restaurante/`.
- `FinanceiroController` em `src/Nexo.Api/Controllers/Modules/Restaurante/FinanceiroController.cs` — `GetSummary` retorna `FinanceiroSummaryDto` (record C# com campos posicionais).
- `FinanceiroSummaryDto` atual tem: `OrdersCount`, `Revenue`, `TotalCostOfGoodsSold`, `WeightedCmvPercent`, `GrossMargin`, `From`, `To`.
- `DateOnly` funciona nativamente com EF Core 8 + Npgsql e com System.Text.Json (.NET 8) — sem converter customizado.
- `[Authorize]` + `[RequireModule("restaurante")]` — obrigatório em todo controller do módulo.
- Integration tests usam `[Collection("Integration")]`, `IAsyncLifetime`, `TestWebApplicationFactory` de `Nexo.IntegrationTests.Helpers`.
- Padrão de autenticação nos testes: `POST /api/auth/login` com `{ login = "admin", password = "nexo@2026" }`.

---

## File Structure

**Backend — Create:**
- `nexo-backend/src/Nexo.Domain/Modules/Restaurante/RestEmployee.cs` — entidade funcionário
- `nexo-backend/src/Nexo.Domain/Modules/Restaurante/RestExpense.cs` — entidade despesa
- `nexo-backend/src/Nexo.Infrastructure/Persistence/Configurations/Modules/Restaurante/RestEmployeeConfiguration.cs`
- `nexo-backend/src/Nexo.Infrastructure/Persistence/Configurations/Modules/Restaurante/RestExpenseConfiguration.cs`
- `nexo-backend/src/Nexo.Api/Controllers/Modules/Restaurante/EmployeesController.cs` — CRUD funcionários
- `nexo-backend/src/Nexo.Api/Controllers/Modules/Restaurante/ExpensesController.cs` — CRUD despesas
- `nexo-backend/tests/Nexo.IntegrationTests/Restaurante/EmployeeExpenseTests.cs` — integration tests

**Backend — Modify:**
- `nexo-backend/src/Nexo.Infrastructure/Persistence/NexoDbContext.cs` — adicionar 2 DbSets
- `nexo-backend/src/Nexo.Api/Controllers/Modules/Restaurante/FinanceiroController.cs` — estender `GetSummary` + estender `FinanceiroSummaryDto`
- Migration gerada por `dotnet ef migrations add`

**Frontend — Create:**
- `nexo-main/src/modules/restaurante/api/employees-expenses.api.ts` — tipos + fetch functions
- `nexo-main/src/modules/restaurante/hooks/use-employees-expenses.ts` — hooks CRUD

**Frontend — Modify:**
- `nexo-main/src/modules/restaurante/api/financeiro.api.ts` — estender `FinanceiroSummaryDto`
- `nexo-main/src/modules/restaurante/pages/FinanceiroPage.tsx` — novas seções + KPIs

---

## Task 1: Backend — Entidades de domínio, configurações EF, migration e DbContext

**Files:**
- Create: `nexo-backend/src/Nexo.Domain/Modules/Restaurante/RestEmployee.cs`
- Create: `nexo-backend/src/Nexo.Domain/Modules/Restaurante/RestExpense.cs`
- Create: `nexo-backend/src/Nexo.Infrastructure/Persistence/Configurations/Modules/Restaurante/RestEmployeeConfiguration.cs`
- Create: `nexo-backend/src/Nexo.Infrastructure/Persistence/Configurations/Modules/Restaurante/RestExpenseConfiguration.cs`
- Modify: `nexo-backend/src/Nexo.Infrastructure/Persistence/NexoDbContext.cs`

- [ ] **Step 1: Criar `RestEmployee.cs`**

```csharp
using Nexo.Domain.Common;
using Nexo.Domain.Exceptions;

namespace Nexo.Domain.Modules.Restaurante;

/// <summary>
/// Funcionário do restaurante. Um registro por pessoa por loja.
/// MonthlySalary é o custo fixo mensal utilizado no cálculo de lucro operacional.
/// </summary>
public class RestEmployee : StoreEntity
{
    private RestEmployee() { }
    private RestEmployee(Guid tenantId) : base(tenantId) { }

    public string   Name          { get; private set; } = string.Empty;
    public string   Role          { get; private set; } = string.Empty;
    public DateOnly AdmissionDate { get; private set; }
    public decimal  MonthlySalary { get; private set; }
    public string?  Notes         { get; private set; }
    public bool     IsActive      { get; private set; }

    public static RestEmployee Create(
        Guid tenantId, string name, string role,
        DateOnly admissionDate, decimal monthlySalary, string? notes = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("Employee name is required.");
        if (monthlySalary < 0)
            throw new DomainException("Monthly salary cannot be negative.");

        return new RestEmployee(tenantId)
        {
            Name          = name.Trim(),
            Role          = role.Trim(),
            AdmissionDate = admissionDate,
            MonthlySalary = monthlySalary,
            Notes         = notes?.Trim(),
            IsActive      = true,
        };
    }

    public void Update(string name, string role, DateOnly admissionDate, decimal monthlySalary, string? notes, bool isActive)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("Employee name is required.");
        if (monthlySalary < 0)
            throw new DomainException("Monthly salary cannot be negative.");

        Name          = name.Trim();
        Role          = role.Trim();
        AdmissionDate = admissionDate;
        MonthlySalary = monthlySalary;
        Notes         = notes?.Trim();
        IsActive      = isActive;
        SetUpdatedAt();
    }
}
```

- [ ] **Step 2: Criar `RestExpense.cs`**

```csharp
using Nexo.Domain.Common;
using Nexo.Domain.Exceptions;

namespace Nexo.Domain.Modules.Restaurante;

/// <summary>
/// Despesa operacional do restaurante (energia, gás, água, aluguel, etc.).
/// CompetenceDate é o mês de competência — usado para filtrar no resumo financeiro.
/// </summary>
public class RestExpense : StoreEntity
{
    private RestExpense() { }
    private RestExpense(Guid tenantId) : base(tenantId) { }

    public string   Description    { get; private set; } = string.Empty;
    public string   Category       { get; private set; } = string.Empty;
    public decimal  Amount         { get; private set; }
    public DateOnly CompetenceDate { get; private set; }
    public DateOnly? PaymentDate   { get; private set; }
    public bool     IsRecurring    { get; private set; }

    public static RestExpense Create(
        Guid tenantId, string description, string category,
        decimal amount, DateOnly competenceDate, DateOnly? paymentDate, bool isRecurring)
    {
        if (string.IsNullOrWhiteSpace(description))
            throw new DomainException("Expense description is required.");
        if (amount < 0)
            throw new DomainException("Expense amount cannot be negative.");

        return new RestExpense(tenantId)
        {
            Description    = description.Trim(),
            Category       = category.Trim(),
            Amount         = amount,
            CompetenceDate = competenceDate,
            PaymentDate    = paymentDate,
            IsRecurring    = isRecurring,
        };
    }

    public void Update(string description, string category, decimal amount,
        DateOnly competenceDate, DateOnly? paymentDate, bool isRecurring)
    {
        if (string.IsNullOrWhiteSpace(description))
            throw new DomainException("Expense description is required.");
        if (amount < 0)
            throw new DomainException("Expense amount cannot be negative.");

        Description    = description.Trim();
        Category       = category.Trim();
        Amount         = amount;
        CompetenceDate = competenceDate;
        PaymentDate    = paymentDate;
        IsRecurring    = isRecurring;
        SetUpdatedAt();
    }
}
```

- [ ] **Step 3: Criar `RestEmployeeConfiguration.cs`**

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nexo.Domain.Modules.Restaurante;

namespace Nexo.Infrastructure.Persistence.Configurations.Modules.Restaurante;

public class RestEmployeeConfiguration : IEntityTypeConfiguration<RestEmployee>
{
    public void Configure(EntityTypeBuilder<RestEmployee> builder)
    {
        builder.ToTable("rest_employees", "nexo");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.StoreId).HasColumnName("store_id").IsRequired();
        builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
        builder.Property(x => x.Role).HasColumnName("role").HasMaxLength(100).IsRequired();
        builder.Property(x => x.AdmissionDate).HasColumnName("admission_date").IsRequired();
        builder.Property(x => x.MonthlySalary).HasColumnName("monthly_salary")
            .HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(x => x.Notes).HasColumnName("notes").HasMaxLength(1000);
        builder.Property(x => x.IsActive).HasColumnName("is_active")
            .HasDefaultValue(true).IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at")
            .HasColumnType("timestamptz").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at")
            .HasColumnType("timestamptz").IsRequired();

        builder.HasOne<Nexo.Domain.Entities.Tenant>()
            .WithMany()
            .HasForeignKey(x => x.TenantId)
            .HasConstraintName("fk_rest_employees_tenants")
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Nexo.Domain.Entities.Store>()
            .WithMany()
            .HasForeignKey(x => x.StoreId)
            .HasConstraintName("fk_rest_employees_stores")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.StoreId).HasDatabaseName("ix_rest_employees_store_id");
        builder.HasIndex(x => new { x.TenantId, x.StoreId, x.IsActive })
            .HasDatabaseName("ix_rest_employees_tenant_store_active");
    }
}
```

- [ ] **Step 4: Criar `RestExpenseConfiguration.cs`**

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nexo.Domain.Modules.Restaurante;

namespace Nexo.Infrastructure.Persistence.Configurations.Modules.Restaurante;

public class RestExpenseConfiguration : IEntityTypeConfiguration<RestExpense>
{
    public void Configure(EntityTypeBuilder<RestExpense> builder)
    {
        builder.ToTable("rest_expenses", "nexo");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.StoreId).HasColumnName("store_id").IsRequired();
        builder.Property(x => x.Description).HasColumnName("description").HasMaxLength(500).IsRequired();
        builder.Property(x => x.Category).HasColumnName("category").HasMaxLength(100).IsRequired();
        builder.Property(x => x.Amount).HasColumnName("amount")
            .HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(x => x.CompetenceDate).HasColumnName("competence_date").IsRequired();
        builder.Property(x => x.PaymentDate).HasColumnName("payment_date");
        builder.Property(x => x.IsRecurring).HasColumnName("is_recurring")
            .HasDefaultValue(false).IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at")
            .HasColumnType("timestamptz").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at")
            .HasColumnType("timestamptz").IsRequired();

        builder.HasOne<Nexo.Domain.Entities.Tenant>()
            .WithMany()
            .HasForeignKey(x => x.TenantId)
            .HasConstraintName("fk_rest_expenses_tenants")
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Nexo.Domain.Entities.Store>()
            .WithMany()
            .HasForeignKey(x => x.StoreId)
            .HasConstraintName("fk_rest_expenses_stores")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.StoreId).HasDatabaseName("ix_rest_expenses_store_id");
        builder.HasIndex(x => new { x.TenantId, x.StoreId, x.CompetenceDate })
            .HasDatabaseName("ix_rest_expenses_tenant_store_competence");
    }
}
```

- [ ] **Step 5: Adicionar DbSets no `NexoDbContext.cs`**

Encontre a linha com `public DbSet<CouponUsage>` (linha ~93) e adicione logo após:

```csharp
    public DbSet<RestEmployee> RestEmployees => Set<RestEmployee>();
    public DbSet<RestExpense>  RestExpenses  => Set<RestExpense>();
```

A seção completa ficará:
```csharp
    public DbSet<DeliveryZone>                  DeliveryZones                => Set<DeliveryZone>();
    public DbSet<Coupon>                        Coupons                      => Set<Coupon>();
    public DbSet<CouponUsage>                   CouponUsages                 => Set<CouponUsage>();
    public DbSet<RestEmployee>                  RestEmployees                => Set<RestEmployee>();
    public DbSet<RestExpense>                   RestExpenses                 => Set<RestExpense>();
```

- [ ] **Step 6: Build para verificar que compila**

```
cd nexo-backend
dotnet build src/Nexo.Api/Nexo.Api.csproj -c Release --no-restore 2>&1 | tail -5
```

Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 7: Gerar migration**

```
cd nexo-backend
dotnet ef migrations add CreateRestEmployeesAndExpenses --project src/Nexo.Infrastructure --startup-project src/Nexo.Api
```

Expected: migration criada em `src/Nexo.Infrastructure/Persistence/Migrations/`.

- [ ] **Step 8: Commit**

```bash
cd nexo-backend
git add src/Nexo.Domain/Modules/Restaurante/RestEmployee.cs
git add src/Nexo.Domain/Modules/Restaurante/RestExpense.cs
git add src/Nexo.Infrastructure/Persistence/Configurations/Modules/Restaurante/RestEmployeeConfiguration.cs
git add src/Nexo.Infrastructure/Persistence/Configurations/Modules/Restaurante/RestExpenseConfiguration.cs
git add src/Nexo.Infrastructure/Persistence/NexoDbContext.cs
git add src/Nexo.Infrastructure/Persistence/Migrations/
git commit -m "feat(restaurante): add RestEmployee and RestExpense entities with EF config and migration"
```

---

## Task 2: Backend — EmployeesController com CRUD + integration tests

**Files:**
- Create: `nexo-backend/src/Nexo.Api/Controllers/Modules/Restaurante/EmployeesController.cs`
- Create: `nexo-backend/tests/Nexo.IntegrationTests/Restaurante/EmployeeExpenseTests.cs`

- [ ] **Step 1: Criar `EmployeesController.cs`**

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Nexo.Api.Filters;
using Nexo.Application.Common.Interfaces;
using Nexo.Domain.Modules.Restaurante;
using Nexo.Infrastructure.Persistence;

namespace Nexo.Api.Controllers.Modules.Restaurante;

[ApiController]
[Route("api/restaurante/employees")]
[Authorize]
[RequireModule("restaurante")]
public class EmployeesController : ControllerBase
{
    private readonly NexoDbContext _db;
    private readonly ICurrentTenant _tenant;

    public EmployeesController(NexoDbContext db, ICurrentTenant tenant)
    {
        _db     = db;
        _tenant = tenant;
    }

    // GET /api/restaurante/employees?includeInactive=false
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<EmployeeDto>>> List(
        [FromQuery] bool includeInactive = false,
        CancellationToken ct = default)
    {
        var q = _db.RestEmployees.AsQueryable();
        if (!includeInactive) q = q.Where(e => e.IsActive);

        var list = await q
            .OrderBy(e => e.Name)
            .Select(e => new EmployeeDto(
                e.Id, e.Name, e.Role, e.AdmissionDate,
                e.MonthlySalary, e.Notes, e.IsActive, e.CreatedAt))
            .ToListAsync(ct);

        return Ok(list);
    }

    // GET /api/restaurante/employees/{id}
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<EmployeeDto>> Get(Guid id, CancellationToken ct)
    {
        var e = await _db.RestEmployees.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (e is null) return NotFound();
        return Ok(new EmployeeDto(e.Id, e.Name, e.Role, e.AdmissionDate, e.MonthlySalary, e.Notes, e.IsActive, e.CreatedAt));
    }

    // POST /api/restaurante/employees
    [HttpPost]
    public async Task<ActionResult<EmployeeDto>> Create(
        [FromBody] CreateEmployeeRequest req, CancellationToken ct)
    {
        var employee = RestEmployee.Create(
            _tenant.Id, req.Name, req.Role, req.AdmissionDate, req.MonthlySalary, req.Notes);

        _db.RestEmployees.Add(employee);
        await _db.SaveChangesAsync(ct);

        var dto = new EmployeeDto(employee.Id, employee.Name, employee.Role,
            employee.AdmissionDate, employee.MonthlySalary, employee.Notes,
            employee.IsActive, employee.CreatedAt);

        return CreatedAtAction(nameof(Get), new { id = employee.Id }, dto);
    }

    // PUT /api/restaurante/employees/{id}
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<EmployeeDto>> Update(
        Guid id, [FromBody] UpdateEmployeeRequest req, CancellationToken ct)
    {
        var employee = await _db.RestEmployees.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (employee is null) return NotFound();

        employee.Update(req.Name, req.Role, req.AdmissionDate, req.MonthlySalary, req.Notes, req.IsActive);
        await _db.SaveChangesAsync(ct);

        return Ok(new EmployeeDto(employee.Id, employee.Name, employee.Role,
            employee.AdmissionDate, employee.MonthlySalary, employee.Notes,
            employee.IsActive, employee.CreatedAt));
    }
}

// ── Request/Response records ──────────────────────────────────────────────────

public record CreateEmployeeRequest(
    string   Name,
    string   Role,
    DateOnly AdmissionDate,
    decimal  MonthlySalary,
    string?  Notes);

public record UpdateEmployeeRequest(
    string   Name,
    string   Role,
    DateOnly AdmissionDate,
    decimal  MonthlySalary,
    string?  Notes,
    bool     IsActive);

public record EmployeeDto(
    Guid     Id,
    string   Name,
    string   Role,
    DateOnly AdmissionDate,
    decimal  MonthlySalary,
    string?  Notes,
    bool     IsActive,
    DateTime CreatedAt);
```

- [ ] **Step 2: Criar `EmployeeExpenseTests.cs` com testes de funcionários**

```csharp
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
```

- [ ] **Step 3: Build + run tests**

```
cd nexo-backend
dotnet build src/Nexo.Api/Nexo.Api.csproj -c Release --no-restore 2>&1 | tail -5
dotnet test tests/Nexo.IntegrationTests --filter "EmployeeExpenseTests" -v minimal
```

Expected: build succeeded, 4 tests PASS.

- [ ] **Step 4: Commit**

```bash
cd nexo-backend
git add src/Nexo.Api/Controllers/Modules/Restaurante/EmployeesController.cs
git add tests/Nexo.IntegrationTests/Restaurante/EmployeeExpenseTests.cs
git commit -m "feat(restaurante): add EmployeesController with CRUD and integration tests"
```

---

## Task 3: Backend — ExpensesController com CRUD

**Files:**
- Create: `nexo-backend/src/Nexo.Api/Controllers/Modules/Restaurante/ExpensesController.cs`

- [ ] **Step 1: Criar `ExpensesController.cs`**

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Nexo.Api.Filters;
using Nexo.Application.Common.Interfaces;
using Nexo.Domain.Modules.Restaurante;
using Nexo.Infrastructure.Persistence;

namespace Nexo.Api.Controllers.Modules.Restaurante;

[ApiController]
[Route("api/restaurante/expenses")]
[Authorize]
[RequireModule("restaurante")]
public class ExpensesController : ControllerBase
{
    private readonly NexoDbContext _db;
    private readonly ICurrentTenant _tenant;

    public ExpensesController(NexoDbContext db, ICurrentTenant tenant)
    {
        _db     = db;
        _tenant = tenant;
    }

    // GET /api/restaurante/expenses?from=yyyy-MM-dd&to=yyyy-MM-dd
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ExpenseDto>>> List(
        [FromQuery] string? from,
        [FromQuery] string? to,
        CancellationToken ct = default)
    {
        var q = _db.RestExpenses.AsQueryable();

        if (TryParseDate(from) is { } fromDate)
            q = q.Where(e => e.CompetenceDate >= fromDate);

        if (TryParseDate(to) is { } toDate)
            q = q.Where(e => e.CompetenceDate <= toDate);

        var list = await q
            .OrderByDescending(e => e.CompetenceDate)
            .ThenBy(e => e.Category)
            .Select(e => new ExpenseDto(
                e.Id, e.Description, e.Category, e.Amount,
                e.CompetenceDate, e.PaymentDate, e.IsRecurring, e.CreatedAt))
            .ToListAsync(ct);

        return Ok(list);
    }

    // GET /api/restaurante/expenses/{id}
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ExpenseDto>> Get(Guid id, CancellationToken ct)
    {
        var e = await _db.RestExpenses.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (e is null) return NotFound();
        return Ok(new ExpenseDto(e.Id, e.Description, e.Category, e.Amount,
            e.CompetenceDate, e.PaymentDate, e.IsRecurring, e.CreatedAt));
    }

    // POST /api/restaurante/expenses
    [HttpPost]
    public async Task<ActionResult<ExpenseDto>> Create(
        [FromBody] CreateExpenseRequest req, CancellationToken ct)
    {
        var expense = RestExpense.Create(
            _tenant.Id, req.Description, req.Category, req.Amount,
            req.CompetenceDate, req.PaymentDate, req.IsRecurring);

        _db.RestExpenses.Add(expense);
        await _db.SaveChangesAsync(ct);

        var dto = new ExpenseDto(expense.Id, expense.Description, expense.Category,
            expense.Amount, expense.CompetenceDate, expense.PaymentDate,
            expense.IsRecurring, expense.CreatedAt);

        return CreatedAtAction(nameof(Get), new { id = expense.Id }, dto);
    }

    // PUT /api/restaurante/expenses/{id}
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ExpenseDto>> Update(
        Guid id, [FromBody] CreateExpenseRequest req, CancellationToken ct)
    {
        var expense = await _db.RestExpenses.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (expense is null) return NotFound();

        expense.Update(req.Description, req.Category, req.Amount,
            req.CompetenceDate, req.PaymentDate, req.IsRecurring);
        await _db.SaveChangesAsync(ct);

        return Ok(new ExpenseDto(expense.Id, expense.Description, expense.Category,
            expense.Amount, expense.CompetenceDate, expense.PaymentDate,
            expense.IsRecurring, expense.CreatedAt));
    }

    // DELETE /api/restaurante/expenses/{id}
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var expense = await _db.RestExpenses.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (expense is null) return NotFound();

        _db.RestExpenses.Remove(expense);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static DateOnly? TryParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return DateOnly.TryParseExact(value, "yyyy-MM-dd",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var d) ? d : null;
    }
}

// ── Request/Response records (shared with employee tests) ─────────────────────

public record CreateExpenseRequest(
    string    Description,
    string    Category,
    decimal   Amount,
    DateOnly  CompetenceDate,
    DateOnly? PaymentDate,
    bool      IsRecurring);

public record ExpenseDto(
    Guid      Id,
    string    Description,
    string    Category,
    decimal   Amount,
    DateOnly  CompetenceDate,
    DateOnly? PaymentDate,
    bool      IsRecurring,
    DateTime  CreatedAt);
```

- [ ] **Step 2: Build e testar os novos endpoints de despesa**

Os testes de despesas já estão em `EmployeeExpenseTests.cs` (Task 2). Só rebuildar e confirmar:

```
cd nexo-backend
dotnet build src/Nexo.Api/Nexo.Api.csproj -c Release --no-restore 2>&1 | tail -5
dotnet test tests/Nexo.IntegrationTests --filter "EmployeeExpenseTests" -v minimal
```

Expected: build succeeded, todos os 4 testes PASS.

- [ ] **Step 3: Commit**

```bash
cd nexo-backend
git add src/Nexo.Api/Controllers/Modules/Restaurante/ExpensesController.cs
git commit -m "feat(restaurante): add ExpensesController with CRUD (GET list w/ period filter, POST, PUT, DELETE)"
```

---

## Task 4: Backend — Estender FinanceiroController summary com pessoal, despesas e lucros

**Files:**
- Modify: `nexo-backend/src/Nexo.Api/Controllers/Modules/Restaurante/FinanceiroController.cs`

O `FinanceiroSummaryDto` atual (ao fim do arquivo) é:
```csharp
public record FinanceiroSummaryDto(
    int     OrdersCount,
    decimal Revenue,
    decimal TotalCostOfGoodsSold,
    decimal WeightedCmvPercent,
    decimal GrossMargin,
    string  From,
    string  To);
```

- [ ] **Step 1: Substituir `FinanceiroSummaryDto` pela versão estendida**

Substitua o record `FinanceiroSummaryDto` no final do arquivo:

```csharp
public record FinanceiroSummaryDto(
    int     OrdersCount,
    decimal Revenue,
    decimal TotalCostOfGoodsSold,
    decimal WeightedCmvPercent,
    decimal GrossMargin,
    decimal TotalPersonnelCost,   // soma salários funcionários ativos
    decimal TotalFixedExpenses,   // soma despesas com competência no período
    decimal OperationalProfit,    // Revenue - COGS - TotalPersonnelCost - TotalFixedExpenses
    decimal BreakEvenRevenue,     // (TotalPersonnelCost + TotalFixedExpenses) / (1 - CMV%), 0 se CMV≥100%
    string  From,
    string  To);
```

- [ ] **Step 2: Substituir o método `GetSummary` completo**

O método atual começa em `[HttpGet("summary")]` e termina com o `return Ok(new FinanceiroSummaryDto(...))`. Substitua-o pelo seguinte (mantendo o helper `TryParseDate` no final):

```csharp
[HttpGet("summary")]
public async Task<ActionResult<FinanceiroSummaryDto>> GetSummary(
    [FromQuery] string? from,
    [FromQuery] string? to,
    CancellationToken ct)
{
    var today   = DateTime.UtcNow.Date;
    var fromUtc = TryParseDate(from) ?? new DateTime(today.Year, today.Month, 1, 0, 0, 0, DateTimeKind.Utc);
    var toUtc   = (TryParseDate(to) ?? today).AddDays(1); // exclusive upper bound

    var fromDate = DateOnly.FromDateTime(fromUtc);
    var toDate   = DateOnly.FromDateTime(toUtc.AddDays(-1));

    // ── Step 1: Revenue from Paid orders in period ─────────────────────────
    var orderRows = await _db.RestOrders
        .Where(o => o.Status == RestOrderStatus.Paid
                 && o.ClosedAt >= fromUtc
                 && o.ClosedAt <  toUtc)
        .Select(o => new
        {
            o.Id,
            o.CouvertAmount,
            o.ServiceFeeAmount,
            ItemsTotal = o.Items
                .Where(i => i.Status != RestOrderItemStatus.Cancelled)
                .Sum(i => (decimal?)i.Total) ?? 0m,
        })
        .ToListAsync(ct);

    var ordersCount = orderRows.Count;
    var revenue     = orderRows.Sum(r => r.ItemsTotal + r.CouvertAmount + r.ServiceFeeAmount);

    // ── Step 2: Personnel cost (all active employees, full month) ──────────
    var personnelCost = await _db.RestEmployees
        .Where(e => e.IsActive)
        .SumAsync(e => (decimal?)e.MonthlySalary, ct) ?? 0m;

    // ── Step 3: Fixed expenses for the period ─────────────────────────────
    var fixedExpenses = await _db.RestExpenses
        .Where(e => e.CompetenceDate >= fromDate && e.CompetenceDate <= toDate)
        .SumAsync(e => (decimal?)e.Amount, ct) ?? 0m;

    // ── Step 4: COGS (if no orders, skip heavy queries) ───────────────────
    decimal totalCogs       = 0m;
    decimal weightedCmv     = 0m;

    if (ordersCount > 0)
    {
        var orderIds = orderRows.Select(r => r.Id).ToList();

        var itemsGrouped = await _db.RestOrderItems
            .Where(oi => orderIds.Contains(oi.OrderId)
                      && oi.Status != RestOrderItemStatus.Cancelled)
            .GroupBy(oi => oi.ProductId)
            .Select(g => new { ProductId = g.Key, TotalQty = g.Sum(i => i.Quantity) })
            .ToListAsync(ct);

        var soldProductIds = itemsGrouped.Select(g => g.ProductId).ToList();

        var recipeCards = await _db.RestRecipeCards
            .Where(rc => soldProductIds.Contains(rc.ProductId))
            .ToListAsync(ct);

        var rcIds = recipeCards.Select(rc => rc.Id).ToList();
        var allIngredients = await _db.RestRecipeIngredients
            .Where(i => rcIds.Contains(i.RecipeCardId))
            .ToListAsync(ct);

        var ingredientsByCard = allIngredients
            .GroupBy(i => i.RecipeCardId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var cardsByProduct = recipeCards.ToDictionary(rc => rc.ProductId);

        var allProductIds = recipeCards.Select(rc => rc.ProductId)
            .Concat(allIngredients.Select(i => i.IngredientProductId))
            .Distinct().ToList();

        var products = await _db.Products
            .Where(p => allProductIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, ct);

        var settings  = await _db.FoodServiceSettings.FirstOrDefaultAsync(ct);
        var gasRate   = settings?.CostPerMinuteGas      ?? 0m;
        var laborRate = settings?.CostPerMinuteLaborRate ?? 0m;

        foreach (var row in itemsGrouped)
        {
            if (!cardsByProduct.TryGetValue(row.ProductId, out var card)) continue;

            var cardIngs     = ingredientsByCard.GetValueOrDefault(card.Id, []);
            var totalIngCost = cardIngs.Sum(ing =>
            {
                products.TryGetValue(ing.IngredientProductId, out var ingProd);
                return ing.Quantity * (ingProd?.CostPrice ?? 0m);
            });

            var prepMin     = (decimal)(card.TotalPrepTimeMin ?? 0);
            var unitIngCost = card.Yield > 0 ? totalIngCost / card.Yield : 0m;
            var unitCost    = unitIngCost + prepMin * gasRate + prepMin * laborRate;

            totalCogs += row.TotalQty * unitCost;
        }

        weightedCmv = revenue > 0 ? totalCogs / revenue * 100m : 0m;
    }

    var grossMargin       = revenue - totalCogs;
    var operationalProfit = grossMargin - personnelCost - fixedExpenses;
    var cmvRatio          = weightedCmv / 100m;
    var breakEven         = cmvRatio < 1m
        ? Math.Round((personnelCost + fixedExpenses) / (1m - cmvRatio), 2)
        : 0m;

    return Ok(new FinanceiroSummaryDto(
        OrdersCount:          ordersCount,
        Revenue:              Math.Round(revenue, 2),
        TotalCostOfGoodsSold: Math.Round(totalCogs, 2),
        WeightedCmvPercent:   Math.Round(weightedCmv, 2),
        GrossMargin:          Math.Round(grossMargin, 2),
        TotalPersonnelCost:   Math.Round(personnelCost, 2),
        TotalFixedExpenses:   Math.Round(fixedExpenses, 2),
        OperationalProfit:    Math.Round(operationalProfit, 2),
        BreakEvenRevenue:     breakEven,
        From:                 fromUtc.ToString("yyyy-MM-dd"),
        To:                   toUtc.AddDays(-1).ToString("yyyy-MM-dd")));
}
```

- [ ] **Step 3: Adicionar integration test para o summary estendido**

No arquivo `tests/Nexo.IntegrationTests/Restaurante/EmployeeExpenseTests.cs`, adicione ao final da classe `EmployeeExpenseTests`:

```csharp
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
```

Adicione o record local no final do arquivo de testes:

```csharp
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
```

- [ ] **Step 4: Build + run todos os testes de funcionários e despesas**

```
cd nexo-backend
dotnet build src/Nexo.Api/Nexo.Api.csproj -c Release --no-restore 2>&1 | tail -5
dotnet test tests/Nexo.IntegrationTests --filter "EmployeeExpenseTests" -v minimal
```

Expected: build succeeded, 5 testes PASS.

- [ ] **Step 5: Commit**

```bash
cd nexo-backend
git add src/Nexo.Api/Controllers/Modules/Restaurante/FinanceiroController.cs
git add tests/Nexo.IntegrationTests/Restaurante/EmployeeExpenseTests.cs
git commit -m "feat(restaurante): extend financeiro summary with personnel cost, expenses, operational profit and break-even"
```

---

## Task 5: Frontend — Tipos de API e fetch functions

**Files:**
- Create: `nexo-main/src/modules/restaurante/api/employees-expenses.api.ts`
- Modify: `nexo-main/src/modules/restaurante/api/financeiro.api.ts`

- [ ] **Step 1: Criar `employees-expenses.api.ts`**

```typescript
import { apiClient } from "@/services/api-client";

// ── Predefined expense categories ─────────────────────────────────────────────

export const EXPENSE_CATEGORIES = [
  "Energia", "Gás", "Água", "Internet",
  "Impostos", "Manutenção", "Aluguel",
  "Embalagem", "Publicidade", "Outros",
] as const;

export type ExpenseCategory = typeof EXPENSE_CATEGORIES[number];

// ── Employee types ────────────────────────────────────────────────────────────

export interface EmployeeDto {
  id:            string;
  name:          string;
  role:          string;
  admissionDate: string; // "yyyy-MM-dd"
  monthlySalary: number;
  notes:         string | null;
  isActive:      boolean;
  createdAt:     string;
}

export interface CreateEmployeeRequest {
  name:          string;
  role:          string;
  admissionDate: string; // "yyyy-MM-dd"
  monthlySalary: number;
  notes?:        string | null;
}

export interface UpdateEmployeeRequest extends CreateEmployeeRequest {
  isActive: boolean;
}

// ── Expense types ─────────────────────────────────────────────────────────────

export interface ExpenseDto {
  id:             string;
  description:    string;
  category:       string;
  amount:         number;
  competenceDate: string; // "yyyy-MM-dd"
  paymentDate:    string | null; // "yyyy-MM-dd"
  isRecurring:    boolean;
  createdAt:      string;
}

export interface CreateExpenseRequest {
  description:    string;
  category:       string;
  amount:         number;
  competenceDate: string; // "yyyy-MM-dd"
  paymentDate?:   string | null;
  isRecurring:    boolean;
}

// ── Employee fetch functions ──────────────────────────────────────────────────

export const fetchEmployees = (includeInactive = false): Promise<EmployeeDto[]> =>
  apiClient.get<EmployeeDto[]>(`/restaurante/employees?includeInactive=${includeInactive}`);

export const createEmployee = (req: CreateEmployeeRequest): Promise<EmployeeDto> =>
  apiClient.post<EmployeeDto>("/restaurante/employees", req);

export const updateEmployee = (id: string, req: UpdateEmployeeRequest): Promise<EmployeeDto> =>
  apiClient.put<EmployeeDto>(`/restaurante/employees/${id}`, req);

// ── Expense fetch functions ───────────────────────────────────────────────────

export const fetchExpenses = (from?: string, to?: string): Promise<ExpenseDto[]> => {
  const p = new URLSearchParams();
  if (from) p.set("from", from);
  if (to)   p.set("to",   to);
  const qs = p.toString();
  return apiClient.get<ExpenseDto[]>(`/restaurante/expenses${qs ? `?${qs}` : ""}`);
};

export const createExpense = (req: CreateExpenseRequest): Promise<ExpenseDto> =>
  apiClient.post<ExpenseDto>("/restaurante/expenses", req);

export const updateExpense = (id: string, req: CreateExpenseRequest): Promise<ExpenseDto> =>
  apiClient.put<ExpenseDto>(`/restaurante/expenses/${id}`, req);

export const deleteExpense = (id: string): Promise<void> =>
  apiClient.delete<void>(`/restaurante/expenses/${id}`);
```

- [ ] **Step 2: Estender `FinanceiroSummaryDto` em `financeiro.api.ts`**

No arquivo `nexo-main/src/modules/restaurante/api/financeiro.api.ts`, substitua o interface `FinanceiroSummaryDto` pelo seguinte:

```typescript
export interface FinanceiroSummaryDto {
  ordersCount:          number;
  revenue:              number;
  totalCostOfGoodsSold: number;
  weightedCmvPercent:   number;
  grossMargin:          number;
  totalPersonnelCost:   number;
  totalFixedExpenses:   number;
  operationalProfit:    number;
  breakEvenRevenue:     number;
  from:                 string;
  to:                   string;
}
```

- [ ] **Step 3: TypeScript check**

```
cd nexo-main
npx tsc --noEmit 2>&1 | head -30
```

Expected: 0 erros.

- [ ] **Step 4: Commit**

```bash
cd nexo-main
git add src/modules/restaurante/api/employees-expenses.api.ts
git add src/modules/restaurante/api/financeiro.api.ts
git commit -m "feat(restaurante): add employees-expenses API types and extend FinanceiroSummaryDto with Fase 3 fields"
```

---

## Task 6: Frontend — Hooks para funcionários e despesas

**Files:**
- Create: `nexo-main/src/modules/restaurante/hooks/use-employees-expenses.ts`

- [ ] **Step 1: Criar `use-employees-expenses.ts`**

```typescript
import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import {
  fetchEmployees, createEmployee, updateEmployee,
  fetchExpenses, createExpense, updateExpense, deleteExpense,
  type CreateEmployeeRequest, type UpdateEmployeeRequest,
  type CreateExpenseRequest,
} from "../api/employees-expenses.api";

// ── Query keys ────────────────────────────────────────────────────────────────

export const EMPLOYEES_KEY = (includeInactive = false) =>
  ["restaurante", "employees", includeInactive] as const;

export const EXPENSES_KEY = (from?: string, to?: string) =>
  ["restaurante", "expenses", from, to] as const;

// ── Employee hooks ────────────────────────────────────────────────────────────

export function useEmployees(includeInactive = false) {
  return useQuery({
    queryKey: EMPLOYEES_KEY(includeInactive),
    queryFn:  () => fetchEmployees(includeInactive),
    staleTime: 60_000,
  });
}

export function useCreateEmployee() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (req: CreateEmployeeRequest) => createEmployee(req),
    onSuccess:  () => qc.invalidateQueries({ queryKey: ["restaurante", "employees"] }),
  });
}

export function useUpdateEmployee() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ id, req }: { id: string; req: UpdateEmployeeRequest }) =>
      updateEmployee(id, req),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["restaurante", "employees"] }),
  });
}

// ── Expense hooks ─────────────────────────────────────────────────────────────

export function useExpenses(from?: string, to?: string) {
  return useQuery({
    queryKey: EXPENSES_KEY(from, to),
    queryFn:  () => fetchExpenses(from, to),
    enabled:  !!from && !!to,
  });
}

export function useCreateExpense() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (req: CreateExpenseRequest) => createExpense(req),
    onSuccess:  () => qc.invalidateQueries({ queryKey: ["restaurante", "expenses"] }),
  });
}

export function useUpdateExpense() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ id, req }: { id: string; req: CreateExpenseRequest }) =>
      updateExpense(id, req),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["restaurante", "expenses"] }),
  });
}

export function useDeleteExpense() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => deleteExpense(id),
    onSuccess:  () => qc.invalidateQueries({ queryKey: ["restaurante", "expenses"] }),
  });
}
```

- [ ] **Step 2: TypeScript check**

```
cd nexo-main
npx tsc --noEmit 2>&1 | head -20
```

Expected: 0 erros neste arquivo.

- [ ] **Step 3: Commit**

```bash
cd nexo-main
git add src/modules/restaurante/hooks/use-employees-expenses.ts
git commit -m "feat(restaurante): add useEmployees, useExpenses and related mutation hooks"
```

---

## Task 7: Frontend — Estender FinanceiroPage com novas seções e KPIs

**Files:**
- Modify: `nexo-main/src/modules/restaurante/pages/FinanceiroPage.tsx`

A `FinanceiroPage.tsx` atual tem: imports, helpers, `KpiCard`, `CmvTable`, `FinanceiroPage` (com period picker, 4 KPIs, CMV table).

O arquivo será estendido com novos imports, novos sub-componentes (`EmployeesSection`, `ExpensesSection`, `InsightCards`) e a `FinanceiroPage` será atualizada para renderizar tudo.

- [ ] **Step 1: Substituir o conteúdo completo de `FinanceiroPage.tsx`**

```tsx
import { useState, useMemo } from "react";
import {
  TrendingUp, TrendingDown, DollarSign, ShoppingBag,
  Users, Receipt, Target, Lightbulb,
  ArrowUpDown, ArrowUp, ArrowDown, Search,
  Plus, Check, X, Pencil, Trash2, Loader2,
} from "lucide-react";
import { cn } from "@/lib/utils";
import { Input } from "@/components/ui/input";
import { Button } from "@/components/ui/button";
import { Label } from "@/components/ui/label";
import {
  Select, SelectContent, SelectItem, SelectTrigger, SelectValue,
} from "@/components/ui/select";
import { PageHeader } from "@/components/shared/PageHeader";
import { toast } from "sonner";
import { useCmvReport, useFinanceiroSummary } from "../hooks/use-financeiro";
import {
  useEmployees, useCreateEmployee, useUpdateEmployee,
  useExpenses, useCreateExpense, useUpdateExpense, useDeleteExpense,
} from "../hooks/use-employees-expenses";
import { EXPENSE_CATEGORIES } from "../api/employees-expenses.api";
import type { CmvReportItemDto, FinanceiroSummaryDto } from "../api/financeiro.api";
import type { EmployeeDto, ExpenseDto } from "../api/employees-expenses.api";

// ── Helpers ───────────────────────────────────────────────────────────────────

function fmt(v: number) {
  return v.toLocaleString("pt-BR", { style: "currency", currency: "BRL" });
}

function fmtPct(v: number) {
  return `${v.toFixed(1)}%`;
}

function cmvColor(pct: number): string {
  if (pct < 30) return "text-green-600 dark:text-green-400";
  if (pct <= 40) return "text-yellow-600 dark:text-yellow-400";
  return "text-red-600 dark:text-red-400";
}

function cmvBadge(pct: number): string {
  if (pct < 30) return "bg-green-100 text-green-700 dark:bg-green-900/30 dark:text-green-400";
  if (pct <= 40) return "bg-yellow-100 text-yellow-700 dark:bg-yellow-900/30 dark:text-yellow-400";
  return "bg-red-100 text-red-700 dark:bg-red-900/30 dark:text-red-400";
}

const MONTHS = [
  "Janeiro","Fevereiro","Março","Abril","Maio","Junho",
  "Julho","Agosto","Setembro","Outubro","Novembro","Dezembro",
];

function monthBounds(year: number, month: number): { from: string; to: string } {
  const pad  = (n: number) => String(n).padStart(2, "0");
  const last = new Date(year, month, 0).getDate();
  return { from: `${year}-${pad(month)}-01`, to: `${year}-${pad(month)}-${pad(last)}` };
}

function prevMonthBounds(year: number, month: number) {
  const d = new Date(year, month - 2, 1); // month-2 because Date uses 0-based months
  return monthBounds(d.getFullYear(), d.getMonth() + 1);
}

// ── KPI Card ──────────────────────────────────────────────────────────────────

interface KpiCardProps {
  icon:   React.ElementType;
  label:  string;
  value:  string;
  sub?:   string;
  color?: string;
}

function KpiCard({ icon: Icon, label, value, sub, color = "text-primary" }: KpiCardProps) {
  return (
    <div className="rounded-xl border border-border bg-card p-4 flex flex-col gap-2">
      <div className="flex items-center gap-2">
        <div className={cn("p-2 rounded-lg bg-muted/60", color)}>
          <Icon className="h-4 w-4" />
        </div>
        <span className="text-xs font-medium text-muted-foreground">{label}</span>
      </div>
      <p className="text-2xl font-bold text-foreground tabular-nums leading-none">{value}</p>
      {sub && <p className="text-xs text-muted-foreground">{sub}</p>}
    </div>
  );
}

// ── CMV Table ─────────────────────────────────────────────────────────────────

type SortField = "productName" | "salePrice" | "unitCost" | "cmvPercent" | "margin";
type SortDir   = "asc" | "desc";

function CmvTable({ items }: { items: CmvReportItemDto[] }) {
  const [search,  setSearch]  = useState("");
  const [sortBy,  setSortBy]  = useState<SortField>("cmvPercent");
  const [sortDir, setSortDir] = useState<SortDir>("desc");

  const toggleSort = (field: SortField) => {
    if (sortBy === field) setSortDir(d => d === "asc" ? "desc" : "asc");
    else { setSortBy(field); setSortDir("desc"); }
  };

  const SortIcon = ({ field }: { field: SortField }) => {
    if (sortBy !== field) return <ArrowUpDown className="h-3 w-3 text-muted-foreground" />;
    return sortDir === "asc" ? <ArrowUp className="h-3 w-3" /> : <ArrowDown className="h-3 w-3" />;
  };

  const filtered = useMemo(() => {
    const q = search.toLowerCase();
    return items.filter(i =>
      i.productName.toLowerCase().includes(q) || i.productCode.toLowerCase().includes(q));
  }, [items, search]);

  const sorted = useMemo(() => [...filtered].sort((a, b) => {
    const dir = sortDir === "asc" ? 1 : -1;
    if (sortBy === "productName") return dir * a.productName.localeCompare(b.productName);
    return dir * (a[sortBy] - b[sortBy]);
  }), [filtered, sortBy, sortDir]);

  const thClass = "text-left text-xs font-medium text-muted-foreground uppercase tracking-wide px-3 py-2";
  const tdClass = "px-3 py-3 text-sm";

  return (
    <div className="space-y-3">
      <div className="relative">
        <Search className="absolute left-3 top-1/2 -translate-y-1/2 h-4 w-4 text-muted-foreground" />
        <Input className="pl-9 text-sm" placeholder="Filtrar por nome ou código…"
          value={search} onChange={e => setSearch(e.target.value)} />
      </div>
      {sorted.length === 0 ? (
        <div className="text-center py-12 text-sm text-muted-foreground">
          Nenhum prato encontrado. Crie fichas técnicas para seus produtos do cardápio.
        </div>
      ) : (
        <div className="rounded-xl border border-border overflow-hidden">
          <div className="overflow-x-auto">
            <table className="w-full min-w-[640px]">
              <thead className="bg-muted/40 border-b border-border">
                <tr>
                  <th className={thClass}>
                    <button className="flex items-center gap-1 hover:text-foreground transition-colors"
                      onClick={() => toggleSort("productName")}>
                      Prato <SortIcon field="productName" />
                    </button>
                  </th>
                  <th className={cn(thClass, "text-right")}>
                    <button className="flex items-center gap-1 ml-auto hover:text-foreground transition-colors"
                      onClick={() => toggleSort("salePrice")}>
                      Preço venda <SortIcon field="salePrice" />
                    </button>
                  </th>
                  <th className={cn(thClass, "text-right")}>
                    <button className="flex items-center gap-1 ml-auto hover:text-foreground transition-colors"
                      onClick={() => toggleSort("unitCost")}>
                      Custo unitário <SortIcon field="unitCost" />
                    </button>
                  </th>
                  <th className={cn(thClass, "text-right")}>
                    <button className="flex items-center gap-1 ml-auto hover:text-foreground transition-colors"
                      onClick={() => toggleSort("margin")}>
                      Margem <SortIcon field="margin" />
                    </button>
                  </th>
                  <th className={cn(thClass, "text-right")}>
                    <button className="flex items-center gap-1 ml-auto hover:text-foreground transition-colors"
                      onClick={() => toggleSort("cmvPercent")}>
                      CMV% <SortIcon field="cmvPercent" />
                    </button>
                  </th>
                </tr>
              </thead>
              <tbody className="divide-y divide-border">
                {sorted.map(item => (
                  <tr key={item.productId} className="hover:bg-muted/20 transition-colors">
                    <td className={tdClass}>
                      <p className="font-medium">{item.productName}</p>
                      <p className="text-xs text-muted-foreground">{item.productCode}</p>
                    </td>
                    <td className={cn(tdClass, "text-right tabular-nums")}>{fmt(item.salePrice)}</td>
                    <td className={cn(tdClass, "text-right tabular-nums")}>
                      <span>{fmt(item.unitCost)}</span>
                      {(item.gasCost > 0 || item.laborCost > 0) && (
                        <p className="text-xs text-muted-foreground">ing: {fmt(item.unitIngredientCost)}</p>
                      )}
                    </td>
                    <td className={cn(tdClass, "text-right tabular-nums")}>
                      <p>{fmt(item.margin)}</p>
                      <p className="text-xs text-muted-foreground">{fmtPct(item.marginPercent)}</p>
                    </td>
                    <td className={cn(tdClass, "text-right")}>
                      <span className={cn(
                        "inline-block px-2 py-0.5 rounded-full text-xs font-semibold tabular-nums",
                        cmvBadge(item.cmvPercent))}>
                        {fmtPct(item.cmvPercent)}
                      </span>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      )}
      <p className="text-xs text-muted-foreground">
        {sorted.length} de {items.length} prato{items.length !== 1 ? "s" : ""} ·
        CMV verde &lt;30% · amarelo 30–40% · vermelho &gt;40%
      </p>
    </div>
  );
}

// ── Employees Section ─────────────────────────────────────────────────────────

function EmployeesSection() {
  const { data: employees = [], isLoading } = useEmployees(true);
  const createMut  = useCreateEmployee();
  const updateMut  = useUpdateEmployee();

  const [adding, setAdding]       = useState(false);
  const [newName, setNewName]     = useState("");
  const [newRole, setNewRole]     = useState("");
  const [newSalary, setNewSalary] = useState("");
  const [editId, setEditId]       = useState<string | null>(null);
  const [editName, setEditName]   = useState("");
  const [editRole, setEditRole]   = useState("");
  const [editSalary, setEditSalary] = useState("");

  const today = new Date().toISOString().split("T")[0]; // "yyyy-MM-dd"

  const handleAdd = () => {
    if (!newName.trim() || !newSalary) return;
    createMut.mutate(
      { name: newName.trim(), role: newRole.trim(), admissionDate: today,
        monthlySalary: parseFloat(newSalary) || 0, notes: null },
      { onSuccess: () => { setAdding(false); setNewName(""); setNewRole(""); setNewSalary(""); toast.success("Funcionário adicionado!"); },
        onError:   () => toast.error("Erro ao adicionar funcionário.") });
  };

  const startEdit = (e: EmployeeDto) => {
    setEditId(e.id); setEditName(e.name); setEditRole(e.role);
    setEditSalary(String(e.monthlySalary));
  };

  const handleSaveEdit = (emp: EmployeeDto) => {
    updateMut.mutate(
      { id: emp.id, req: { name: editName.trim(), role: editRole.trim(),
          admissionDate: emp.admissionDate, monthlySalary: parseFloat(editSalary) || 0,
          notes: emp.notes, isActive: emp.isActive } },
      { onSuccess: () => { setEditId(null); toast.success("Funcionário atualizado!"); },
        onError:   () => toast.error("Erro ao atualizar.") });
  };

  const toggleActive = (emp: EmployeeDto) => {
    updateMut.mutate(
      { id: emp.id, req: { name: emp.name, role: emp.role,
          admissionDate: emp.admissionDate, monthlySalary: emp.monthlySalary,
          notes: emp.notes, isActive: !emp.isActive } },
      { onError: () => toast.error("Erro ao atualizar funcionário.") });
  };

  const activeTotal = employees.filter(e => e.isActive).reduce((s, e) => s + e.monthlySalary, 0);

  return (
    <div className="space-y-3">
      <div className="flex items-center justify-between">
        <div>
          <h2 className="text-sm font-semibold">Funcionários</h2>
          <p className="text-xs text-muted-foreground mt-0.5">
            Custo mensal total (ativos): <strong>{fmt(activeTotal)}</strong>
          </p>
        </div>
        {!adding && (
          <Button size="sm" variant="outline" onClick={() => setAdding(true)}>
            <Plus className="h-3.5 w-3.5 mr-1" /> Adicionar
          </Button>
        )}
      </div>

      {adding && (
        <div className="flex items-end gap-2 p-3 border border-dashed border-primary/40 rounded-xl flex-wrap">
          <div className="space-y-1 flex-1 min-w-[140px]">
            <Label className="text-xs">Nome</Label>
            <Input value={newName} onChange={e => setNewName(e.target.value)}
              placeholder="Nome completo" className="h-8 text-sm" autoFocus />
          </div>
          <div className="space-y-1 flex-1 min-w-[120px]">
            <Label className="text-xs">Função</Label>
            <Input value={newRole} onChange={e => setNewRole(e.target.value)}
              placeholder="Cozinheiro, Garçom…" className="h-8 text-sm" />
          </div>
          <div className="space-y-1 w-32">
            <Label className="text-xs">Salário/mês</Label>
            <div className="relative">
              <span className="absolute left-2 top-1/2 -translate-y-1/2 text-xs text-muted-foreground">R$</span>
              <Input value={newSalary} onChange={e => setNewSalary(e.target.value)}
                type="number" min={0} step={0.01} className="h-8 text-sm pl-7" placeholder="0,00" />
            </div>
          </div>
          <div className="flex gap-1">
            <button onClick={handleAdd} disabled={!newName.trim() || createMut.isPending}
              className="text-primary disabled:opacity-40">
              <Check className="h-4 w-4" />
            </button>
            <button onClick={() => { setAdding(false); setNewName(""); setNewRole(""); setNewSalary(""); }}
              className="text-muted-foreground">
              <X className="h-4 w-4" />
            </button>
          </div>
        </div>
      )}

      {isLoading ? (
        <div className="space-y-2">
          {[1, 2].map(i => <div key={i} className="h-10 rounded-lg bg-muted animate-pulse" />)}
        </div>
      ) : employees.length === 0 ? (
        <p className="text-sm text-muted-foreground py-4 text-center">
          Nenhum funcionário cadastrado.
        </p>
      ) : (
        <div className="rounded-xl border border-border overflow-hidden divide-y divide-border">
          {employees.map(emp => (
            <div key={emp.id} className={cn("flex items-center gap-2 px-4 py-3 text-sm",
              !emp.isActive && "opacity-50")}>
              {editId === emp.id ? (
                <>
                  <Input value={editName} onChange={e => setEditName(e.target.value)}
                    className="h-7 text-xs flex-1" />
                  <Input value={editRole} onChange={e => setEditRole(e.target.value)}
                    className="h-7 text-xs w-28" placeholder="Função" />
                  <div className="relative w-28">
                    <span className="absolute left-2 top-1/2 -translate-y-1/2 text-xs text-muted-foreground">R$</span>
                    <Input value={editSalary} onChange={e => setEditSalary(e.target.value)}
                      type="number" className="h-7 text-xs pl-7" />
                  </div>
                  <button onClick={() => handleSaveEdit(emp)} className="text-primary">
                    <Check className="h-3.5 w-3.5" />
                  </button>
                  <button onClick={() => setEditId(null)} className="text-muted-foreground">
                    <X className="h-3.5 w-3.5" />
                  </button>
                </>
              ) : (
                <>
                  <div className="flex-1">
                    <p className="font-medium">{emp.name}</p>
                    <p className="text-xs text-muted-foreground">{emp.role}</p>
                  </div>
                  <span className="tabular-nums text-sm">{fmt(emp.monthlySalary)}<span className="text-xs text-muted-foreground">/mês</span></span>
                  <button onClick={() => startEdit(emp)} className="text-muted-foreground hover:text-foreground">
                    <Pencil className="h-3 w-3" />
                  </button>
                  <button onClick={() => toggleActive(emp)}
                    className={cn("text-xs px-2 py-0.5 rounded-full border transition-colors",
                      emp.isActive
                        ? "border-border text-muted-foreground hover:border-destructive hover:text-destructive"
                        : "border-primary text-primary hover:bg-primary/10")}>
                    {emp.isActive ? "Desativar" : "Ativar"}
                  </button>
                </>
              )}
            </div>
          ))}
        </div>
      )}
    </div>
  );
}

// ── Expenses Section ──────────────────────────────────────────────────────────

function ExpensesSection({ from, to }: { from: string; to: string }) {
  const { data: expenses = [], isLoading } = useExpenses(from, to);
  const createMut = useCreateExpense();
  const deleteMut = useDeleteExpense();

  const [adding, setAdding]       = useState(false);
  const [desc, setDesc]           = useState("");
  const [cat, setCat]             = useState(EXPENSE_CATEGORIES[0]);
  const [amount, setAmount]       = useState("");
  const [isRecurring, setIsRecurring] = useState(false);

  const handleAdd = () => {
    if (!desc.trim() || !amount) return;
    createMut.mutate(
      { description: desc.trim(), category: cat, amount: parseFloat(amount) || 0,
        competenceDate: from, paymentDate: null, isRecurring },
      { onSuccess: () => { setAdding(false); setDesc(""); setAmount(""); setIsRecurring(false); toast.success("Despesa adicionada!"); },
        onError:   () => toast.error("Erro ao adicionar despesa.") });
  };

  const handleDelete = (id: string) => {
    deleteMut.mutate(id, {
      onSuccess: () => toast.success("Despesa removida."),
      onError:   () => toast.error("Erro ao remover despesa."),
    });
  };

  const total = expenses.reduce((s, e) => s + e.amount, 0);

  return (
    <div className="space-y-3">
      <div className="flex items-center justify-between">
        <div>
          <h2 className="text-sm font-semibold">Despesas Gerais</h2>
          <p className="text-xs text-muted-foreground mt-0.5">
            Total do período: <strong>{fmt(total)}</strong>
          </p>
        </div>
        {!adding && (
          <Button size="sm" variant="outline" onClick={() => setAdding(true)}>
            <Plus className="h-3.5 w-3.5 mr-1" /> Adicionar
          </Button>
        )}
      </div>

      {adding && (
        <div className="flex items-end gap-2 p-3 border border-dashed border-primary/40 rounded-xl flex-wrap">
          <div className="space-y-1 flex-1 min-w-[160px]">
            <Label className="text-xs">Descrição</Label>
            <Input value={desc} onChange={e => setDesc(e.target.value)}
              placeholder="Ex: Conta de energia" className="h-8 text-sm" autoFocus />
          </div>
          <div className="space-y-1 w-36">
            <Label className="text-xs">Categoria</Label>
            <Select value={cat} onValueChange={v => setCat(v)}>
              <SelectTrigger className="h-8 text-sm">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                {EXPENSE_CATEGORIES.map(c => (
                  <SelectItem key={c} value={c}>{c}</SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>
          <div className="space-y-1 w-28">
            <Label className="text-xs">Valor</Label>
            <div className="relative">
              <span className="absolute left-2 top-1/2 -translate-y-1/2 text-xs text-muted-foreground">R$</span>
              <Input value={amount} onChange={e => setAmount(e.target.value)}
                type="number" min={0} step={0.01} className="h-8 text-sm pl-7" placeholder="0,00" />
            </div>
          </div>
          <div className="flex items-center gap-1.5 pb-1">
            <input type="checkbox" id="recurring" checked={isRecurring}
              onChange={e => setIsRecurring(e.target.checked)}
              className="h-3.5 w-3.5 accent-primary" />
            <Label htmlFor="recurring" className="text-xs cursor-pointer">Recorrente</Label>
          </div>
          <div className="flex gap-1">
            <button onClick={handleAdd} disabled={!desc.trim() || !amount || createMut.isPending}
              className="text-primary disabled:opacity-40">
              <Check className="h-4 w-4" />
            </button>
            <button onClick={() => { setAdding(false); setDesc(""); setAmount(""); setIsRecurring(false); }}
              className="text-muted-foreground">
              <X className="h-4 w-4" />
            </button>
          </div>
        </div>
      )}

      {isLoading ? (
        <div className="space-y-2">
          {[1, 2].map(i => <div key={i} className="h-10 rounded-lg bg-muted animate-pulse" />)}
        </div>
      ) : expenses.length === 0 ? (
        <p className="text-sm text-muted-foreground py-4 text-center">
          Nenhuma despesa neste período.
        </p>
      ) : (
        <div className="rounded-xl border border-border overflow-hidden divide-y divide-border">
          {expenses.map(exp => (
            <div key={exp.id} className="flex items-center gap-3 px-4 py-3 text-sm">
              <div className="flex-1">
                <p className="font-medium">{exp.description}</p>
                <p className="text-xs text-muted-foreground">
                  {exp.category}{exp.isRecurring ? " · recorrente" : ""}
                </p>
              </div>
              <span className="tabular-nums font-medium">{fmt(exp.amount)}</span>
              <button onClick={() => handleDelete(exp.id)}
                className="text-muted-foreground hover:text-destructive transition-colors">
                <Trash2 className="h-3.5 w-3.5" />
              </button>
            </div>
          ))}
        </div>
      )}
    </div>
  );
}

// ── Insight Cards ─────────────────────────────────────────────────────────────

interface InsightCardsProps {
  cmvItems:   CmvReportItemDto[];
  summary:    FinanceiroSummaryDto | undefined;
  prevSummary: FinanceiroSummaryDto | undefined;
  employees:  EmployeeDto[];
}

function InsightCards({ cmvItems, summary, prevSummary, employees }: InsightCardsProps) {
  const highCmvCount = cmvItems.filter(i => i.cmvPercent > 35).length;

  const topEmployee = employees
    .filter(e => e.isActive)
    .sort((a, b) => b.monthlySalary - a.monthlySalary)[0];

  const prevProfit  = prevSummary?.operationalProfit ?? 0;
  const currProfit  = summary?.operationalProfit     ?? 0;
  const profitDelta = prevSummary ? currProfit - prevProfit : null;

  return (
    <div className="grid grid-cols-1 sm:grid-cols-3 gap-3">
      {/* Insight 1: High CMV dishes */}
      <div className="rounded-xl border border-border bg-card p-4 space-y-1">
        <div className="flex items-center gap-2">
          <Lightbulb className="h-4 w-4 text-yellow-500" />
          <span className="text-xs font-medium text-muted-foreground">CMV elevado (&gt;35%)</span>
        </div>
        {highCmvCount === 0 ? (
          <p className="text-sm font-semibold text-green-600 dark:text-green-400">
            Nenhum prato acima de 35% 🎉
          </p>
        ) : (
          <p className="text-sm font-semibold text-red-600 dark:text-red-400">
            {highCmvCount} prato{highCmvCount !== 1 ? "s" : ""} acima de 35%
          </p>
        )}
        <p className="text-xs text-muted-foreground">
          {cmvItems.length} fichas técnicas no total.
        </p>
      </div>

      {/* Insight 2: Lucro vs mês anterior */}
      <div className="rounded-xl border border-border bg-card p-4 space-y-1">
        <div className="flex items-center gap-2">
          <TrendingUp className="h-4 w-4 text-blue-500" />
          <span className="text-xs font-medium text-muted-foreground">Lucro vs mês anterior</span>
        </div>
        {profitDelta === null ? (
          <p className="text-sm text-muted-foreground">Carregando…</p>
        ) : (
          <p className={cn("text-sm font-semibold tabular-nums",
            profitDelta >= 0 ? "text-green-600 dark:text-green-400" : "text-red-600 dark:text-red-400")}>
            {profitDelta >= 0 ? "+" : ""}{fmt(profitDelta)}
          </p>
        )}
        <p className="text-xs text-muted-foreground">
          Lucro atual: {fmt(currProfit)}
        </p>
      </div>

      {/* Insight 3: Top employee cost */}
      <div className="rounded-xl border border-border bg-card p-4 space-y-1">
        <div className="flex items-center gap-2">
          <Users className="h-4 w-4 text-purple-500" />
          <span className="text-xs font-medium text-muted-foreground">Maior custo — pessoal</span>
        </div>
        {!topEmployee ? (
          <p className="text-sm text-muted-foreground">Nenhum funcionário ativo.</p>
        ) : (
          <>
            <p className="text-sm font-semibold">{topEmployee.name}</p>
            <p className="text-xs text-muted-foreground">
              {fmt(topEmployee.monthlySalary)}/mês · {topEmployee.role}
            </p>
          </>
        )}
      </div>
    </div>
  );
}

// ── Main page ─────────────────────────────────────────────────────────────────

export default function FinanceiroPage() {
  const now = new Date();
  const [month, setMonth] = useState(now.getMonth() + 1);
  const [year,  setYear]  = useState(now.getFullYear());

  const { from, to }             = monthBounds(year, month);
  const { from: prevFrom, to: prevTo } = prevMonthBounds(year, month);

  const { data: cmvData,   isLoading: cmvLoading   } = useCmvReport();
  const { data: summary,   isLoading: sumLoading   } = useFinanceiroSummary(from, to);
  const { data: prevSummary                         } = useFinanceiroSummary(prevFrom, prevTo);
  const { data: employees = []                      } = useEmployees(true);

  const isLoading = cmvLoading || sumLoading;

  return (
    <div className="p-6 space-y-8">
      <PageHeader
        title="Financeiro"
        description="CMV por prato, pessoal, despesas e KPIs do período selecionado."
      />

      {/* ── Period picker ─────────────────────────────────────────────────── */}
      <div className="flex items-center gap-3 flex-wrap">
        <Select value={String(month)} onValueChange={v => setMonth(Number(v))}>
          <SelectTrigger className="w-36 text-sm">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            {MONTHS.map((m, i) => (
              <SelectItem key={i + 1} value={String(i + 1)}>{m}</SelectItem>
            ))}
          </SelectContent>
        </Select>
        <div className="flex items-center gap-2">
          <Button variant="outline" size="icon" className="h-9 w-9 text-sm"
            onClick={() => setYear(y => y - 1)}>‹</Button>
          <span className="tabular-nums text-sm font-medium w-12 text-center">{year}</span>
          <Button variant="outline" size="icon" className="h-9 w-9 text-sm"
            onClick={() => setYear(y => y + 1)} disabled={year >= now.getFullYear()}>›</Button>
        </div>
        <span className="text-xs text-muted-foreground">{from} → {to}</span>
      </div>

      {/* ── KPI Cards — linha 1: faturamento ─────────────────────────────── */}
      <div className="space-y-3">
        <h2 className="text-sm font-semibold text-foreground">Resultado do período</h2>
        <div className="grid grid-cols-2 md:grid-cols-4 gap-4">
          <KpiCard icon={DollarSign} label="Faturamento bruto"
            value={isLoading ? "—" : fmt(summary?.revenue ?? 0)}
            sub={isLoading ? undefined : `${summary?.ordersCount ?? 0} comanda(s)`}
            color="text-blue-600" />
          <KpiCard icon={ShoppingBag} label="Custo de mercadoria (CMG)"
            value={isLoading ? "—" : fmt(summary?.totalCostOfGoodsSold ?? 0)}
            sub="CMG do período" color="text-orange-600" />
          <KpiCard icon={TrendingUp} label="CMV% ponderado"
            value={isLoading ? "—" : fmtPct(summary?.weightedCmvPercent ?? 0)}
            sub="Baseado nos pedidos"
            color={isLoading ? "text-muted-foreground" : cmvColor(summary?.weightedCmvPercent ?? 0)} />
          <KpiCard icon={TrendingDown} label="Margem bruta"
            value={isLoading ? "—" : fmt(summary?.grossMargin ?? 0)}
            sub={isLoading || !summary?.revenue ? undefined
              : fmtPct(100 - (summary.totalCostOfGoodsSold / summary.revenue) * 100)}
            color="text-green-600" />
        </div>

        {/* ── KPI Cards — linha 2: custos fixos e lucro ────────────────── */}
        <div className="grid grid-cols-2 md:grid-cols-4 gap-4">
          <KpiCard icon={Users} label="Custo de pessoal"
            value={isLoading ? "—" : fmt(summary?.totalPersonnelCost ?? 0)}
            sub="Salários mensais (ativos)" color="text-purple-600" />
          <KpiCard icon={Receipt} label="Despesas do período"
            value={isLoading ? "—" : fmt(summary?.totalFixedExpenses ?? 0)}
            sub="Energia, água, etc." color="text-rose-600" />
          <KpiCard icon={TrendingUp} label="Lucro operacional"
            value={isLoading ? "—" : fmt(summary?.operationalProfit ?? 0)}
            sub="Margem − pessoal − despesas"
            color={isLoading ? "text-muted-foreground"
              : (summary?.operationalProfit ?? 0) >= 0
                ? "text-green-600" : "text-red-600"} />
          <KpiCard icon={Target} label="Ponto de equilíbrio"
            value={isLoading ? "—" : summary?.breakEvenRevenue
              ? fmt(summary.breakEvenRevenue) : "N/D"}
            sub="Faturamento mínimo necessário" color="text-indigo-600" />
        </div>
      </div>

      {/* ── Insights ──────────────────────────────────────────────────────── */}
      <div className="space-y-2">
        <h2 className="text-sm font-semibold">Insights</h2>
        <InsightCards
          cmvItems={cmvData?.items ?? []}
          summary={summary}
          prevSummary={prevSummary}
          employees={employees}
        />
      </div>

      {/* ── CMV Table ─────────────────────────────────────────────────────── */}
      <div className="space-y-2">
        <div>
          <h2 className="text-sm font-semibold">CMV por prato</h2>
          <p className="text-xs text-muted-foreground mt-0.5">
            Custo atual baseado nas fichas técnicas. Ordene por coluna clicando no cabeçalho.
          </p>
        </div>
        {cmvLoading ? (
          <div className="space-y-2">
            {[1, 2, 3].map(i => <div key={i} className="h-14 rounded-xl bg-muted animate-pulse" />)}
          </div>
        ) : (
          <CmvTable items={cmvData?.items ?? []} />
        )}
      </div>

      {/* ── Funcionários ──────────────────────────────────────────────────── */}
      <EmployeesSection />

      {/* ── Despesas ──────────────────────────────────────────────────────── */}
      <ExpensesSection from={from} to={to} />
    </div>
  );
}
```

- [ ] **Step 2: TypeScript check**

```
cd nexo-main
npx tsc --noEmit 2>&1 | head -30
```

Expected: 0 erros.

- [ ] **Step 3: Commit**

```bash
cd nexo-main
git add src/modules/restaurante/pages/FinanceiroPage.tsx
git commit -m "feat(restaurante): extend FinanceiroPage with employees section, expenses section, expanded KPIs and insights"
```

---

## Self-Review

**1. Spec coverage:**
- ✅ `RestEmployee` (nome, função, dataAdmissão, salário mensal, observações) — Task 1
- ✅ `RestExpense` (descrição, categoria, valor, dataCompetência, dataPagamento, recorrente) — Task 1
- ✅ Summary atualizado: custo pessoal, despesas fixas, lucro operacional, ponto de equilíbrio — Task 4
- ✅ Seção "Funcionários" com lista + formulário inline + ativar/desativar — Task 7
- ✅ Seção "Despesas Gerais" com lista + formulário inline + delete — Task 7
- ✅ KPI cards expandidos: lucro operacional + ponto de equilíbrio — Task 7
- ✅ Cards de insights: CMV>35%, comparativo mês anterior, funcionário com maior custo — Task 7
- ✅ Categorias pré-definidas: Energia, Gás, Água, Internet, Impostos, Manutenção, Aluguel, Embalagem, Publicidade, Outros — Task 5

**2. Placeholder scan:** Nenhum. Todo código está completo e específico.

**3. Type consistency:**
- `RestEmployee.Create(tenantId, name, role, admissionDate, monthlySalary, notes)` — usado em `EmployeesController.Create` com exatamente os mesmos parâmetros.
- `RestExpense.Create(tenantId, description, category, amount, competenceDate, paymentDate, isRecurring)` — usado em `ExpensesController.Create` com os mesmos parâmetros.
- `EmployeeDto` definido no `EmployeesController.cs` — usado no teste com `ReadFromJsonAsync<EmployeeDto>()` e também em `use-employees-expenses.ts` com interface separada mas os mesmos campos.
- `FinanceiroSummaryDto` C# com `TotalPersonnelCost`, `TotalFixedExpenses`, `OperationalProfit`, `BreakEvenRevenue` — espelhado exatamente na interface TypeScript em `financeiro.api.ts`.
- `EXPENSE_CATEGORIES` em `employees-expenses.api.ts` — importado e usado em `ExpensesSection` em `FinanceiroPage.tsx`.
- `useEmployees(includeInactive)` → `fetchEmployees(includeInactive)` → `GET /restaurante/employees?includeInactive=...` — cadeia consistente.
- `useExpenses(from, to)` → `fetchExpenses(from, to)` → `GET /restaurante/expenses?from=&to=` — consistente.
