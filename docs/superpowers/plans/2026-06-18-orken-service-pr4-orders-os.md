# Orken Service PR4 — Orders / OS Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add the operational service order — `SvcOrder` (aggregate) + `SvcOrderItem` (children) with a status state machine, server-computed totals, per-item price/name/commission snapshots, manual creation **and** creation from an appointment, plus extending records to accept `ContextType=Order`.

**Architecture:** Mirrors the `BuildBudget`+`BuildBudgetItem` aggregate exactly: `SvcOrder` (StoreEntity, no `IsActive` — uses `Status`) holds a public `ICollection<SvcOrderItem> Items` navigation; `SvcOrderItem` (StoreEntity) is a separate entity with its own DbSet/table/repository and an `OrderId` FK. The service uses **two repositories** (orders + items): items are persisted via the item repo, then `order.RecalculateTotal(items)` recomputes `TotalAmount` (avoiding the readonly-backing-field tracking pitfall). Migration is strictly additive (2 `CreateTable` + indexes + new FKs to existing tables, incl. a partial unique index on `appointment_id`).

**Tech Stack:** .NET 8, EF Core 8 (Npgsql/PostgreSQL), FluentValidation, xUnit + FluentAssertions + Testcontainers.

---

## Design decisions (locked, grounded in PR1–PR3 + BuildBudget)

| Concern | Decision |
|---|---|
| `SvcOrder` base | `StoreEntity`, no `IsActive` (uses `Status`) → reuses PR2 `ConfigureStoreScopedSvcEntityNoActive`. |
| `SvcOrderItem` base | `StoreEntity` (own DbSet/table/repo) — store-scoped like its order. Separate entity + `OrderId` FK (BuildBudget pattern). |
| Aggregate wiring | Parent `HasMany(x => x.Items).WithOne().HasForeignKey(x => x.OrderId).OnDelete(Cascade)`. Service persists items via item repo, recomputes total. |
| Status enum | `SvcOrderStatus { Draft, Open, InProgress, Completed, Cancelled }`, `.HasConversion<string>()` (Service-module convention). |
| Transitions | Draft→{Open,Cancelled}; Open→{InProgress,Cancelled}; InProgress→{Completed,Cancelled}; Completed/Cancelled terminal. Invalid → `DomainException` → **422**. |
| Total | `TotalAmount = Σ(item.TotalAmount)`, recomputed server-side on every item add/update/delete. Item `TotalAmount = Quantity × UnitPriceSnapshot`. Never trust a client total. |
| Item snapshots | `NameSnapshot`/`DescriptionSnapshot`/`UnitPriceSnapshot`/`CommissionPercentSnapshot` copied from the catalog **at add time**; immutable thereafter (item update changes Quantity/Professional only). |
| `Code` | Server-generated `OS-{yyyyMMdd}-{6 upper hex}` (Guid-derived). No DB unique constraint on code (probabilistic uniqueness; no 500 risk). |
| From-appointment | New order copies customer/subject/professional/appointment; initial item's `UnitPriceSnapshot` = **`Appointment.PriceSnapshot`** (not current catalog price); name/description/commission from the catalog. Appointment must not be Cancelled/NoShow (→422); one order per appointment (app check →409 + partial unique index backstop). **Appointment status is NOT mutated.** |
| Reference rules | customer exists (404); subject (if set) exists + belongs to customer (404/422); professional (if set) exists + active (404/422); catalog (per item) exists + active (404/422); `RequiresSubject` enforced at **item-add** + from-appointment (422). |
| Terminal guards | Terminal order rejects edit, status change, and item add/update/delete → **422**. |
| Records `ContextType=Order` | Extend PR2 records (validator `Supported` + service context switch + controller GET filter) to accept `Order`, validating the order exists (404 cross-tenant). Customer/Subject records unchanged. |
| Status codes | payload/bad-status → 400; missing refs → 404; domain rules (inactive, mismatch, requiresSubject, invalid transition, terminal) → 422; duplicate order per appointment → 409. |

## File structure

**Domain** (`Nexo.Domain/Modules/Service/`): `SvcOrderStatus.cs`, `SvcOrder.cs`, `SvcOrderItem.cs`
**Application** (`Nexo.Application/Modules/Service/`): `SvcOrderDtos.cs`, `SvcOrderService.cs`, modify `SvcValidators.cs`, `Interfaces/ISvcOrderRepository.cs`, `Interfaces/ISvcOrderItemRepository.cs`, modify `SvcRecordEntryService.cs`, modify `DependencyInjection.cs`
**Infrastructure** (`Nexo.Infrastructure/`): `Persistence/Configurations/Modules/Service/SvcOrderConfiguration.cs` + `SvcOrderItemConfiguration.cs`, `Repositories/Modules/Service/SvcOrderRepository.cs` + `SvcOrderItemRepository.cs`, modify `DependencyInjection.cs`, modify `Persistence/NexoDbContext.cs`, migration `<ts>_AddServiceOrders.cs`
**API** (`Nexo.Api/`): `Controllers/Modules/Service/OrdersController.cs`, modify `Controllers/Modules/Service/RecordsController.cs`
**Tests**: `Nexo.UnitTests/Service/SvcOrderTests.cs`, `Nexo.UnitTests/Service/SvcOrderItemTests.cs`, `Nexo.IntegrationTests/Service/ServiceOrdersTests.cs`, modify `Nexo.IntegrationTests/Service/ServiceRecordsTests.cs`

---

## Task 1: Domain — `SvcOrderStatus` + `SvcOrder` + `SvcOrderItem` (unit tested)

**Files:** Create the three domain files + `SvcOrderTests.cs` + `SvcOrderItemTests.cs`.

- [ ] **Step 1: Write failing unit tests** (`SvcOrderTests.cs` + `SvcOrderItemTests.cs`)

```csharp
// SvcOrderTests.cs
using FluentAssertions;
using Nexo.Domain.Exceptions;
using Nexo.Domain.Modules.Service;
using Xunit;

namespace Nexo.UnitTests.Service;

public class SvcOrderTests
{
    private static readonly Guid T = Guid.NewGuid();
    private static readonly Guid Cust = Guid.NewGuid();
    private static SvcOrder New() => SvcOrder.Create(T, "OS-20260618-AAA111", Cust, null, null, null, "note");

    [Fact]
    public void Create_defaults_draft_zero_total()
    {
        var o = New();
        o.Code.Should().Be("OS-20260618-AAA111");
        o.CustomerId.Should().Be(Cust);
        o.Status.Should().Be(SvcOrderStatus.Draft);
        o.TotalAmount.Should().Be(0m);
        o.IsTerminal.Should().BeFalse();
    }

    [Fact]
    public void Create_with_empty_customer_throws()
        => ((Action)(() => SvcOrder.Create(T, "OS-x", Guid.Empty, null, null, null, null)))
            .Should().Throw<DomainException>();

    [Fact]
    public void Create_with_blank_code_throws()
        => ((Action)(() => SvcOrder.Create(T, "  ", Cust, null, null, null, null)))
            .Should().Throw<DomainException>();

    [Theory]
    [InlineData(SvcOrderStatus.Open)]
    [InlineData(SvcOrderStatus.Cancelled)]
    public void Draft_allows(SvcOrderStatus to)
    {
        var o = New();
        o.ChangeStatus(to, "r");
        o.Status.Should().Be(to);
    }

    [Fact]
    public void Open_to_inprogress_to_completed_allowed()
    {
        var o = New();
        o.ChangeStatus(SvcOrderStatus.Open, null);
        o.ChangeStatus(SvcOrderStatus.InProgress, null);
        o.ChangeStatus(SvcOrderStatus.Completed, null);
        o.Status.Should().Be(SvcOrderStatus.Completed);
        o.IsTerminal.Should().BeTrue();
    }

    [Theory]
    [InlineData(SvcOrderStatus.InProgress)]   // Draft cannot jump to InProgress
    [InlineData(SvcOrderStatus.Completed)]    // Draft cannot jump to Completed
    public void Invalid_transition_from_draft_throws(SvcOrderStatus to)
        => ((Action)(() => New().ChangeStatus(to, null)))
            .Should().Throw<DomainException>().WithMessage("*status*");

    [Fact]
    public void Cancel_records_reason()
    {
        var o = New();
        o.ChangeStatus(SvcOrderStatus.Cancelled, "client gave up");
        o.CancellationReason.Should().Be("client gave up");
    }

    [Fact]
    public void RecalculateTotal_sums_items()
    {
        var o = New();
        var i1 = SvcOrderItem.Create(T, o.Id, Guid.NewGuid(), null, "A", null, 2m, 10m, null);   // 20
        var i2 = SvcOrderItem.Create(T, o.Id, Guid.NewGuid(), null, "B", null, 1m, 5m, 50m);      // 5
        o.RecalculateTotal(new[] { i1, i2 });
        o.TotalAmount.Should().Be(25m);
    }

    [Fact]
    public void UpdateDetails_throws_when_terminal()
    {
        var o = New();
        o.ChangeStatus(SvcOrderStatus.Cancelled, "x");
        ((Action)(() => o.UpdateDetails(null, null, "new note")))
            .Should().Throw<DomainException>();
    }

    [Fact]
    public void EnsureEditable_throws_when_terminal()
    {
        var o = New();
        o.ChangeStatus(SvcOrderStatus.Open, null);
        o.ChangeStatus(SvcOrderStatus.Completed.ToOpenPath(), null); // illustrative — replace, see note
    }
}
```

> Replace the last test with the real drive-to-terminal (no placeholder helper):

```csharp
    [Fact]
    public void EnsureEditable_throws_when_terminal()
    {
        var o = New();
        o.ChangeStatus(SvcOrderStatus.Open, null);
        o.ChangeStatus(SvcOrderStatus.InProgress, null);
        o.ChangeStatus(SvcOrderStatus.Completed, null);
        ((Action)o.EnsureEditable).Should().Throw<DomainException>().WithMessage("*Completed*");
    }
```

```csharp
// SvcOrderItemTests.cs
using FluentAssertions;
using Nexo.Domain.Exceptions;
using Nexo.Domain.Modules.Service;
using Xunit;

namespace Nexo.UnitTests.Service;

public class SvcOrderItemTests
{
    private static readonly Guid T = Guid.NewGuid();
    private static readonly Guid Order = Guid.NewGuid();
    private static readonly Guid Item = Guid.NewGuid();

    [Fact]
    public void Create_computes_total_and_snapshots()
    {
        var i = SvcOrderItem.Create(T, Order, Item, Guid.NewGuid(), "Corte", "desc", 3m, 20m, 10m);
        i.OrderId.Should().Be(Order);
        i.CatalogItemId.Should().Be(Item);
        i.NameSnapshot.Should().Be("Corte");
        i.DescriptionSnapshot.Should().Be("desc");
        i.UnitPriceSnapshot.Should().Be(20m);
        i.CommissionPercentSnapshot.Should().Be(10m);
        i.Quantity.Should().Be(3m);
        i.TotalAmount.Should().Be(60m);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Create_with_non_positive_quantity_throws(decimal q)
        => ((Action)(() => SvcOrderItem.Create(T, Order, Item, null, "A", null, q, 10m, null)))
            .Should().Throw<DomainException>();

    [Fact]
    public void Create_with_negative_price_throws()
        => ((Action)(() => SvcOrderItem.Create(T, Order, Item, null, "A", null, 1m, -1m, null)))
            .Should().Throw<DomainException>();

    [Fact]
    public void Create_with_empty_catalog_throws()
        => ((Action)(() => SvcOrderItem.Create(T, Order, Guid.Empty, null, "A", null, 1m, 10m, null)))
            .Should().Throw<DomainException>();

    [Fact]
    public void Update_changes_quantity_and_recomputes_total_keeping_price()
    {
        var i = SvcOrderItem.Create(T, Order, Item, null, "A", null, 2m, 15m, null); // 30
        i.Update(4m, Guid.NewGuid());
        i.Quantity.Should().Be(4m);
        i.UnitPriceSnapshot.Should().Be(15m); // unchanged
        i.TotalAmount.Should().Be(60m);
    }
}
```

- [ ] **Step 2: Run, verify fail** — `dotnet test tests/Nexo.UnitTests --filter "SvcOrderTests|SvcOrderItemTests"` → FAIL (types missing).

- [ ] **Step 3: Implement `SvcOrderStatus.cs`**

```csharp
namespace Nexo.Domain.Modules.Service;

/// <summary>
/// Lifecycle of a <see cref="SvcOrder"/> (ordem de serviço). Stored as a string. Transitions
/// enforced by the entity; Completed/Cancelled are terminal.
/// </summary>
public enum SvcOrderStatus
{
    Draft,
    Open,
    InProgress,
    Completed,
    Cancelled,
}
```

- [ ] **Step 4: Implement `SvcOrderItem.cs`**

```csharp
using Nexo.Domain.Common;
using Nexo.Domain.Exceptions;

namespace Nexo.Domain.Modules.Service;

/// <summary>
/// A line item of a <see cref="SvcOrder"/>. Store-scoped. Name/Description/UnitPrice/Commission
/// are snapshots copied from the catalog at add time and never change with later catalog edits;
/// only Quantity and the executing Professional are mutable. TotalAmount = Quantity × UnitPriceSnapshot.
/// </summary>
public class SvcOrderItem : StoreEntity
{
    private SvcOrderItem() { }                                   // EF Core
    private SvcOrderItem(Guid tenantId) : base(tenantId) { }

    public Guid     OrderId                   { get; private set; }
    public Guid     CatalogItemId             { get; private set; }
    public Guid?    ProfessionalId            { get; private set; }
    public string   NameSnapshot              { get; private set; } = string.Empty;
    public string?  DescriptionSnapshot       { get; private set; }
    public decimal  Quantity                  { get; private set; }
    public decimal  UnitPriceSnapshot         { get; private set; }
    public decimal? CommissionPercentSnapshot { get; private set; }
    public decimal  TotalAmount               { get; private set; }

    public static SvcOrderItem Create(
        Guid tenantId, Guid orderId, Guid catalogItemId, Guid? professionalId,
        string nameSnapshot, string? descriptionSnapshot, decimal quantity,
        decimal unitPriceSnapshot, decimal? commissionPercentSnapshot)
    {
        if (orderId == Guid.Empty)                  throw new DomainException("OrderId is required.");
        if (catalogItemId == Guid.Empty)            throw new DomainException("CatalogItemId is required.");
        if (string.IsNullOrWhiteSpace(nameSnapshot)) throw new DomainException("Item name is required.");
        if (quantity <= 0m)                         throw new DomainException("Quantity must be positive.");
        if (unitPriceSnapshot < 0m)                 throw new DomainException("Unit price cannot be negative.");

        return new SvcOrderItem(tenantId)
        {
            OrderId                   = orderId,
            CatalogItemId             = catalogItemId,
            ProfessionalId            = professionalId,
            NameSnapshot              = nameSnapshot.Trim(),
            DescriptionSnapshot       = descriptionSnapshot?.Trim(),
            Quantity                  = quantity,
            UnitPriceSnapshot         = unitPriceSnapshot,
            CommissionPercentSnapshot = commissionPercentSnapshot,
            TotalAmount               = quantity * unitPriceSnapshot,
        };
    }

    /// <summary>Updates the quantity and executing professional. Price snapshot is immutable.</summary>
    public void Update(decimal quantity, Guid? professionalId)
    {
        if (quantity <= 0m) throw new DomainException("Quantity must be positive.");
        Quantity       = quantity;
        ProfessionalId = professionalId;
        TotalAmount    = quantity * UnitPriceSnapshot;
        SetUpdatedAt();
    }
}
```

- [ ] **Step 5: Implement `SvcOrder.cs`**

```csharp
using Nexo.Domain.Common;
using Nexo.Domain.Exceptions;

namespace Nexo.Domain.Modules.Service;

/// <summary>
/// An order of service (ordem de serviço) — store-scoped aggregate root. Created manually or
/// from an appointment (<see cref="AppointmentId"/>). Holds <see cref="SvcOrderItem"/> children;
/// <see cref="TotalAmount"/> is recomputed by the application service from the items (never trusted
/// from the client). Status machine: Draft→Open→InProgress→Completed (Cancelled exits); terminal
/// orders cannot be edited nor have items changed.
/// </summary>
public class SvcOrder : StoreEntity
{
    private SvcOrder() { }                                   // EF Core
    private SvcOrder(Guid tenantId) : base(tenantId) { }

    public string         Code               { get; private set; } = string.Empty;
    public Guid           CustomerId         { get; private set; }
    public Guid?          SubjectId          { get; private set; }
    public Guid?          ProfessionalId     { get; private set; }
    public Guid?          AppointmentId      { get; private set; }
    public SvcOrderStatus Status             { get; private set; }
    public string?        Notes              { get; private set; }
    public string?        CancellationReason { get; private set; }
    public decimal        TotalAmount        { get; private set; }

    // Navigation (BuildBudget pattern: public collection; items persisted via the item repository).
    public ICollection<SvcOrderItem> Items { get; private set; } = [];

    public bool IsTerminal => Status is SvcOrderStatus.Completed or SvcOrderStatus.Cancelled;

    public static SvcOrder Create(
        Guid tenantId, string code, Guid customerId,
        Guid? subjectId, Guid? professionalId, Guid? appointmentId, string? notes = null)
    {
        if (string.IsNullOrWhiteSpace(code)) throw new DomainException("Order code is required.");
        if (customerId == Guid.Empty)        throw new DomainException("Customer is required.");

        return new SvcOrder(tenantId)
        {
            Code           = code.Trim(),
            CustomerId     = customerId,
            SubjectId      = subjectId,
            ProfessionalId = professionalId,
            AppointmentId  = appointmentId,
            Status         = SvcOrderStatus.Draft,
            Notes          = notes?.Trim(),
            TotalAmount    = 0m,
        };
    }

    public void UpdateDetails(Guid? subjectId, Guid? professionalId, string? notes)
    {
        EnsureEditable();
        SubjectId      = subjectId;
        ProfessionalId = professionalId;
        Notes          = notes?.Trim();
        SetUpdatedAt();
    }

    /// <summary>Recomputes TotalAmount from the supplied items (server-authoritative).</summary>
    public void RecalculateTotal(IEnumerable<SvcOrderItem> items)
    {
        TotalAmount = items.Sum(i => i.TotalAmount);
        SetUpdatedAt();
    }

    public void ChangeStatus(SvcOrderStatus target, string? reason)
    {
        if (!CanTransition(Status, target))
            throw new DomainException($"Cannot change order status from {Status} to {target}.");
        Status = target;
        if (target == SvcOrderStatus.Cancelled)
            CancellationReason = reason?.Trim();
        SetUpdatedAt();
    }

    /// <summary>Throws if the order is terminal (Completed/Cancelled) — blocks edits and item changes.</summary>
    public void EnsureEditable()
    {
        if (IsTerminal)
            throw new DomainException($"Cannot modify a {Status} order.");
    }

    private static bool CanTransition(SvcOrderStatus from, SvcOrderStatus to) => (from, to) switch
    {
        (SvcOrderStatus.Draft,      SvcOrderStatus.Open)       => true,
        (SvcOrderStatus.Draft,      SvcOrderStatus.Cancelled)  => true,
        (SvcOrderStatus.Open,       SvcOrderStatus.InProgress) => true,
        (SvcOrderStatus.Open,       SvcOrderStatus.Cancelled)  => true,
        (SvcOrderStatus.InProgress, SvcOrderStatus.Completed)  => true,
        (SvcOrderStatus.InProgress, SvcOrderStatus.Cancelled)  => true,
        _ => false,
    };
}
```

- [ ] **Step 6: Run, verify pass.** Commit — `feat(service): SvcOrder + SvcOrderItem domain (status machine, totals) (PR4)`

---

## Task 2: EF configs + DbSets (model verified)

- [ ] **Step 1: `SvcOrderConfiguration.cs`**

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nexo.Domain.Entities;
using Nexo.Domain.Modules.Service;

namespace Nexo.Infrastructure.Persistence.Configurations.Modules.Service;

public class SvcOrderConfiguration : IEntityTypeConfiguration<SvcOrder>
{
    public void Configure(EntityTypeBuilder<SvcOrder> builder)
    {
        builder.ConfigureStoreScopedSvcEntityNoActive("svc_orders");

        builder.Property(x => x.Code).HasColumnName("code").HasMaxLength(40).IsRequired();
        builder.Property(x => x.CustomerId).HasColumnName("customer_id").IsRequired();
        builder.Property(x => x.SubjectId).HasColumnName("subject_id");
        builder.Property(x => x.ProfessionalId).HasColumnName("professional_id");
        builder.Property(x => x.AppointmentId).HasColumnName("appointment_id");
        builder.Property(x => x.Status)
            .HasColumnName("status").HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.Notes).HasColumnName("notes").HasMaxLength(2000);
        builder.Property(x => x.CancellationReason).HasColumnName("cancellation_reason").HasMaxLength(500);
        builder.Property(x => x.TotalAmount)
            .HasColumnName("total_amount").HasColumnType("numeric(18,2)").IsRequired();

        builder.HasMany(x => x.Items)
            .WithOne()
            .HasForeignKey(x => x.OrderId)
            .HasConstraintName("fk_svc_order_items_order")
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Customer>().WithMany().HasForeignKey(x => x.CustomerId)
            .HasConstraintName("fk_svc_orders_customers").OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<SvcSubject>().WithMany().HasForeignKey(x => x.SubjectId)
            .HasConstraintName("fk_svc_orders_subjects").OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<SvcProfessional>().WithMany().HasForeignKey(x => x.ProfessionalId)
            .HasConstraintName("fk_svc_orders_professionals").OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<SvcAppointment>().WithMany().HasForeignKey(x => x.AppointmentId)
            .HasConstraintName("fk_svc_orders_appointments").OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.CustomerId).HasDatabaseName("ix_svc_orders_customer_id");
        builder.HasIndex("TenantId", "StoreId", "Status").HasDatabaseName("ix_svc_orders_tenant_store_status");
        builder.HasIndex(x => x.SubjectId).HasDatabaseName("ix_svc_orders_subject_id");
        builder.HasIndex(x => x.ProfessionalId).HasDatabaseName("ix_svc_orders_professional_id");
        // One order per appointment (partial unique — only when appointment_id is set).
        builder.HasIndex(x => x.AppointmentId).IsUnique()
            .HasFilter("appointment_id IS NOT NULL")
            .HasDatabaseName("ux_svc_orders_appointment_id");
    }
}
```

- [ ] **Step 2: `SvcOrderItemConfiguration.cs`**

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nexo.Domain.Modules.Service;

namespace Nexo.Infrastructure.Persistence.Configurations.Modules.Service;

public class SvcOrderItemConfiguration : IEntityTypeConfiguration<SvcOrderItem>
{
    public void Configure(EntityTypeBuilder<SvcOrderItem> builder)
    {
        builder.ConfigureStoreScopedSvcEntityNoActive("svc_order_items");

        builder.Property(x => x.OrderId).HasColumnName("order_id").IsRequired();
        builder.Property(x => x.CatalogItemId).HasColumnName("catalog_item_id").IsRequired();
        builder.Property(x => x.ProfessionalId).HasColumnName("professional_id");
        builder.Property(x => x.NameSnapshot).HasColumnName("name_snapshot").HasMaxLength(200).IsRequired();
        builder.Property(x => x.DescriptionSnapshot).HasColumnName("description_snapshot").HasMaxLength(1000);
        builder.Property(x => x.Quantity).HasColumnName("quantity").HasColumnType("numeric(12,3)").IsRequired();
        builder.Property(x => x.UnitPriceSnapshot).HasColumnName("unit_price_snapshot").HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(x => x.CommissionPercentSnapshot).HasColumnName("commission_percent_snapshot").HasColumnType("numeric(5,2)");
        builder.Property(x => x.TotalAmount).HasColumnName("total_amount").HasColumnType("numeric(18,2)").IsRequired();

        builder.HasOne<SvcCatalogItem>().WithMany().HasForeignKey(x => x.CatalogItemId)
            .HasConstraintName("fk_svc_order_items_catalog_items").OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<SvcProfessional>().WithMany().HasForeignKey(x => x.ProfessionalId)
            .HasConstraintName("fk_svc_order_items_professionals").OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.OrderId).HasDatabaseName("ix_svc_order_items_order_id");
    }
}
```

- [ ] **Step 3: Add DbSets to `NexoDbContext.cs`** (after `SvcAppointments`):

```csharp
        public DbSet<SvcOrder>            SvcOrders        => Set<SvcOrder>();
        public DbSet<SvcOrderItem>        SvcOrderItems    => Set<SvcOrderItem>();
```

- [ ] **Step 4: Build → success. Commit** — `feat(service): EF config + DbSets for SvcOrder/SvcOrderItem (PR4)`

---

## Task 3: Additive migration

- [ ] **Step 1: Generate** — `dotnet ef migrations add AddServiceOrders -p src/Nexo.Infrastructure -s src/Nexo.Api -o Persistence/Migrations`
- [ ] **Step 2: PROVE additive.** `Up()` must contain ONLY `CreateTable("svc_orders")`, `CreateTable("svc_order_items")`, `CreateIndex(...)` (incl. the `ux_svc_orders_appointment_id` with `filter: "appointment_id IS NOT NULL"`), and FKs to existing tables. No Drop/Alter/Rename/AddColumn on existing tables. STOP if anything else appears.
- [ ] **Step 3: Confirm clean** — `dotnet ef migrations has-pending-model-changes …` → "No changes…".
- [ ] **Step 4: Commit** — `feat(service): additive migration AddServiceOrders (PR4)`

---

## Task 4: Repositories + interfaces + DI

- [ ] **Step 1: `ISvcOrderRepository.cs`**

```csharp
using Nexo.Domain.Modules.Service;

namespace Nexo.Application.Modules.Service.Interfaces;

public interface ISvcOrderRepository
{
    Task<SvcOrder?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<SvcOrder?> GetByIdWithItemsAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<SvcOrder>> GetAllAsync(
        SvcOrderStatus? status, Guid? customerId, Guid? subjectId, Guid? professionalId,
        Guid? appointmentId, CancellationToken ct = default);
    Task<bool> ExistsForAppointmentAsync(Guid appointmentId, CancellationToken ct = default);
    Task AddAsync(SvcOrder entity, CancellationToken ct = default);
    void Update(SvcOrder entity);
    Task SaveChangesAsync(CancellationToken ct = default);
}
```

- [ ] **Step 2: `ISvcOrderItemRepository.cs`**

```csharp
using Nexo.Domain.Modules.Service;

namespace Nexo.Application.Modules.Service.Interfaces;

public interface ISvcOrderItemRepository
{
    Task<SvcOrderItem?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<SvcOrderItem>> GetByOrderAsync(Guid orderId, CancellationToken ct = default);
    Task AddAsync(SvcOrderItem entity, CancellationToken ct = default);
    void Update(SvcOrderItem entity);
    void Remove(SvcOrderItem entity);
    Task SaveChangesAsync(CancellationToken ct = default);
}
```

- [ ] **Step 3: `SvcOrderRepository.cs`**

```csharp
using Microsoft.EntityFrameworkCore;
using Nexo.Application.Modules.Service.Interfaces;
using Nexo.Domain.Modules.Service;
using Nexo.Infrastructure.Persistence;

namespace Nexo.Infrastructure.Repositories.Modules.Service;

public class SvcOrderRepository : ISvcOrderRepository
{
    private readonly NexoDbContext _context;
    public SvcOrderRepository(NexoDbContext context) => _context = context;

    public async Task<SvcOrder?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await _context.SvcOrders.FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<SvcOrder?> GetByIdWithItemsAsync(Guid id, CancellationToken ct = default)
        => await _context.SvcOrders.Include(x => x.Items).FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<IReadOnlyList<SvcOrder>> GetAllAsync(
        SvcOrderStatus? status, Guid? customerId, Guid? subjectId, Guid? professionalId,
        Guid? appointmentId, CancellationToken ct = default)
    {
        var q = _context.SvcOrders.AsQueryable();
        if (status is { } s)         q = q.Where(o => o.Status == s);
        if (customerId is { } c)     q = q.Where(o => o.CustomerId == c);
        if (subjectId is { } sub)    q = q.Where(o => o.SubjectId == sub);
        if (professionalId is { } p) q = q.Where(o => o.ProfessionalId == p);
        if (appointmentId is { } a)  q = q.Where(o => o.AppointmentId == a);
        return await q.OrderByDescending(o => o.CreatedAt).ToListAsync(ct);
    }

    public async Task<bool> ExistsForAppointmentAsync(Guid appointmentId, CancellationToken ct = default)
        => await _context.SvcOrders.AnyAsync(o => o.AppointmentId == appointmentId, ct);

    public async Task AddAsync(SvcOrder entity, CancellationToken ct = default)
        => await _context.SvcOrders.AddAsync(entity, ct);

    public void Update(SvcOrder entity) => _context.SvcOrders.Update(entity);

    public async Task SaveChangesAsync(CancellationToken ct = default)
        => await _context.SaveChangesAsync(ct);
}
```

- [ ] **Step 4: `SvcOrderItemRepository.cs`**

```csharp
using Microsoft.EntityFrameworkCore;
using Nexo.Application.Modules.Service.Interfaces;
using Nexo.Domain.Modules.Service;
using Nexo.Infrastructure.Persistence;

namespace Nexo.Infrastructure.Repositories.Modules.Service;

public class SvcOrderItemRepository : ISvcOrderItemRepository
{
    private readonly NexoDbContext _context;
    public SvcOrderItemRepository(NexoDbContext context) => _context = context;

    public async Task<SvcOrderItem?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await _context.SvcOrderItems.FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<IReadOnlyList<SvcOrderItem>> GetByOrderAsync(Guid orderId, CancellationToken ct = default)
        => await _context.SvcOrderItems.Where(x => x.OrderId == orderId)
            .OrderBy(x => x.CreatedAt).ToListAsync(ct);

    public async Task AddAsync(SvcOrderItem entity, CancellationToken ct = default)
        => await _context.SvcOrderItems.AddAsync(entity, ct);

    public void Update(SvcOrderItem entity) => _context.SvcOrderItems.Update(entity);
    public void Remove(SvcOrderItem entity) => _context.SvcOrderItems.Remove(entity);

    public async Task SaveChangesAsync(CancellationToken ct = default)
        => await _context.SaveChangesAsync(ct);
}
```

- [ ] **Step 5: Register both in Infrastructure `DependencyInjection.cs`** (Service repos block):

```csharp
        services.AddScoped<ISvcOrderRepository, SvcOrderRepository>();
        services.AddScoped<ISvcOrderItemRepository, SvcOrderItemRepository>();
```

- [ ] **Step 6: Build → success. Commit** — `feat(service): SvcOrder repositories + DI (PR4)`

---

## Task 5: DTOs + validators

- [ ] **Step 1: `SvcOrderDtos.cs`**

```csharp
using Nexo.Domain.Modules.Service;

namespace Nexo.Application.Modules.Service;

public sealed record SvcOrderItemDto(
    Guid     Id,
    Guid     OrderId,
    Guid     CatalogItemId,
    Guid?    ProfessionalId,
    string   NameSnapshot,
    string?  DescriptionSnapshot,
    decimal  Quantity,
    decimal  UnitPriceSnapshot,
    decimal? CommissionPercentSnapshot,
    decimal  TotalAmount,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public sealed record SvcOrderDto(
    Guid                       Id,
    Guid                       StoreId,
    string                     Code,
    Guid                       CustomerId,
    Guid?                      SubjectId,
    Guid?                      ProfessionalId,
    Guid?                      AppointmentId,
    SvcOrderStatus             Status,
    string?                    Notes,
    string?                    CancellationReason,
    decimal                    TotalAmount,
    IReadOnlyList<SvcOrderItemDto> Items,
    DateTime                   CreatedAt,
    DateTime                   UpdatedAt);

public sealed record CreateSvcOrderRequest(
    Guid    CustomerId,
    Guid?   SubjectId      = null,
    Guid?   ProfessionalId = null,
    string? Notes          = null);

public sealed record UpdateSvcOrderRequest(
    Guid?   SubjectId      = null,
    Guid?   ProfessionalId = null,
    string? Notes          = null);

public sealed record ChangeSvcOrderStatusRequest(
    SvcOrderStatus? Status,
    string?         Reason = null);

public sealed record AddSvcOrderItemRequest(
    Guid    CatalogItemId,
    decimal Quantity,
    Guid?   ProfessionalId = null);

public sealed record UpdateSvcOrderItemRequest(
    decimal Quantity,
    Guid?   ProfessionalId = null);
```

- [ ] **Step 2: Add validators to `SvcValidators.cs`** (append classes; `SvcOrderStatus` reachable via existing `using Nexo.Domain.Modules.Service;`)

```csharp
public class CreateSvcOrderRequestValidator : AbstractValidator<CreateSvcOrderRequest>
{
    public CreateSvcOrderRequestValidator()
    {
        RuleFor(x => x.CustomerId).NotEmpty().WithMessage("CustomerId is required.");
        RuleFor(x => x.Notes).MaximumLength(2000).When(x => x.Notes is not null);
    }
}

public class UpdateSvcOrderRequestValidator : AbstractValidator<UpdateSvcOrderRequest>
{
    public UpdateSvcOrderRequestValidator()
        => RuleFor(x => x.Notes).MaximumLength(2000).When(x => x.Notes is not null);
}

public class ChangeSvcOrderStatusRequestValidator : AbstractValidator<ChangeSvcOrderStatusRequest>
{
    public ChangeSvcOrderStatusRequestValidator()
    {
        RuleFor(x => x.Status).NotNull().WithMessage("Status is required.")
            .IsInEnum().WithMessage("Invalid status.");
        RuleFor(x => x.Reason).MaximumLength(500).When(x => x.Reason is not null);
    }
}

public class AddSvcOrderItemRequestValidator : AbstractValidator<AddSvcOrderItemRequest>
{
    public AddSvcOrderItemRequestValidator()
    {
        RuleFor(x => x.CatalogItemId).NotEmpty().WithMessage("CatalogItemId is required.");
        RuleFor(x => x.Quantity).GreaterThan(0m).WithMessage("Quantity must be positive.");
    }
}

public class UpdateSvcOrderItemRequestValidator : AbstractValidator<UpdateSvcOrderItemRequest>
{
    public UpdateSvcOrderItemRequestValidator()
        => RuleFor(x => x.Quantity).GreaterThan(0m).WithMessage("Quantity must be positive.");
}
```

- [ ] **Step 3: Build → success. Commit** — `feat(service): order DTOs + validators (PR4)`

---

## Task 6: `SvcOrderService` + `OrdersController` + DI

- [ ] **Step 1: `SvcOrderService.cs`**

```csharp
using Nexo.Application.Common.Interfaces;
using Nexo.Application.Modules.Service.Interfaces;
using Nexo.Domain.Entities;
using Nexo.Domain.Exceptions;
using Nexo.Domain.Modules.Service;

namespace Nexo.Application.Modules.Service;

/// <summary>
/// Use cases for the SvcOrder aggregate. Orders are created manually or from an appointment.
/// All references (customer/subject/professional/catalog) are validated through tenant/store-
/// filtered repositories (cross-tenant → 404); inactive professional/catalog and subject/customer
/// mismatch and RequiresSubject → 422. TotalAmount is always recomputed from items server-side.
/// </summary>
public class SvcOrderService
{
    private readonly ISvcOrderRepository        _orders;
    private readonly ISvcOrderItemRepository    _items;
    private readonly ICustomerRepository        _customers;
    private readonly ISvcSubjectRepository      _subjects;
    private readonly ISvcProfessionalRepository _professionals;
    private readonly ISvcCatalogItemRepository  _catalog;
    private readonly ISvcAppointmentRepository  _appointments;
    private readonly ICurrentTenant             _currentTenant;

    public SvcOrderService(
        ISvcOrderRepository orders, ISvcOrderItemRepository items, ICustomerRepository customers,
        ISvcSubjectRepository subjects, ISvcProfessionalRepository professionals,
        ISvcCatalogItemRepository catalog, ISvcAppointmentRepository appointments,
        ICurrentTenant currentTenant)
    {
        _orders = orders; _items = items; _customers = customers; _subjects = subjects;
        _professionals = professionals; _catalog = catalog; _appointments = appointments;
        _currentTenant = currentTenant;
    }

    // ── Queries ──────────────────────────────────────────────────────────────
    public async Task<IReadOnlyList<SvcOrderDto>> GetAllAsync(
        SvcOrderStatus? status, Guid? customerId, Guid? subjectId, Guid? professionalId,
        Guid? appointmentId, CancellationToken ct = default)
    {
        var orders = await _orders.GetAllAsync(status, customerId, subjectId, professionalId, appointmentId, ct);
        var dtos = new List<SvcOrderDto>(orders.Count);
        foreach (var o in orders)
            dtos.Add(MapToDto(o, await _items.GetByOrderAsync(o.Id, ct)));
        return dtos;
    }

    public async Task<SvcOrderDto> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var order = await _orders.GetByIdWithItemsAsync(id, ct) ?? throw new NotFoundException("SvcOrder", id);
        return MapToDto(order, order.Items);
    }

    // ── Create (manual) ──────────────────────────────────────────────────────
    public async Task<SvcOrderDto> CreateAsync(CreateSvcOrderRequest r, CancellationToken ct = default)
    {
        await EnsureCustomerAsync(r.CustomerId, ct);
        await EnsureSubjectAsync(r.SubjectId, r.CustomerId, ct);
        await EnsureProfessionalActiveAsync(r.ProfessionalId, ct);

        var order = SvcOrder.Create(
            _currentTenant.Id, GenerateCode(), r.CustomerId, r.SubjectId, r.ProfessionalId, null, r.Notes);
        await _orders.AddAsync(order, ct);
        await _orders.SaveChangesAsync(ct);
        return MapToDto(order, []);
    }

    // ── Create (from appointment) ────────────────────────────────────────────
    public async Task<SvcOrderDto> CreateFromAppointmentAsync(Guid appointmentId, CancellationToken ct = default)
    {
        var appt = await _appointments.GetByIdAsync(appointmentId, ct)
            ?? throw new NotFoundException("SvcAppointment", appointmentId);

        if (appt.Status is SvcAppointmentStatus.Cancelled or SvcAppointmentStatus.NoShow)
            throw new DomainException($"Cannot create an order from a {appt.Status} appointment.");

        if (await _orders.ExistsForAppointmentAsync(appointmentId, ct))
            throw new ConflictException("An order already exists for this appointment.");

        var catalog = await _catalog.GetByIdAsync(appt.CatalogItemId, ct)
            ?? throw new NotFoundException("SvcCatalogItem", appt.CatalogItemId);

        var order = SvcOrder.Create(
            _currentTenant.Id, GenerateCode(), appt.CustomerId,
            appt.SubjectId, appt.ProfessionalId, appt.Id, null);
        await _orders.AddAsync(order, ct);

        // Initial item: price snapshot from the APPOINTMENT, name/commission from the catalog.
        var item = SvcOrderItem.Create(
            _currentTenant.Id, order.Id, appt.CatalogItemId, appt.ProfessionalId,
            catalog.Name, catalog.Description, 1m, appt.PriceSnapshot, catalog.CommissionPercent);
        await _items.AddAsync(item, ct);

        order.RecalculateTotal(new[] { item });
        _orders.Update(order);
        await _orders.SaveChangesAsync(ct);
        return MapToDto(order, new[] { item });
    }

    // ── Update / status ──────────────────────────────────────────────────────
    public async Task<SvcOrderDto> UpdateAsync(Guid id, UpdateSvcOrderRequest r, CancellationToken ct = default)
    {
        var order = await _orders.GetByIdAsync(id, ct) ?? throw new NotFoundException("SvcOrder", id);
        order.EnsureEditable();

        await EnsureSubjectAsync(r.SubjectId, order.CustomerId, ct);
        await EnsureProfessionalActiveAsync(r.ProfessionalId, ct);
        if (r.SubjectId is null) await EnsureNoItemRequiresSubjectAsync(order.Id, ct);

        order.UpdateDetails(r.SubjectId, r.ProfessionalId, r.Notes);
        _orders.Update(order);
        await _orders.SaveChangesAsync(ct);
        return MapToDto(order, await _items.GetByOrderAsync(order.Id, ct));
    }

    public async Task<SvcOrderDto> ChangeStatusAsync(Guid id, ChangeSvcOrderStatusRequest r, CancellationToken ct = default)
    {
        var order = await _orders.GetByIdAsync(id, ct) ?? throw new NotFoundException("SvcOrder", id);
        order.ChangeStatus(r.Status!.Value, r.Reason);
        _orders.Update(order);
        await _orders.SaveChangesAsync(ct);
        return MapToDto(order, await _items.GetByOrderAsync(order.Id, ct));
    }

    // ── Items ────────────────────────────────────────────────────────────────
    public async Task<SvcOrderDto> AddItemAsync(Guid orderId, AddSvcOrderItemRequest r, CancellationToken ct = default)
    {
        var order = await _orders.GetByIdAsync(orderId, ct) ?? throw new NotFoundException("SvcOrder", orderId);
        order.EnsureEditable();

        var catalog = await _catalog.GetByIdAsync(r.CatalogItemId, ct)
            ?? throw new NotFoundException("SvcCatalogItem", r.CatalogItemId);
        if (!catalog.IsActive) throw new DomainException("Catalog item is not active.");
        if (catalog.RequiresSubject && order.SubjectId is null)
            throw new DomainException("This service requires a subject on the order.");
        await EnsureProfessionalActiveAsync(r.ProfessionalId, ct);

        var item = SvcOrderItem.Create(
            _currentTenant.Id, orderId, r.CatalogItemId, r.ProfessionalId,
            catalog.Name, catalog.Description, r.Quantity, catalog.Price, catalog.CommissionPercent);
        await _items.AddAsync(item, ct);

        var all = (await _items.GetByOrderAsync(orderId, ct)).Append(item).ToList();
        order.RecalculateTotal(all);
        _orders.Update(order);
        await _orders.SaveChangesAsync(ct);
        return MapToDto(order, all);
    }

    public async Task<SvcOrderDto> UpdateItemAsync(
        Guid orderId, Guid itemId, UpdateSvcOrderItemRequest r, CancellationToken ct = default)
    {
        var order = await _orders.GetByIdAsync(orderId, ct) ?? throw new NotFoundException("SvcOrder", orderId);
        order.EnsureEditable();
        var item = await _items.GetByIdAsync(itemId, ct);
        if (item is null || item.OrderId != orderId) throw new NotFoundException("SvcOrderItem", itemId);
        await EnsureProfessionalActiveAsync(r.ProfessionalId, ct);

        item.Update(r.Quantity, r.ProfessionalId);
        _items.Update(item);

        var all = await _items.GetByOrderAsync(orderId, ct);
        order.RecalculateTotal(all);
        _orders.Update(order);
        await _orders.SaveChangesAsync(ct);
        return MapToDto(order, all);
    }

    public async Task<SvcOrderDto> RemoveItemAsync(Guid orderId, Guid itemId, CancellationToken ct = default)
    {
        var order = await _orders.GetByIdAsync(orderId, ct) ?? throw new NotFoundException("SvcOrder", orderId);
        order.EnsureEditable();
        var item = await _items.GetByIdAsync(itemId, ct);
        if (item is null || item.OrderId != orderId) throw new NotFoundException("SvcOrderItem", itemId);

        _items.Remove(item);
        var remaining = (await _items.GetByOrderAsync(orderId, ct)).Where(i => i.Id != itemId).ToList();
        order.RecalculateTotal(remaining);
        _orders.Update(order);
        await _orders.SaveChangesAsync(ct);
        return MapToDto(order, remaining);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────
    private static string GenerateCode()
        => $"OS-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid():N}"[..12].ToUpperInvariant();

    private async Task EnsureCustomerAsync(Guid customerId, CancellationToken ct)
        => _ = await _customers.GetByIdAsync(customerId, ct) ?? throw new NotFoundException(nameof(Customer), customerId);

    private async Task EnsureSubjectAsync(Guid? subjectId, Guid customerId, CancellationToken ct)
    {
        if (subjectId is not { } sid) return;
        var subject = await _subjects.GetByIdAsync(sid, ct) ?? throw new NotFoundException("SvcSubject", sid);
        if (subject.CustomerId != customerId)
            throw new DomainException("Subject does not belong to the customer.");
    }

    private async Task EnsureProfessionalActiveAsync(Guid? professionalId, CancellationToken ct)
    {
        if (professionalId is not { } pid) return;
        var professional = await _professionals.GetByIdAsync(pid, ct)
            ?? throw new NotFoundException("SvcProfessional", pid);
        if (!professional.IsActive) throw new DomainException("Professional is not active.");
    }

    private async Task EnsureNoItemRequiresSubjectAsync(Guid orderId, CancellationToken ct)
    {
        var items = await _items.GetByOrderAsync(orderId, ct);
        foreach (var it in items)
        {
            var catalog = await _catalog.GetByIdAsync(it.CatalogItemId, ct);
            if (catalog is { RequiresSubject: true })
                throw new DomainException("An item on this order requires a subject; cannot clear it.");
        }
    }

    private static SvcOrderDto MapToDto(SvcOrder o, IEnumerable<SvcOrderItem> items) => new(
        Id: o.Id, StoreId: o.StoreId, Code: o.Code, CustomerId: o.CustomerId, SubjectId: o.SubjectId,
        ProfessionalId: o.ProfessionalId, AppointmentId: o.AppointmentId, Status: o.Status, Notes: o.Notes,
        CancellationReason: o.CancellationReason, TotalAmount: o.TotalAmount,
        Items: items.Select(MapItemToDto).ToList(), CreatedAt: o.CreatedAt, UpdatedAt: o.UpdatedAt);

    private static SvcOrderItemDto MapItemToDto(SvcOrderItem i) => new(
        Id: i.Id, OrderId: i.OrderId, CatalogItemId: i.CatalogItemId, ProfessionalId: i.ProfessionalId,
        NameSnapshot: i.NameSnapshot, DescriptionSnapshot: i.DescriptionSnapshot, Quantity: i.Quantity,
        UnitPriceSnapshot: i.UnitPriceSnapshot, CommissionPercentSnapshot: i.CommissionPercentSnapshot,
        TotalAmount: i.TotalAmount, CreatedAt: i.CreatedAt, UpdatedAt: i.UpdatedAt);
}
```

- [ ] **Step 2: Register in Application `DependencyInjection.cs`** — `services.AddScoped<SvcOrderService>();`

- [ ] **Step 3: `OrdersController.cs`**

```csharp
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nexo.Api.Attributes;
using Nexo.Application.Modules.Service;
using Nexo.Domain.Modules.Service;

namespace Nexo.Api.Controllers.Modules.Service;

/// <summary>
/// Service orders (ORKEN SERVICE — ordem de serviço). Store-scoped aggregate with line items.
/// All endpoints require an active service-family subscription.
/// </summary>
[ApiController]
[Route("api/v1/service/orders")]
[Authorize]
[RequireServiceModule]
public class OrdersController : ControllerBase
{
    private readonly SvcOrderService                          _service;
    private readonly IValidator<CreateSvcOrderRequest>        _createValidator;
    private readonly IValidator<UpdateSvcOrderRequest>        _updateValidator;
    private readonly IValidator<ChangeSvcOrderStatusRequest>  _statusValidator;
    private readonly IValidator<AddSvcOrderItemRequest>       _addItemValidator;
    private readonly IValidator<UpdateSvcOrderItemRequest>    _updateItemValidator;

    public OrdersController(
        SvcOrderService service,
        IValidator<CreateSvcOrderRequest> createValidator,
        IValidator<UpdateSvcOrderRequest> updateValidator,
        IValidator<ChangeSvcOrderStatusRequest> statusValidator,
        IValidator<AddSvcOrderItemRequest> addItemValidator,
        IValidator<UpdateSvcOrderItemRequest> updateItemValidator)
    {
        _service = service; _createValidator = createValidator; _updateValidator = updateValidator;
        _statusValidator = statusValidator; _addItemValidator = addItemValidator; _updateItemValidator = updateItemValidator;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<SvcOrderDto>>> GetAll(
        [FromQuery] SvcOrderStatus? status, [FromQuery] Guid? customerId, [FromQuery] Guid? subjectId,
        [FromQuery] Guid? professionalId, [FromQuery] Guid? appointmentId, CancellationToken ct = default)
        => Ok(await _service.GetAllAsync(status, customerId, subjectId, professionalId, appointmentId, ct));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<SvcOrderDto>> GetById(Guid id, CancellationToken ct)
        => Ok(await _service.GetByIdAsync(id, ct));

    [HttpPost]
    public async Task<ActionResult<SvcOrderDto>> Create([FromBody] CreateSvcOrderRequest request, CancellationToken ct)
    {
        await _createValidator.ValidateAndThrowAsync(request, ct);
        var dto = await _service.CreateAsync(request, ct);
        return CreatedAtAction(nameof(GetById), new { id = dto.Id }, dto);
    }

    [HttpPost("from-appointment/{appointmentId:guid}")]
    public async Task<ActionResult<SvcOrderDto>> CreateFromAppointment(Guid appointmentId, CancellationToken ct)
    {
        var dto = await _service.CreateFromAppointmentAsync(appointmentId, ct);
        return CreatedAtAction(nameof(GetById), new { id = dto.Id }, dto);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<SvcOrderDto>> Update(Guid id, [FromBody] UpdateSvcOrderRequest request, CancellationToken ct)
    {
        await _updateValidator.ValidateAndThrowAsync(request, ct);
        return Ok(await _service.UpdateAsync(id, request, ct));
    }

    [HttpPatch("{id:guid}/status")]
    public async Task<ActionResult<SvcOrderDto>> ChangeStatus(Guid id, [FromBody] ChangeSvcOrderStatusRequest request, CancellationToken ct)
    {
        await _statusValidator.ValidateAndThrowAsync(request, ct);
        return Ok(await _service.ChangeStatusAsync(id, request, ct));
    }

    [HttpPost("{id:guid}/items")]
    public async Task<ActionResult<SvcOrderDto>> AddItem(Guid id, [FromBody] AddSvcOrderItemRequest request, CancellationToken ct)
    {
        await _addItemValidator.ValidateAndThrowAsync(request, ct);
        return Ok(await _service.AddItemAsync(id, request, ct));
    }

    [HttpPut("{id:guid}/items/{itemId:guid}")]
    public async Task<ActionResult<SvcOrderDto>> UpdateItem(Guid id, Guid itemId, [FromBody] UpdateSvcOrderItemRequest request, CancellationToken ct)
    {
        await _updateItemValidator.ValidateAndThrowAsync(request, ct);
        return Ok(await _service.UpdateItemAsync(id, itemId, request, ct));
    }

    [HttpDelete("{id:guid}/items/{itemId:guid}")]
    public async Task<ActionResult<SvcOrderDto>> RemoveItem(Guid id, Guid itemId, CancellationToken ct)
        => Ok(await _service.RemoveItemAsync(id, itemId, ct));
}
```

- [ ] **Step 4: Build → success. Commit** — `feat(service): SvcOrderService + OrdersController (PR4)`

---

## Task 7: Records `ContextType=Order` (extend PR2)

**Files:** Modify `SvcValidators.cs` (records `Supported`), `SvcRecordEntryService.cs` (Order context + inject `ISvcOrderRepository`), `RecordsController.cs` (GET allowed types).

- [ ] **Step 1:** In `CreateSvcRecordEntryRequestValidator`, add `Order` to `Supported`:

```csharp
    private static readonly SvcRecordContextType[] Supported =
        { SvcRecordContextType.Customer, SvcRecordContextType.Subject, SvcRecordContextType.Order };
```

- [ ] **Step 2:** In `SvcRecordEntryService`, inject `ISvcOrderRepository _orders` (add ctor param + field) and add the `Order` case to `EnsureContextExistsAsync`:

```csharp
            case SvcRecordContextType.Order:
                _ = await _orders.GetByIdAsync(id, ct) ?? throw new NotFoundException("SvcOrder", id);
                break;
```

- [ ] **Step 3:** In `RecordsController.GetByContext`, widen the allowed filter:

```csharp
        if (contextType is not (SvcRecordContextType.Customer or SvcRecordContextType.Subject or SvcRecordContextType.Order))
            return BadRequest(new { error = "ContextType is not supported yet." });
```

- [ ] **Step 4: Build → success. Commit** — `feat(service): records accept ContextType=Order (PR4)`

---

## Task 8: Integration tests

**Files:** Create `ServiceOrdersTests.cs`; extend `ServiceRecordsTests.cs` with Order-context cases.

`ServiceOrdersTests` covers: gate 403/200; manual create→get→list; add/update/delete item with total recompute; price snapshot stays after the catalog price changes; status transitions valid + invalid (422); terminal blocks edit + item changes (422); requiresSubject at add-item (422); subject/customer mismatch (422); inactive professional/catalog (422); from-appointment creates order + initial item using `Appointment.PriceSnapshot`; from-appointment no-duplicate (409); from cancelled/no-show appointment (422); payload 400; unknown refs 404; cross-tenant 404.

Key helper sketch (UTC times for appointments; reuse PR1–PR3 routes):

```csharp
// price-snapshot-immutability test core:
var catalogId = await CreateCatalogAsync(c, price: 100m, requiresSubject: false);
var orderId   = await CreateOrderAsync(c, customerId);
await AddItemAsync(c, orderId, catalogId, qty: 2);                       // 2 × 100 = 200
await c.PutAsJsonAsync($"/api/v1/service/catalog/{catalogId}",
    new { name = "X", durationMinutes = 60, price = 999m, requiresSubject = false }); // catalog now 999
var dto = await (await c.GetAsync($"/api/v1/service/orders/{orderId}")).Content.ReadFromJsonAsync<JsonElement>();
dto.GetProperty("totalAmount").GetDecimal().Should().Be(200m);          // snapshot unchanged
dto.GetProperty("items").EnumerateArray().Single().GetProperty("unitPriceSnapshot").GetDecimal().Should().Be(100m);

// from-appointment uses Appointment.PriceSnapshot (catalog 120 at booking; appointment locks it):
var appt = await CreateAppointmentAsync(...); // PriceSnapshot 120 from catalog at booking
var fromAppt = await c.PostAsync($"/api/v1/service/orders/from-appointment/{appt}", null);
fromAppt.StatusCode.Should().Be(HttpStatusCode.Created);
var ord = await fromAppt.Content.ReadFromJsonAsync<JsonElement>();
ord.GetProperty("appointmentId").GetGuid().Should().Be(appt);
ord.GetProperty("items").EnumerateArray().Single().GetProperty("unitPriceSnapshot").GetDecimal().Should().Be(120m);
// duplicate:
(await c.PostAsync($"/api/v1/service/orders/from-appointment/{appt}", null)).StatusCode.Should().Be(HttpStatusCode.Conflict);
```

Foreign-tenant order seed mirrors PR3's appointment seed (create tenant B + store + customer; `SvcOrder.Create(...)`, set `StoreId` explicitly via `db.Entry(order).Property("StoreId").CurrentValue = storeId`).

`ServiceRecordsTests` additions: create a record with `contextType=Order` against a real order → 201; list by that order → contains it; foreign-tenant order context → 404; re-assert a Customer-context record still works.

- [ ] **Step 1:** Run new tests, watch fail (orders endpoints missing); the 404 cases may pass coincidentally.
- [ ] **Step 2:** After Tasks 1–7, run again → green.
- [ ] **Step 3: Commit** — `test(service): integration coverage for orders/OS + records-order (PR4)`

---

## Task 9: Full verification + PR

- [ ] **Step 1:** `dotnet build Nexo.sln` → 0 errors.
- [ ] **Step 2:** `dotnet test tests/Nexo.UnitTests` → green.
- [ ] **Step 3:** `dotnet test tests/Nexo.IntegrationTests` → green (incl. unchanged PR2 records tests).
- [ ] **Step 4:** Re-read migration `Up()` — only `CreateTable(svc_orders)` + `CreateTable(svc_order_items)` + indexes + new FKs.
- [ ] **Step 5:** `git diff --name-only origin/master` — only Service files + migration. No Auth/Redis/Stripe/SuperAdmin/Build, no frontend, no `dist`. (StorageController untouched.)
- [ ] **Step 6:** Push + `gh pr create --base master` (no merge).

---

## Self-review (spec coverage)

Entities `SvcOrder`+`SvcOrderItem` (T1) ✔ · status machine + invalid/terminal guards (T1) ✔ · server total recompute (T1 domain + T6 service) ✔ · per-item snapshots, price immutable (T1+T6) ✔ · manual create (T6) ✔ · from-appointment + Appointment.PriceSnapshot + no-dup + reject Cancelled/NoShow + no status mutation (T6) ✔ · ref rules active/mismatch/requiresSubject (T6) ✔ · endpoints orders + items + from-appointment + status (T6) ✔ · records ContextType=Order (T7) ✔ · additive migration incl. partial unique on appointment_id (T2/T3) ✔ · tests (T8) ✔ · gate + isolation ✔.

## Risks
1. **Order-per-appointment race** — app check + partial unique index; a concurrent race could 500 (rare, documented; same posture as PR3 overlap).
2. **Code uniqueness is probabilistic** (Guid-derived, no DB constraint) — collision negligible for a single-store v1; documented.
3. **PUT clearing subject with a RequiresSubject item** — guarded by `EnsureNoItemRequiresSubjectAsync` (loads items + catalogs). Slight N+1, acceptable for small orders.
4. **Two StoreEntity in one aggregate with `Include`** — both carry the tenant+store query filter; verified by integration tests that items load and totals compute.
5. **Touching PR2 records code** (T7) — re-run the PR2 records tests to confirm Customer/Subject records still pass.
