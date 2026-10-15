# Orken Service PR6 — Payments / Pagamentos Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add `SvcPayment` — a manual record that a payment was received against an `SvcOrder` **or** an `SvcCustomerPackage` — with partial-payment tracking, operational void, and paid/remaining summaries.

**Architecture:** A single store-scoped entity (`SvcPayment`, no aggregate/children) with a 2-state status (Paid → Voided). The application service resolves the target (order or package), **reads** its total, computes `remaining = total − Σ(Paid payments)`, and rejects over-payment. **It is purely a record:** it never touches the order's `TotalAmount`/status, the package's balance/status, and never creates any `FinancialMovement`/`CashMovement`, Stripe, checkout, or invoice. Migration is strictly additive (one `CreateTable` + indexes + new FKs to existing tables).

**Tech Stack:** .NET 8, EF Core 8 (Npgsql/PostgreSQL, `timestamptz`), FluentValidation, xUnit + FluentAssertions + Testcontainers.

---

## Design decisions (locked)

| Concern | Decision |
|---|---|
| Entity | `SvcPayment : StoreEntity` (no `IsActive` — uses `Status`) → `ConfigureStoreScopedSvcEntityNoActive`. Single entity, no children. |
| Method enum | `SvcPaymentMethod { Cash, Pix, DebitCard, CreditCard, BankTransfer, Other }`, `.HasConversion<string>()`. |
| Status enum | `SvcPaymentStatus { Paid, Voided }`. No Pending/Refunded (out of scope). Initial = Paid; Paid→Voided terminal; no double void. |
| Target | Exactly one of `OrderId` / `CustomerPackageId` (XOR). Two domain factories (`CreateForOrder`/`CreateForCustomerPackage`); XOR also guarded in the private factory + the request validator (400 for both/neither). |
| CustomerId | **Derived from the target** (order/package `CustomerId`), never from the payload. |
| Remaining | Service computes `target total − Σ(Status==Paid amounts)`; voided payments don't count. `Amount > remaining` → 422. |
| No side effects | The service only **reads** the order/package. **Never** calls `_orders.Update`, `order.RecalculateTotal`, `order.ChangeStatus`, `_customerPackages.Update`, nor any `cp.*` mutation; **no** `FinancialMovement`/`CashMovement`/Stripe/checkout. |
| Void | `POST /payments/{id}/void` → Paid→Voided, `VoidedAt = UtcNow`, optional `VoidReason` (≤500). No delete — history stays. |
| Amount | `decimal numeric(18,2)`. `PaidAt`/`VoidedAt` `timestamptz`; `PaidAt` must be UTC (validator → 400). |
| Status codes | payload (amount≤0, bad method, non-UTC paidAt, not-exactly-one-target, lengths) → 400; missing/cross-tenant target → 404; domain (cancelled target, amount>remaining, double void) → 422. |

## File structure

**Domain** (`Nexo.Domain/Modules/Service/`): `SvcPaymentMethod.cs`, `SvcPaymentStatus.cs`, `SvcPayment.cs`
**Application** (`Nexo.Application/Modules/Service/`): `SvcPaymentDtos.cs`, `SvcPaymentService.cs`, modify `SvcValidators.cs`, `Interfaces/ISvcPaymentRepository.cs`, modify `DependencyInjection.cs`
**Infrastructure** (`Nexo.Infrastructure/`): `Persistence/Configurations/Modules/Service/SvcPaymentConfiguration.cs`, `Repositories/Modules/Service/SvcPaymentRepository.cs`, modify `DependencyInjection.cs`, modify `Persistence/NexoDbContext.cs`, migration `<ts>_AddServicePayments.cs`
**API** (`Nexo.Api/`): `Controllers/Modules/Service/PaymentsController.cs`
**Tests**: `Nexo.UnitTests/Service/SvcPaymentTests.cs`, `Nexo.IntegrationTests/Service/ServicePaymentsTests.cs`

---

## Task 1: Domain — enums + `SvcPayment` (unit tested)

**Files:** `SvcPaymentMethod.cs`, `SvcPaymentStatus.cs`, `SvcPayment.cs`, `SvcPaymentTests.cs`.

- [ ] **Step 1: Write failing unit tests**

```csharp
using FluentAssertions;
using Nexo.Domain.Exceptions;
using Nexo.Domain.Modules.Service;
using Xunit;

namespace Nexo.UnitTests.Service;

public class SvcPaymentTests
{
    private static readonly Guid T = Guid.NewGuid();
    private static readonly Guid Cust = Guid.NewGuid();
    private static readonly Guid Order = Guid.NewGuid();
    private static readonly Guid Pkg = Guid.NewGuid();
    private static readonly DateTime Paid = new(2026, 6, 18, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void CreateForOrder_sets_fields_paid_and_target()
    {
        var p = SvcPayment.CreateForOrder(T, Cust, Order, 100m, SvcPaymentMethod.Pix, Paid, "ext", "n");
        p.CustomerId.Should().Be(Cust);
        p.OrderId.Should().Be(Order);
        p.CustomerPackageId.Should().BeNull();
        p.Amount.Should().Be(100m);
        p.Method.Should().Be(SvcPaymentMethod.Pix);
        p.Status.Should().Be(SvcPaymentStatus.Paid);
        p.PaidAt.Should().Be(Paid);
    }

    [Fact]
    public void CreateForCustomerPackage_sets_package_target()
    {
        var p = SvcPayment.CreateForCustomerPackage(T, Cust, Pkg, 50m, SvcPaymentMethod.Cash, Paid, null, null);
        p.CustomerPackageId.Should().Be(Pkg);
        p.OrderId.Should().BeNull();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Create_with_non_positive_amount_throws(decimal amount)
        => ((Action)(() => SvcPayment.CreateForOrder(T, Cust, Order, amount, SvcPaymentMethod.Cash, Paid, null, null)))
            .Should().Throw<DomainException>();

    [Fact]
    public void Create_with_empty_customer_throws()
        => ((Action)(() => SvcPayment.CreateForOrder(T, Guid.Empty, Order, 10m, SvcPaymentMethod.Cash, Paid, null, null)))
            .Should().Throw<DomainException>();

    [Fact]
    public void Void_marks_voided_and_records_reason_and_time()
    {
        var p = SvcPayment.CreateForOrder(T, Cust, Order, 100m, SvcPaymentMethod.Pix, Paid, null, null);
        p.Void("wrong entry");
        p.Status.Should().Be(SvcPaymentStatus.Voided);
        p.VoidReason.Should().Be("wrong entry");
        p.VoidedAt.Should().NotBeNull();
    }

    [Fact]
    public void Void_twice_throws()
    {
        var p = SvcPayment.CreateForOrder(T, Cust, Order, 100m, SvcPaymentMethod.Pix, Paid, null, null);
        p.Void(null);
        ((Action)(() => p.Void(null))).Should().Throw<DomainException>();
    }
}
```

- [ ] **Step 2: Run, verify fail.**

- [ ] **Step 3: Implement `SvcPaymentMethod.cs`**

```csharp
namespace Nexo.Domain.Modules.Service;

/// <summary>How a Service payment was received. Stored as a string.</summary>
public enum SvcPaymentMethod { Cash, Pix, DebitCard, CreditCard, BankTransfer, Other }
```

- [ ] **Step 4: Implement `SvcPaymentStatus.cs`**

```csharp
namespace Nexo.Domain.Modules.Service;

/// <summary>Status of a Service payment record. Paid → Voided (terminal). Stored as a string.</summary>
public enum SvcPaymentStatus { Paid, Voided }
```

- [ ] **Step 5: Implement `SvcPayment.cs`**

```csharp
using Nexo.Domain.Common;
using Nexo.Domain.Exceptions;

namespace Nexo.Domain.Modules.Service;

/// <summary>
/// A manual record that a payment was received in the Service context, against exactly one target —
/// an <c>SvcOrder</c> OR an <c>SvcCustomerPackage</c>. Store-scoped. This is ONLY a record: it never
/// touches the order total/status, the package balance/status, nor any global financial/cash entity.
/// Status: Paid → Voided (operational correction; no delete — history is preserved).
/// </summary>
public class SvcPayment : StoreEntity
{
    private SvcPayment() { }
    private SvcPayment(Guid tenantId) : base(tenantId) { }

    public Guid             CustomerId        { get; private set; }
    public Guid?            OrderId           { get; private set; }
    public Guid?            CustomerPackageId { get; private set; }
    public decimal          Amount            { get; private set; }
    public SvcPaymentMethod Method            { get; private set; }
    public SvcPaymentStatus Status            { get; private set; }
    public DateTime         PaidAt            { get; private set; }
    public string?          ExternalReference { get; private set; }
    public string?          Notes             { get; private set; }
    public string?          VoidReason        { get; private set; }
    public DateTime?        VoidedAt          { get; private set; }

    public static SvcPayment CreateForOrder(
        Guid tenantId, Guid customerId, Guid orderId, decimal amount, SvcPaymentMethod method,
        DateTime paidAt, string? externalReference, string? notes)
        => Create(tenantId, customerId, orderId, null, amount, method, paidAt, externalReference, notes);

    public static SvcPayment CreateForCustomerPackage(
        Guid tenantId, Guid customerId, Guid customerPackageId, decimal amount, SvcPaymentMethod method,
        DateTime paidAt, string? externalReference, string? notes)
        => Create(tenantId, customerId, null, customerPackageId, amount, method, paidAt, externalReference, notes);

    private static SvcPayment Create(
        Guid tenantId, Guid customerId, Guid? orderId, Guid? customerPackageId, decimal amount,
        SvcPaymentMethod method, DateTime paidAt, string? externalReference, string? notes)
    {
        if (customerId == Guid.Empty) throw new DomainException("Customer is required.");
        if (amount <= 0m)             throw new DomainException("Amount must be positive.");
        if ((orderId is null) == (customerPackageId is null))
            throw new DomainException("Exactly one of OrderId or CustomerPackageId must be set.");

        return new SvcPayment(tenantId)
        {
            CustomerId = customerId, OrderId = orderId, CustomerPackageId = customerPackageId,
            Amount = amount, Method = method, Status = SvcPaymentStatus.Paid, PaidAt = paidAt,
            ExternalReference = externalReference?.Trim(), Notes = notes?.Trim(),
        };
    }

    public void Void(string? reason)
    {
        if (Status != SvcPaymentStatus.Paid)
            throw new DomainException($"Only a Paid payment can be voided (current: {Status}).");
        Status     = SvcPaymentStatus.Voided;
        VoidReason = reason?.Trim();
        VoidedAt   = DateTime.UtcNow;
        SetUpdatedAt();
    }
}
```

- [ ] **Step 6: Run, verify pass. Commit** — `feat(service): SvcPayment domain entity (PR6)`

---

## Task 2: EF config + DbSet (model verified)

- [ ] **Step 1: `SvcPaymentConfiguration.cs`**

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nexo.Domain.Entities;
using Nexo.Domain.Modules.Service;

namespace Nexo.Infrastructure.Persistence.Configurations.Modules.Service;

public class SvcPaymentConfiguration : IEntityTypeConfiguration<SvcPayment>
{
    public void Configure(EntityTypeBuilder<SvcPayment> builder)
    {
        builder.ConfigureStoreScopedSvcEntityNoActive("svc_payments");

        builder.Property(x => x.CustomerId).HasColumnName("customer_id").IsRequired();
        builder.Property(x => x.OrderId).HasColumnName("order_id");
        builder.Property(x => x.CustomerPackageId).HasColumnName("customer_package_id");
        builder.Property(x => x.Amount).HasColumnName("amount").HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(x => x.Method).HasColumnName("method").HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.PaidAt).HasColumnName("paid_at").HasColumnType("timestamptz").IsRequired();
        builder.Property(x => x.ExternalReference).HasColumnName("external_reference").HasMaxLength(200);
        builder.Property(x => x.Notes).HasColumnName("notes").HasMaxLength(2000);
        builder.Property(x => x.VoidReason).HasColumnName("void_reason").HasMaxLength(500);
        builder.Property(x => x.VoidedAt).HasColumnName("voided_at").HasColumnType("timestamptz");

        builder.HasOne<Customer>().WithMany().HasForeignKey(x => x.CustomerId)
            .HasConstraintName("fk_svc_payments_customers").OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<SvcOrder>().WithMany().HasForeignKey(x => x.OrderId)
            .HasConstraintName("fk_svc_payments_orders").OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<SvcCustomerPackage>().WithMany().HasForeignKey(x => x.CustomerPackageId)
            .HasConstraintName("fk_svc_payments_customer_packages").OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.CustomerId).HasDatabaseName("ix_svc_payments_customer_id");
        builder.HasIndex(x => x.OrderId).HasDatabaseName("ix_svc_payments_order_id");
        builder.HasIndex(x => x.CustomerPackageId).HasDatabaseName("ix_svc_payments_customer_package_id");
        builder.HasIndex("TenantId", "StoreId", "Status").HasDatabaseName("ix_svc_payments_tenant_store_status");
        builder.HasIndex(x => x.PaidAt).HasDatabaseName("ix_svc_payments_paid_at");
    }
}
```

- [ ] **Step 2: DbSet in `NexoDbContext.cs`** (after `SvcPackageUsages`):

```csharp
        public DbSet<SvcPayment>             SvcPayments             => Set<SvcPayment>();
```

- [ ] **Step 3: Build → success. Commit** — `feat(service): EF config + DbSet for SvcPayment (PR6)`

---

## Task 3: Additive migration

- [ ] **Step 1:** `dotnet ef migrations add AddServicePayments -p src/Nexo.Infrastructure -s src/Nexo.Api -o Persistence/Migrations`
- [ ] **Step 2: PROVE additive.** `Up()` must contain ONLY `CreateTable("svc_payments")` + `CreateIndex` + FKs to existing tables (customers, svc_orders, svc_customer_packages). No Drop/Alter/Rename/AddColumn on existing tables. STOP if anything else appears.
- [ ] **Step 3:** `has-pending-model-changes` → "No changes…".
- [ ] **Step 4: Commit** — `feat(service): additive migration AddServicePayments (PR6)`

---

## Task 4: Repository + interface + DI

- [ ] **`ISvcPaymentRepository.cs`**

```csharp
using Nexo.Domain.Modules.Service;

namespace Nexo.Application.Modules.Service.Interfaces;

/// <summary>Repository for SvcPayment. Tenant + store isolation via the EF global query filter.</summary>
public interface ISvcPaymentRepository
{
    Task<SvcPayment?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<SvcPayment>> GetAllAsync(
        Guid? customerId, Guid? orderId, Guid? customerPackageId, SvcPaymentMethod? method,
        SvcPaymentStatus? status, DateTime? from, DateTime? to, CancellationToken ct = default);
    Task<IReadOnlyList<SvcPayment>> GetByOrderAsync(Guid orderId, CancellationToken ct = default);
    Task<IReadOnlyList<SvcPayment>> GetByCustomerPackageAsync(Guid customerPackageId, CancellationToken ct = default);
    Task AddAsync(SvcPayment entity, CancellationToken ct = default);
    void Update(SvcPayment entity);
    Task SaveChangesAsync(CancellationToken ct = default);
}
```

- [ ] **`SvcPaymentRepository.cs`**

```csharp
using Microsoft.EntityFrameworkCore;
using Nexo.Application.Modules.Service.Interfaces;
using Nexo.Domain.Modules.Service;
using Nexo.Infrastructure.Persistence;

namespace Nexo.Infrastructure.Repositories.Modules.Service;

public class SvcPaymentRepository : ISvcPaymentRepository
{
    private readonly NexoDbContext _context;
    public SvcPaymentRepository(NexoDbContext context) => _context = context;

    public async Task<SvcPayment?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await _context.SvcPayments.FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<IReadOnlyList<SvcPayment>> GetAllAsync(
        Guid? customerId, Guid? orderId, Guid? customerPackageId, SvcPaymentMethod? method,
        SvcPaymentStatus? status, DateTime? from, DateTime? to, CancellationToken ct = default)
    {
        var q = _context.SvcPayments.AsQueryable();
        if (customerId is { } c)        q = q.Where(x => x.CustomerId == c);
        if (orderId is { } o)           q = q.Where(x => x.OrderId == o);
        if (customerPackageId is { } p) q = q.Where(x => x.CustomerPackageId == p);
        if (method is { } m)            q = q.Where(x => x.Method == m);
        if (status is { } s)            q = q.Where(x => x.Status == s);
        if (from is { } f)              q = q.Where(x => x.PaidAt >= f);
        if (to is { } t)                q = q.Where(x => x.PaidAt <= t);
        return await q.OrderByDescending(x => x.PaidAt).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<SvcPayment>> GetByOrderAsync(Guid orderId, CancellationToken ct = default)
        => await _context.SvcPayments.Where(x => x.OrderId == orderId).OrderByDescending(x => x.PaidAt).ToListAsync(ct);

    public async Task<IReadOnlyList<SvcPayment>> GetByCustomerPackageAsync(Guid customerPackageId, CancellationToken ct = default)
        => await _context.SvcPayments.Where(x => x.CustomerPackageId == customerPackageId).OrderByDescending(x => x.PaidAt).ToListAsync(ct);

    public async Task AddAsync(SvcPayment entity, CancellationToken ct = default)
        => await _context.SvcPayments.AddAsync(entity, ct);

    public void Update(SvcPayment entity) => _context.SvcPayments.Update(entity);

    public async Task SaveChangesAsync(CancellationToken ct = default)
        => await _context.SaveChangesAsync(ct);
}
```

Register in Infrastructure `DependencyInjection.cs`: `services.AddScoped<ISvcPaymentRepository, SvcPaymentRepository>();`. Build → commit `feat(service): SvcPayment repository + DI (PR6)`.

---

## Task 5: DTOs + validators

- [ ] **`SvcPaymentDtos.cs`**

```csharp
using Nexo.Domain.Modules.Service;

namespace Nexo.Application.Modules.Service;

public sealed record SvcPaymentDto(
    Guid             Id,
    Guid             StoreId,
    Guid             CustomerId,
    Guid?            OrderId,
    Guid?            CustomerPackageId,
    decimal          Amount,
    SvcPaymentMethod Method,
    SvcPaymentStatus Status,
    DateTime         PaidAt,
    string?          ExternalReference,
    string?          Notes,
    string?          VoidReason,
    DateTime?        VoidedAt,
    DateTime         CreatedAt,
    DateTime         UpdatedAt);

public sealed record SvcPaymentSummaryDto(
    Guid    TargetId,
    string  TargetType,         // "Order" | "CustomerPackage"
    decimal TotalAmount,
    decimal PaidAmount,
    decimal VoidedAmount,
    decimal RemainingAmount,
    bool    IsFullyPaid);

public sealed record CreateSvcPaymentRequest(
    decimal          Amount,
    SvcPaymentMethod Method,
    DateTime         PaidAt,
    Guid?            OrderId           = null,
    Guid?            CustomerPackageId = null,
    string?          ExternalReference = null,
    string?          Notes             = null);

public sealed record VoidSvcPaymentRequest(string? Reason = null);
```

- [ ] **Validators** (append to `SvcValidators.cs`):

```csharp
public class CreateSvcPaymentRequestValidator : AbstractValidator<CreateSvcPaymentRequest>
{
    public CreateSvcPaymentRequestValidator()
    {
        RuleFor(x => x.Amount).GreaterThan(0m).WithMessage("Amount must be positive.");
        RuleFor(x => x.Method).IsInEnum().WithMessage("Invalid payment method.");
        RuleFor(x => x.PaidAt).Must(d => d.Kind == DateTimeKind.Utc)
            .WithMessage("PaidAt must be UTC (use a trailing Z).");
        RuleFor(x => x).Must(r => (r.OrderId is null) != (r.CustomerPackageId is null))
            .WithMessage("Exactly one of OrderId or CustomerPackageId must be set.");
        RuleFor(x => x.ExternalReference).MaximumLength(200).When(x => x.ExternalReference is not null);
        RuleFor(x => x.Notes).MaximumLength(2000).When(x => x.Notes is not null);
    }
}

public class VoidSvcPaymentRequestValidator : AbstractValidator<VoidSvcPaymentRequest>
{
    public VoidSvcPaymentRequestValidator()
        => RuleFor(x => x.Reason).MaximumLength(500).When(x => x.Reason is not null);
}
```

Build → commit `feat(service): payment DTOs + validators (PR6)`.

---

## Task 6: `SvcPaymentService` + `PaymentsController` + DI

- [ ] **`SvcPaymentService.cs`**

```csharp
using Nexo.Application.Common.Interfaces;
using Nexo.Application.Modules.Service.Interfaces;
using Nexo.Domain.Exceptions;
using Nexo.Domain.Modules.Service;

namespace Nexo.Application.Modules.Service;

/// <summary>
/// Use cases for SvcPayment — manual payment records against an order or a customer package.
/// Resolves the target (cross-tenant invisible → 404), rejects a cancelled target (422), and
/// rejects an amount that exceeds the remaining balance (422). The CustomerId is taken from the
/// target. This service NEVER mutates the order (total/status) or the package (balance/status),
/// and never creates any global financial/cash entity — it only reads to compute remaining.
/// </summary>
public class SvcPaymentService
{
    private readonly ISvcPaymentRepository         _payments;
    private readonly ISvcOrderRepository           _orders;
    private readonly ISvcCustomerPackageRepository _customerPackages;
    private readonly ICurrentTenant                _currentTenant;

    public SvcPaymentService(
        ISvcPaymentRepository payments, ISvcOrderRepository orders,
        ISvcCustomerPackageRepository customerPackages, ICurrentTenant currentTenant)
    {
        _payments = payments; _orders = orders; _customerPackages = customerPackages; _currentTenant = currentTenant;
    }

    public async Task<IReadOnlyList<SvcPaymentDto>> GetAllAsync(
        Guid? customerId, Guid? orderId, Guid? customerPackageId, SvcPaymentMethod? method,
        SvcPaymentStatus? status, DateTime? from, DateTime? to, CancellationToken ct = default)
        => (await _payments.GetAllAsync(customerId, orderId, customerPackageId, method, status, from, to, ct))
            .Select(MapToDto).ToList();

    public async Task<SvcPaymentDto> GetByIdAsync(Guid id, CancellationToken ct = default)
        => MapToDto(await _payments.GetByIdAsync(id, ct) ?? throw new NotFoundException("SvcPayment", id));

    public async Task<SvcPaymentDto> CreateAsync(CreateSvcPaymentRequest r, CancellationToken ct = default)
    {
        SvcPayment payment;
        if (r.OrderId is { } orderId)
        {
            var order = await _orders.GetByIdAsync(orderId, ct) ?? throw new NotFoundException("SvcOrder", orderId);
            if (order.Status == SvcOrderStatus.Cancelled) throw new DomainException("Cannot pay a cancelled order.");
            EnsureWithinRemaining(r.Amount, order.TotalAmount, await PaidTotalForOrderAsync(orderId, ct));
            payment = SvcPayment.CreateForOrder(_currentTenant.Id, order.CustomerId, orderId, r.Amount, r.Method, r.PaidAt, r.ExternalReference, r.Notes);
        }
        else
        {
            var cpId = r.CustomerPackageId!.Value;
            var cp = await _customerPackages.GetByIdAsync(cpId, ct) ?? throw new NotFoundException("SvcCustomerPackage", cpId);
            if (cp.Status == SvcCustomerPackageStatus.Cancelled) throw new DomainException("Cannot pay a cancelled customer package.");
            EnsureWithinRemaining(r.Amount, cp.PriceSnapshot, await PaidTotalForCustomerPackageAsync(cpId, ct));
            payment = SvcPayment.CreateForCustomerPackage(_currentTenant.Id, cp.CustomerId, cpId, r.Amount, r.Method, r.PaidAt, r.ExternalReference, r.Notes);
        }

        await _payments.AddAsync(payment, ct);
        await _payments.SaveChangesAsync(ct);
        return MapToDto(payment);
    }

    public async Task<SvcPaymentDto> VoidAsync(Guid id, VoidSvcPaymentRequest r, CancellationToken ct = default)
    {
        var payment = await _payments.GetByIdAsync(id, ct) ?? throw new NotFoundException("SvcPayment", id);
        payment.Void(r.Reason);
        _payments.Update(payment);
        await _payments.SaveChangesAsync(ct);
        return MapToDto(payment);
    }

    public async Task<SvcPaymentSummaryDto> GetOrderSummaryAsync(Guid orderId, CancellationToken ct = default)
    {
        var order = await _orders.GetByIdAsync(orderId, ct) ?? throw new NotFoundException("SvcOrder", orderId);
        return Summarize(orderId, "Order", order.TotalAmount, await _payments.GetByOrderAsync(orderId, ct));
    }

    public async Task<SvcPaymentSummaryDto> GetCustomerPackageSummaryAsync(Guid customerPackageId, CancellationToken ct = default)
    {
        var cp = await _customerPackages.GetByIdAsync(customerPackageId, ct)
            ?? throw new NotFoundException("SvcCustomerPackage", customerPackageId);
        return Summarize(customerPackageId, "CustomerPackage", cp.PriceSnapshot,
            await _payments.GetByCustomerPackageAsync(customerPackageId, ct));
    }

    // ── Helpers ──────────────────────────────────────────────────────────────
    private async Task<decimal> PaidTotalForOrderAsync(Guid orderId, CancellationToken ct)
        => (await _payments.GetByOrderAsync(orderId, ct)).Where(p => p.Status == SvcPaymentStatus.Paid).Sum(p => p.Amount);

    private async Task<decimal> PaidTotalForCustomerPackageAsync(Guid cpId, CancellationToken ct)
        => (await _payments.GetByCustomerPackageAsync(cpId, ct)).Where(p => p.Status == SvcPaymentStatus.Paid).Sum(p => p.Amount);

    private static void EnsureWithinRemaining(decimal amount, decimal total, decimal paid)
    {
        if (amount > total - paid) throw new DomainException("Amount exceeds the remaining balance.");
    }

    private static SvcPaymentSummaryDto Summarize(Guid targetId, string targetType, decimal total, IEnumerable<SvcPayment> payments)
    {
        var paid   = payments.Where(p => p.Status == SvcPaymentStatus.Paid).Sum(p => p.Amount);
        var voided = payments.Where(p => p.Status == SvcPaymentStatus.Voided).Sum(p => p.Amount);
        var remaining = total - paid;
        return new(targetId, targetType, total, paid, voided, remaining, remaining <= 0m);
    }

    private static SvcPaymentDto MapToDto(SvcPayment p) => new(
        Id: p.Id, StoreId: p.StoreId, CustomerId: p.CustomerId, OrderId: p.OrderId,
        CustomerPackageId: p.CustomerPackageId, Amount: p.Amount, Method: p.Method, Status: p.Status,
        PaidAt: p.PaidAt, ExternalReference: p.ExternalReference, Notes: p.Notes, VoidReason: p.VoidReason,
        VoidedAt: p.VoidedAt, CreatedAt: p.CreatedAt, UpdatedAt: p.UpdatedAt);
}
```

- [ ] Register in Application `DependencyInjection.cs`: `services.AddScoped<SvcPaymentService>();`.

- [ ] **`PaymentsController.cs`**

```csharp
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nexo.Api.Attributes;
using Nexo.Application.Modules.Service;
using Nexo.Domain.Modules.Service;

namespace Nexo.Api.Controllers.Modules.Service;

/// <summary>
/// Service payments (ORKEN SERVICE) — manual records of payments received against an order or a
/// customer package. Operational only: no Stripe/checkout/gateway, no global financial/cash entity,
/// and no change to the order total/status or package balance/status. Gated by the service family.
/// </summary>
[ApiController]
[Route("api/v1/service/payments")]
[Authorize]
[RequireServiceModule]
public class PaymentsController : ControllerBase
{
    private readonly SvcPaymentService                    _service;
    private readonly IValidator<CreateSvcPaymentRequest>  _createValidator;
    private readonly IValidator<VoidSvcPaymentRequest>    _voidValidator;

    public PaymentsController(
        SvcPaymentService service,
        IValidator<CreateSvcPaymentRequest> createValidator,
        IValidator<VoidSvcPaymentRequest> voidValidator)
    {
        _service = service; _createValidator = createValidator; _voidValidator = voidValidator;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<SvcPaymentDto>>> GetAll(
        [FromQuery] Guid? customerId, [FromQuery] Guid? orderId, [FromQuery] Guid? customerPackageId,
        [FromQuery] SvcPaymentMethod? method, [FromQuery] SvcPaymentStatus? status,
        [FromQuery] DateTime? from, [FromQuery] DateTime? to, CancellationToken ct = default)
        => Ok(await _service.GetAllAsync(customerId, orderId, customerPackageId, method, status, from, to, ct));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<SvcPaymentDto>> GetById(Guid id, CancellationToken ct)
        => Ok(await _service.GetByIdAsync(id, ct));

    [HttpPost]
    public async Task<ActionResult<SvcPaymentDto>> Create([FromBody] CreateSvcPaymentRequest request, CancellationToken ct)
    {
        await _createValidator.ValidateAndThrowAsync(request, ct);
        var dto = await _service.CreateAsync(request, ct);
        return CreatedAtAction(nameof(GetById), new { id = dto.Id }, dto);
    }

    [HttpPost("{id:guid}/void")]
    public async Task<ActionResult<SvcPaymentDto>> Void(Guid id, [FromBody] VoidSvcPaymentRequest request, CancellationToken ct)
    {
        await _voidValidator.ValidateAndThrowAsync(request, ct);
        return Ok(await _service.VoidAsync(id, request, ct));
    }

    [HttpGet("order/{orderId:guid}/summary")]
    public async Task<ActionResult<SvcPaymentSummaryDto>> OrderSummary(Guid orderId, CancellationToken ct)
        => Ok(await _service.GetOrderSummaryAsync(orderId, ct));

    [HttpGet("customer-package/{customerPackageId:guid}/summary")]
    public async Task<ActionResult<SvcPaymentSummaryDto>> CustomerPackageSummary(Guid customerPackageId, CancellationToken ct)
        => Ok(await _service.GetCustomerPackageSummaryAsync(customerPackageId, ct));
}
```

- [ ] **Step: Build → success. Commit** — `feat(service): SvcPaymentService + PaymentsController (PR6)`

---

## Task 7: Integration tests

**File:** `ServicePaymentsTests.cs`.

Covers: gate 403/200; payment for order (Paid, CustomerId derived); payment for customer package (Paid, CustomerId derived); exactly-one-target (both → 400, neither → 400); non-UTC paidAt → 400; amount ≤ 0 → 400; invalid method → 400; order not found → 404; customer-package not found → 404; cross-tenant target → 404; cancelled order → 422; cancelled customer package → 422; amount > remaining → 422; partial payments accumulate; order summary (total/paid/voided/remaining/isFullyPaid); customer-package summary; void → Voided; voided drops out of the summary; void twice → 422; **order TotalAmount/Status unchanged after payment + void**; **customer-package PriceSnapshot/Status unchanged after payment** (assert via GET of the order/package).

Helpers reuse PR1–PR5 routes (customer, catalog, order + item for a non-zero total, package + item + assign). All `paidAt` are UTC. Foreign-tenant seed mirrors PR4/PR5 (`SvcPayment.CreateForOrder`/`CreateForCustomerPackage`, set `StoreId` via `db.Entry(x).Property("StoreId").CurrentValue`). Example assertions:

```csharp
// order remaining + over-payment + immutability:
var (orderId, _) = await CreateOrderWithItemAsync(c, customerId, catalogId, qty: 3m); // catalog 100 → total 300
var pay = await c.PostAsJsonAsync("/api/v1/service/payments",
    new { orderId, amount = 100m, method = "Pix", paidAt = DateTime.UtcNow });
pay.StatusCode.Should().Be(HttpStatusCode.Created);
(await (await c.GetAsync($"/api/v1/service/payments/order/{orderId}/summary")).Content.ReadFromJsonAsync<JsonElement>())
    .GetProperty("remainingAmount").GetDecimal().Should().Be(200m);
// over-payment of the remaining → 422
(await c.PostAsJsonAsync("/api/v1/service/payments", new { orderId, amount = 9999m, method = "Cash", paidAt = DateTime.UtcNow }))
    .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
// order total/status untouched by the payment
var order = await (await c.GetAsync($"/api/v1/service/orders/{orderId}")).Content.ReadFromJsonAsync<JsonElement>();
order.GetProperty("totalAmount").GetDecimal().Should().Be(300m);
order.GetProperty("status").GetString().Should().Be("Draft");
```

- [ ] **Step 1:** Run new tests, watch fail (endpoints missing).
- [ ] **Step 2:** After Tasks 1–6, run again → green.
- [ ] **Step 3: Commit** — `test(service): integration coverage for payments (PR6)`

---

## Task 8: Full verification + PR

- [ ] `dotnet build Nexo.sln` → 0 errors.
- [ ] `dotnet test tests/Nexo.UnitTests` → green.
- [ ] `dotnet test tests/Nexo.IntegrationTests` → green (incl. unchanged PR1–PR5 tests).
- [ ] Re-read migration `Up()` — only `CreateTable(svc_payments)` + indexes + new FKs.
- [ ] `git diff --name-only origin/master` — only Service files + migration. No Auth/Redis/Stripe/SuperAdmin/Build, no frontend, no `dist`. Confirm **no** `FinancialMovement`/`CashMovement`/Stripe/checkout/gateway anywhere in the new code, and **no** `_orders.Update`/`order.ChangeStatus`/`order.RecalculateTotal`/`_customerPackages.Update` in `SvcPaymentService`.
- [ ] Push + `gh pr create --base master` (no merge).

---

## Self-review (spec coverage)

Entity `SvcPayment` + all fields (T1) ✔ · method/status enums (T1) ✔ · CreateForOrder/CreateForCustomerPackage/Void (T1) ✔ · order payment rules (exist/not-cancelled/customer-from-order/amount≤remaining/no-mutation) (T6) ✔ · package payment rules (same) (T6) ✔ · void rules (Paid→Voided, no double void, no delete) (T1/T6) ✔ · summaries total/paid/voided/remaining/isFullyPaid (T6) ✔ · endpoints list/create/get/void + 2 summaries (T6) ✔ · exactly-one-target + UTC paidAt + lengths (T5) ✔ · additive migration (T2/T3) ✔ · tests incl. immutability asserts (T7) ✔ · gate + isolation ✔ · **no Stripe/gateway/CashMovement/FinancialMovement/OS-or-package mutation** (T6 design + T8 grep) ✔.

## Risks
1. **Remaining computed at write time, no DB constraint** — two concurrent payments could both pass the `≤ remaining` check and overshoot (rare; documented; same posture as PR3/PR4 races). A future DB-level guard could harden it.
2. **No auto status on the order/package when fully paid** — by design (PR6 must not change OS/package status); "fully paid" is a read-only summary flag.
3. **Voided amount kept in history** — `IsFullyPaid`/remaining use only Paid; voided payments are visible in the summary's `voidedAmount` and the list.
