# Orken Service PR5 — Packages / Pacotes Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add the Service packages engine — package templates (`SvcPackage`/`SvcPackageItem`), assignment to a customer creating consumable balances (`SvcCustomerPackage`/`SvcCustomerPackageItem`), balance consumption with append-only history (`SvcPackageUsage`) and optional order links — granting the **operational right of use** only (no payment, no cash, no OS-total change).

**Architecture:** Two aggregates mirroring the PR4 `SvcOrder` pattern (parent holds `ICollection<Item> Items`; children are separate StoreEntities with their own DbSet/repo + a `ParentId` FK via `HasMany().WithOne().HasForeignKey().OnDelete(Cascade)`; **never call `Update` on a freshly-`Added` aggregate — that 500'd in PR4**). `SvcCustomerPackage` has a status machine (Active→Consumed/Cancelled; Expired deferred to a job). Consumption decrements `RemainingQuantity`, writes a `SvcPackageUsage`, and auto-marks the package `Consumed` when every balance hits zero. Migration is strictly additive (5 `CreateTable` + indexes + new FKs to existing tables).

**Tech Stack:** .NET 8, EF Core 8 (Npgsql/PostgreSQL, `timestamptz`), FluentValidation, xUnit + FluentAssertions + Testcontainers.

---

## Design decisions (locked)

| Concern | Decision |
|---|---|
| `SvcPackage` base | `StoreEntity` **with** `IsActive` → `ConfigureStoreScopedSvcEntity`. Template; no status machine. |
| `SvcPackageItem`, `SvcCustomerPackage`, `SvcCustomerPackageItem`, `SvcPackageUsage` base | `StoreEntity` **no `IsActive`** → `ConfigureStoreScopedSvcEntityNoActive`. |
| `SvcCustomerPackage` status | `SvcCustomerPackageStatus { Active, Consumed, Expired, Cancelled }`, `.HasConversion<string>()`. Consumed/Expired/Cancelled terminal. |
| Quantities | `decimal` everywhere (IncludedQuantity / Total / Remaining / consume Quantity) — consistent with `SvcOrderItem.Quantity` numeric. |
| Snapshots | `SvcPackageItem.NameSnapshot` from catalog at add; `SvcCustomerPackageItem.NameSnapshot`/`TotalQuantity` snapshot the package item at assign; `PriceSnapshot` from `SvcPackage.Price`. Later template edits never touch assigned packages. |
| `Code` | Server-generated `PKG-yyyyMMdd-XXXXXX` (Guid-derived, no DB unique constraint). |
| ExpiresAt | Server-computed: `StartsAt + ValidityDays` (null if no ValidityDays). `StartsAt` must be UTC (validator) for `timestamptz`. |
| Expiry handling | **Block consume** when `ExpiresAt < now` (422). **No auto-mutation to Expired** on GET/consume (owner: "mutação automática pode ficar para depois"). |
| Auto-Consumed | When every balance `RemainingQuantity == 0` after a consume → `Status = Consumed`. |
| Order link | Consume may reference `OrderId`/`OrderItemId` (validated: order exists 404, same customer 422, same subject if package has one 422, item belongs to order 422). **Never** changes the order's `TotalAmount` or status — operational history only. |
| **Reversal** | **DEFERRED** to a later PR (documented). Rationale: PR5 already adds 5 tables, 2 aggregates, a consumption state machine, and ~28 integration tests; reversal needs its own semantics (restore-from-`Consumed` → re-`Active`, and the "reverse on Cancelled/Expired" rule) that warrant a focused, separately-reviewed PR. Keeps `SvcPackageUsage` clean/append-only. Owner explicitly allowed deferral if it expands the PR too much. |
| Status codes | payload/bad-status/non-UTC StartsAt/orderItemId-without-orderId → 400; missing refs → 404; domain rules (inactive package, empty package, subject mismatch, insufficient balance, terminal/expired consume, order mismatch) → 422. |

## File structure

**Domain** (`Nexo.Domain/Modules/Service/`): `SvcCustomerPackageStatus.cs`, `SvcPackage.cs`, `SvcPackageItem.cs`, `SvcCustomerPackage.cs`, `SvcCustomerPackageItem.cs`, `SvcPackageUsage.cs`
**Application** (`Nexo.Application/Modules/Service/`): `SvcPackageDtos.cs`, `SvcCustomerPackageDtos.cs`, `SvcPackageService.cs`, `SvcCustomerPackageService.cs`, modify `SvcValidators.cs`, 5 repo interfaces under `Interfaces/`, modify `DependencyInjection.cs`
**Infrastructure** (`Nexo.Infrastructure/`): 5 EF configs under `Persistence/Configurations/Modules/Service/`, 5 repos under `Repositories/Modules/Service/`, modify `DependencyInjection.cs`, modify `Persistence/NexoDbContext.cs`, migration `<ts>_AddServicePackages.cs`
**API** (`Nexo.Api/`): `Controllers/Modules/Service/PackagesController.cs`, `Controllers/Modules/Service/CustomerPackagesController.cs`
**Tests**: `Nexo.UnitTests/Service/SvcPackageTests.cs`, `SvcCustomerPackageTests.cs`; `Nexo.IntegrationTests/Service/ServicePackagesTests.cs`, `ServiceCustomerPackagesTests.cs`

---

## Task 1: Domain — status enum + 5 entities (unit tested)

**Files:** the 6 domain files + `SvcPackageTests.cs` + `SvcCustomerPackageTests.cs`.

- [ ] **Step 1: Write failing unit tests**

```csharp
// SvcPackageTests.cs
using FluentAssertions;
using Nexo.Domain.Exceptions;
using Nexo.Domain.Modules.Service;
using Xunit;

namespace Nexo.UnitTests.Service;

public class SvcPackageTests
{
    private static readonly Guid T = Guid.NewGuid();

    [Fact]
    public void Create_defaults_active()
    {
        var p = SvcPackage.Create(T, "Plano Pet", 500m, "desc", 30);
        p.Name.Should().Be("Plano Pet");
        p.Price.Should().Be(500m);
        p.ValidityDays.Should().Be(30);
        p.IsActive.Should().BeTrue();
    }

    [Fact] public void Create_blank_name_throws()
        => ((Action)(() => SvcPackage.Create(T, " ", 1m))).Should().Throw<DomainException>();
    [Fact] public void Create_negative_price_throws()
        => ((Action)(() => SvcPackage.Create(T, "x", -1m))).Should().Throw<DomainException>();
    [Fact] public void Create_non_positive_validity_throws()
        => ((Action)(() => SvcPackage.Create(T, "x", 1m, null, 0))).Should().Throw<DomainException>();

    [Fact]
    public void Activate_deactivate_toggles()
    {
        var p = SvcPackage.Create(T, "x", 1m);
        p.Deactivate(); p.IsActive.Should().BeFalse();
        p.Activate();   p.IsActive.Should().BeTrue();
    }

    [Fact]
    public void PackageItem_create_computes_snapshot()
    {
        var i = SvcPackageItem.Create(T, Guid.NewGuid(), Guid.NewGuid(), "Banho", 4m);
        i.NameSnapshot.Should().Be("Banho");
        i.IncludedQuantity.Should().Be(4m);
    }

    [Fact] public void PackageItem_non_positive_qty_throws()
        => ((Action)(() => SvcPackageItem.Create(T, Guid.NewGuid(), Guid.NewGuid(), "x", 0m)))
            .Should().Throw<DomainException>();
}
```

```csharp
// SvcCustomerPackageTests.cs
using FluentAssertions;
using Nexo.Domain.Exceptions;
using Nexo.Domain.Modules.Service;
using Xunit;

namespace Nexo.UnitTests.Service;

public class SvcCustomerPackageTests
{
    private static readonly Guid T = Guid.NewGuid();
    private static readonly DateTime Start = new(2026, 6, 18, 0, 0, 0, DateTimeKind.Utc);

    private static SvcCustomerPackage New(DateTime? expires = null)
        => SvcCustomerPackage.Create(T, "PKG-1", Guid.NewGuid(), Guid.NewGuid(), null,
            Start, expires, 500m, null);

    [Fact]
    public void Create_defaults_active()
    {
        var cp = New(Start.AddDays(30));
        cp.Status.Should().Be(SvcCustomerPackageStatus.Active);
        cp.PriceSnapshot.Should().Be(500m);
        cp.ExpiresAt.Should().Be(Start.AddDays(30));
        cp.IsTerminal.Should().BeFalse();
    }

    [Fact] public void Create_expires_before_start_throws()
        => ((Action)(() => New(Start.AddDays(-1)))).Should().Throw<DomainException>();

    [Fact]
    public void Cancel_sets_status_and_blocks_when_terminal()
    {
        var cp = New();
        cp.Cancel();
        cp.Status.Should().Be(SvcCustomerPackageStatus.Cancelled);
        cp.IsTerminal.Should().BeTrue();
        ((Action)cp.Cancel).Should().Throw<DomainException>();
    }

    [Fact]
    public void MarkConsumed_only_from_active()
    {
        var cp = New();
        cp.MarkConsumed();
        cp.Status.Should().Be(SvcCustomerPackageStatus.Consumed);
        ((Action)cp.MarkConsumed).Should().Throw<DomainException>();
    }

    [Fact]
    public void IsExpiredAt_true_after_expiry()
    {
        var cp = New(Start.AddDays(1));
        cp.IsExpiredAt(Start.AddDays(2)).Should().BeTrue();
        cp.IsExpiredAt(Start).Should().BeFalse();
    }

    [Fact]
    public void Balance_item_consume_reduces_remaining_and_guards()
    {
        var b = SvcCustomerPackageItem.Create(T, Guid.NewGuid(), Guid.NewGuid(), "Banho", 4m);
        b.RemainingQuantity.Should().Be(4m);
        b.Consume(1m);
        b.RemainingQuantity.Should().Be(3m);
        ((Action)(() => b.Consume(5m))).Should().Throw<DomainException>();   // insufficient
        ((Action)(() => b.Consume(0m))).Should().Throw<DomainException>();   // non-positive
    }

    [Fact]
    public void Usage_create_guards()
    {
        var u = SvcPackageUsage.Create(T, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 2m, null, null, "n");
        u.Quantity.Should().Be(2m);
        ((Action)(() => SvcPackageUsage.Create(T, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 0m, null, null, null)))
            .Should().Throw<DomainException>();
    }
}
```

- [ ] **Step 2: Run, verify fail.**

- [ ] **Step 3: Implement `SvcCustomerPackageStatus.cs`**

```csharp
namespace Nexo.Domain.Modules.Service;

/// <summary>Lifecycle of a <see cref="SvcCustomerPackage"/>. Stored as a string. Consumed/Expired/Cancelled are terminal.</summary>
public enum SvcCustomerPackageStatus { Active, Consumed, Expired, Cancelled }
```

- [ ] **Step 4: Implement `SvcPackage.cs`**

```csharp
using Nexo.Domain.Common;
using Nexo.Domain.Exceptions;

namespace Nexo.Domain.Modules.Service;

/// <summary>
/// A package template (pacote) — store-scoped aggregate. A bundle of catalog services with an
/// included quantity each (<see cref="SvcPackageItem"/>). When assigned to a customer the items
/// become consumable balances (<see cref="SvcCustomerPackage"/>). Price/validity are templates;
/// assignment snapshots them so later edits never touch assigned packages.
/// </summary>
public class SvcPackage : StoreEntity
{
    private SvcPackage() { }
    private SvcPackage(Guid tenantId) : base(tenantId) { }

    public string  Name         { get; private set; } = string.Empty;
    public string? Description  { get; private set; }
    public decimal Price        { get; private set; }
    public int?    ValidityDays { get; private set; }
    public bool    IsActive     { get; private set; }

    public ICollection<SvcPackageItem> Items { get; private set; } = [];

    public static SvcPackage Create(Guid tenantId, string name, decimal price, string? description = null, int? validityDays = null)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new DomainException("Package name is required.");
        EnsurePriceNonNegative(price);
        EnsureValidityPositive(validityDays);
        return new SvcPackage(tenantId)
        {
            Name = name.Trim(), Description = description?.Trim(), Price = price,
            ValidityDays = validityDays, IsActive = true,
        };
    }

    public void UpdateDetails(string name, string? description, int? validityDays)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new DomainException("Package name is required.");
        EnsureValidityPositive(validityDays);
        Name = name.Trim(); Description = description?.Trim(); ValidityDays = validityDays; SetUpdatedAt();
    }

    public void UpdatePrice(decimal price) { EnsurePriceNonNegative(price); Price = price; SetUpdatedAt(); }
    public void Activate()   { IsActive = true;  SetUpdatedAt(); }
    public void Deactivate() { IsActive = false; SetUpdatedAt(); }

    private static void EnsurePriceNonNegative(decimal p) { if (p < 0m) throw new DomainException("Price cannot be negative."); }
    private static void EnsureValidityPositive(int? d) { if (d is <= 0) throw new DomainException("ValidityDays must be positive when set."); }
}
```

- [ ] **Step 5: Implement `SvcPackageItem.cs`**

```csharp
using Nexo.Domain.Common;
using Nexo.Domain.Exceptions;

namespace Nexo.Domain.Modules.Service;

/// <summary>A template line of a <see cref="SvcPackage"/>: a catalog service + included quantity. Store-scoped.</summary>
public class SvcPackageItem : StoreEntity
{
    private SvcPackageItem() { }
    private SvcPackageItem(Guid tenantId) : base(tenantId) { }

    public Guid    PackageId        { get; private set; }
    public Guid    CatalogItemId    { get; private set; }
    public string  NameSnapshot     { get; private set; } = string.Empty;
    public decimal IncludedQuantity { get; private set; }

    public static SvcPackageItem Create(Guid tenantId, Guid packageId, Guid catalogItemId, string nameSnapshot, decimal includedQuantity)
    {
        if (packageId == Guid.Empty)                 throw new DomainException("PackageId is required.");
        if (catalogItemId == Guid.Empty)             throw new DomainException("CatalogItemId is required.");
        if (string.IsNullOrWhiteSpace(nameSnapshot)) throw new DomainException("Item name is required.");
        if (includedQuantity <= 0m)                  throw new DomainException("IncludedQuantity must be positive.");
        return new SvcPackageItem(tenantId)
        {
            PackageId = packageId, CatalogItemId = catalogItemId,
            NameSnapshot = nameSnapshot.Trim(), IncludedQuantity = includedQuantity,
        };
    }

    public void UpdateQuantity(decimal includedQuantity)
    {
        if (includedQuantity <= 0m) throw new DomainException("IncludedQuantity must be positive.");
        IncludedQuantity = includedQuantity; SetUpdatedAt();
    }
}
```

- [ ] **Step 6: Implement `SvcCustomerPackage.cs`**

```csharp
using Nexo.Domain.Common;
using Nexo.Domain.Exceptions;

namespace Nexo.Domain.Modules.Service;

/// <summary>
/// A package assigned to a customer — store-scoped aggregate. Holds the consumable balances
/// (<see cref="SvcCustomerPackageItem"/>). Status machine: Active → Consumed (auto when all
/// balances reach zero) or Cancelled; Expired is reserved for a future job (consume is blocked
/// by date here). Grants only the operational right of use — no payment/cash/financial effect.
/// </summary>
public class SvcCustomerPackage : StoreEntity
{
    private SvcCustomerPackage() { }
    private SvcCustomerPackage(Guid tenantId) : base(tenantId) { }

    public string                   Code          { get; private set; } = string.Empty;
    public Guid                     PackageId     { get; private set; }
    public Guid                     CustomerId    { get; private set; }
    public Guid?                    SubjectId     { get; private set; }
    public SvcCustomerPackageStatus Status        { get; private set; }
    public DateTime                 StartsAt      { get; private set; }
    public DateTime?                ExpiresAt     { get; private set; }
    public decimal                  PriceSnapshot { get; private set; }
    public string?                  Notes         { get; private set; }

    public ICollection<SvcCustomerPackageItem> Items { get; private set; } = [];

    public bool IsTerminal => Status is SvcCustomerPackageStatus.Consumed
                                     or SvcCustomerPackageStatus.Expired
                                     or SvcCustomerPackageStatus.Cancelled;

    public static SvcCustomerPackage Create(
        Guid tenantId, string code, Guid packageId, Guid customerId, Guid? subjectId,
        DateTime startsAt, DateTime? expiresAt, decimal priceSnapshot, string? notes)
    {
        if (string.IsNullOrWhiteSpace(code)) throw new DomainException("Code is required.");
        if (packageId == Guid.Empty)         throw new DomainException("Package is required.");
        if (customerId == Guid.Empty)        throw new DomainException("Customer is required.");
        if (priceSnapshot < 0m)              throw new DomainException("Price snapshot cannot be negative.");
        if (expiresAt is { } e && e <= startsAt) throw new DomainException("ExpiresAt must be after StartsAt.");
        return new SvcCustomerPackage(tenantId)
        {
            Code = code.Trim(), PackageId = packageId, CustomerId = customerId, SubjectId = subjectId,
            Status = SvcCustomerPackageStatus.Active, StartsAt = startsAt, ExpiresAt = expiresAt,
            PriceSnapshot = priceSnapshot, Notes = notes?.Trim(),
        };
    }

    public void Cancel()
    {
        if (IsTerminal) throw new DomainException($"Cannot cancel a {Status} customer package.");
        Status = SvcCustomerPackageStatus.Cancelled; SetUpdatedAt();
    }

    public void MarkConsumed()
    {
        if (Status != SvcCustomerPackageStatus.Active)
            throw new DomainException($"Only an active package can be marked consumed (current: {Status}).");
        Status = SvcCustomerPackageStatus.Consumed; SetUpdatedAt();
    }

    public bool IsExpiredAt(DateTime nowUtc) => ExpiresAt is { } e && e < nowUtc;
}
```

- [ ] **Step 7: Implement `SvcCustomerPackageItem.cs`**

```csharp
using Nexo.Domain.Common;
using Nexo.Domain.Exceptions;

namespace Nexo.Domain.Modules.Service;

/// <summary>A consumable balance line of a <see cref="SvcCustomerPackage"/>. RemainingQuantity never goes negative.</summary>
public class SvcCustomerPackageItem : StoreEntity
{
    private SvcCustomerPackageItem() { }
    private SvcCustomerPackageItem(Guid tenantId) : base(tenantId) { }

    public Guid    CustomerPackageId { get; private set; }
    public Guid    CatalogItemId     { get; private set; }
    public string  NameSnapshot      { get; private set; } = string.Empty;
    public decimal TotalQuantity     { get; private set; }
    public decimal RemainingQuantity { get; private set; }

    public static SvcCustomerPackageItem Create(Guid tenantId, Guid customerPackageId, Guid catalogItemId, string nameSnapshot, decimal totalQuantity)
    {
        if (customerPackageId == Guid.Empty)         throw new DomainException("CustomerPackageId is required.");
        if (catalogItemId == Guid.Empty)             throw new DomainException("CatalogItemId is required.");
        if (string.IsNullOrWhiteSpace(nameSnapshot)) throw new DomainException("Item name is required.");
        if (totalQuantity <= 0m)                     throw new DomainException("TotalQuantity must be positive.");
        return new SvcCustomerPackageItem(tenantId)
        {
            CustomerPackageId = customerPackageId, CatalogItemId = catalogItemId,
            NameSnapshot = nameSnapshot.Trim(), TotalQuantity = totalQuantity, RemainingQuantity = totalQuantity,
        };
    }

    public void Consume(decimal quantity)
    {
        if (quantity <= 0m)                throw new DomainException("Quantity must be positive.");
        if (quantity > RemainingQuantity)  throw new DomainException("Insufficient package balance.");
        RemainingQuantity -= quantity; SetUpdatedAt();
    }
}
```

- [ ] **Step 8: Implement `SvcPackageUsage.cs`**

```csharp
using Nexo.Domain.Common;
using Nexo.Domain.Exceptions;

namespace Nexo.Domain.Modules.Service;

/// <summary>
/// Append-only record of a balance consumption. May reference an order/order-item for operational
/// traceability — that link NEVER changes the order's total or status.
/// </summary>
public class SvcPackageUsage : StoreEntity
{
    private SvcPackageUsage() { }
    private SvcPackageUsage(Guid tenantId) : base(tenantId) { }

    public Guid    CustomerPackageId     { get; private set; }
    public Guid    CustomerPackageItemId { get; private set; }
    public Guid    CatalogItemId         { get; private set; }
    public Guid?   OrderId               { get; private set; }
    public Guid?   OrderItemId           { get; private set; }
    public decimal Quantity              { get; private set; }
    public string? Notes                 { get; private set; }

    public static SvcPackageUsage Create(
        Guid tenantId, Guid customerPackageId, Guid customerPackageItemId, Guid catalogItemId,
        decimal quantity, Guid? orderId, Guid? orderItemId, string? notes)
    {
        if (customerPackageId == Guid.Empty)     throw new DomainException("CustomerPackageId is required.");
        if (customerPackageItemId == Guid.Empty) throw new DomainException("CustomerPackageItemId is required.");
        if (catalogItemId == Guid.Empty)         throw new DomainException("CatalogItemId is required.");
        if (quantity <= 0m)                      throw new DomainException("Quantity must be positive.");
        return new SvcPackageUsage(tenantId)
        {
            CustomerPackageId = customerPackageId, CustomerPackageItemId = customerPackageItemId,
            CatalogItemId = catalogItemId, OrderId = orderId, OrderItemId = orderItemId,
            Quantity = quantity, Notes = notes?.Trim(),
        };
    }
}
```

- [ ] **Step 9: Run, verify pass. Commit** — `feat(service): package domain entities + status machine (PR5)`

---

## Task 2: EF configs + DbSets (model verified)

**Files:** 5 configs + DbSets. All reuse the PR2 helpers. Key points below; map every property → snake_case column, `decimal` → `numeric(18,3)` for quantities / `numeric(18,2)` for price, status `HasConversion<string>().HasMaxLength(20)`, `timestamptz` for StartsAt/ExpiresAt.

- [ ] **Step 1: `SvcPackageConfiguration.cs`** — `ConfigureStoreScopedSvcEntity("svc_packages")`; columns name(200)/description(1000)/price `numeric(18,2)`/validity_days(int?); `HasMany(x => x.Items).WithOne().HasForeignKey(x => x.PackageId).HasConstraintName("fk_svc_package_items_package").OnDelete(Cascade)`; index `ix_svc_packages_tenant_store_active` is added by the helper.

- [ ] **Step 2: `SvcPackageItemConfiguration.cs`** — `ConfigureStoreScopedSvcEntityNoActive("svc_package_items")`; package_id/catalog_item_id/name_snapshot(200)/included_quantity `numeric(18,3)`; FK to `SvcCatalogItem` (`fk_svc_package_items_catalog_items`, Restrict); index `ix_svc_package_items_package_id` on package_id.

- [ ] **Step 3: `SvcCustomerPackageConfiguration.cs`** — `ConfigureStoreScopedSvcEntityNoActive("svc_customer_packages")`; code(40)/package_id/customer_id/subject_id?/status(string,20)/starts_at(timestamptz)/expires_at(timestamptz?)/price_snapshot `numeric(18,2)`/notes(2000); `HasMany(x => x.Items).WithOne().HasForeignKey(x => x.CustomerPackageId).HasConstraintName("fk_svc_customer_package_items_cp").OnDelete(Cascade)`; FKs → `SvcPackage` (Restrict), `Customer` (Restrict), `SvcSubject` (Restrict); indexes on customer_id, subject_id, package_id, and `(tenant_id, store_id, status)`.

- [ ] **Step 4: `SvcCustomerPackageItemConfiguration.cs`** — `ConfigureStoreScopedSvcEntityNoActive("svc_customer_package_items")`; customer_package_id/catalog_item_id/name_snapshot(200)/total_quantity `numeric(18,3)`/remaining_quantity `numeric(18,3)`; FK → `SvcCatalogItem` (Restrict); indexes on customer_package_id and catalog_item_id.

- [ ] **Step 5: `SvcPackageUsageConfiguration.cs`** — `ConfigureStoreScopedSvcEntityNoActive("svc_package_usages")`; customer_package_id/customer_package_item_id/catalog_item_id/order_id?/order_item_id?/quantity `numeric(18,3)`/notes(2000); FKs → `SvcCustomerPackage` (Restrict), `SvcCustomerPackageItem` (Restrict), `SvcCatalogItem` (Restrict), `SvcOrder` (Restrict, optional), `SvcOrderItem` (Restrict, optional); index on customer_package_id.

- [ ] **Step 6: DbSets in `NexoDbContext.cs`** (after `SvcOrderItems`):

```csharp
        public DbSet<SvcPackage>             SvcPackages            => Set<SvcPackage>();
        public DbSet<SvcPackageItem>         SvcPackageItems        => Set<SvcPackageItem>();
        public DbSet<SvcCustomerPackage>     SvcCustomerPackages    => Set<SvcCustomerPackage>();
        public DbSet<SvcCustomerPackageItem> SvcCustomerPackageItems => Set<SvcCustomerPackageItem>();
        public DbSet<SvcPackageUsage>        SvcPackageUsages       => Set<SvcPackageUsage>();
```

- [ ] **Step 7: Build → success. Commit** — `feat(service): EF config + DbSets for packages (PR5)`

---

## Task 3: Additive migration

- [ ] **Step 1:** `dotnet ef migrations add AddServicePackages -p src/Nexo.Infrastructure -s src/Nexo.Api -o Persistence/Migrations`
- [ ] **Step 2: PROVE additive.** `Up()` must contain ONLY 5 `CreateTable` (svc_packages, svc_package_items, svc_customer_packages, svc_customer_package_items, svc_package_usages) + `CreateIndex` + FKs to existing tables. No Drop/Alter/Rename/AddColumn on existing tables. STOP if anything else appears.
- [ ] **Step 3:** `has-pending-model-changes` → "No changes…".
- [ ] **Step 4: Commit** — `feat(service): additive migration AddServicePackages (PR5)`

---

## Task 4: Repositories + interfaces + DI

Five repos, all following the PR4 pattern (`FirstOrDefaultAsync`, `Include(x => x.Items)` for the `*WithItems` variants, `AnyAsync`, filtered `GetAll`). Interfaces:

- [ ] **`ISvcPackageRepository`**: `GetByIdAsync`, `GetByIdWithItemsAsync` (Include Items), `GetAllAsync(bool? active)`, `AddAsync`, `Update`, `SaveChangesAsync`.
- [ ] **`ISvcPackageItemRepository`**: `GetByIdAsync`, `GetByPackageAsync(packageId)`, `ExistsForCatalogAsync(packageId, catalogItemId)`, `AddAsync`, `Update`, `Remove`, `SaveChangesAsync`.
- [ ] **`ISvcCustomerPackageRepository`**: `GetByIdAsync`, `GetByIdWithItemsAsync` (Include Items), `GetAllAsync(Guid? customerId, Guid? subjectId, SvcCustomerPackageStatus? status, Guid? packageId)`, `AddAsync`, `Update`, `SaveChangesAsync`.
- [ ] **`ISvcCustomerPackageItemRepository`**: `GetByCustomerPackageAsync`, `Update`, `AddAsync`, `SaveChangesAsync`.
- [ ] **`ISvcPackageUsageRepository`**: `GetByCustomerPackageAsync(customerPackageId)`, `AddAsync`, `SaveChangesAsync`.

Implement each in `Repositories/Modules/Service/`, register all 5 in Infrastructure `DependencyInjection.cs`. Build → commit `feat(service): package repositories + DI (PR5)`.

---

## Task 5: DTOs + validators

- [ ] **`SvcPackageDtos.cs`**: `SvcPackageItemDto`, `SvcPackageDto(... Items)`, `CreateSvcPackageRequest(Name, Price, Description?, ValidityDays?)`, `UpdateSvcPackageRequest(Name, Description?, ValidityDays?)`, `UpdateSvcPackagePriceRequest(Price)`, `AddSvcPackageItemRequest(CatalogItemId, IncludedQuantity)`, `UpdateSvcPackageItemRequest(IncludedQuantity)`.
- [ ] **`SvcCustomerPackageDtos.cs`**: `SvcCustomerPackageItemDto`, `SvcCustomerPackageDto(... Items)`, `SvcPackageUsageDto`, `AssignSvcCustomerPackageRequest(PackageId, CustomerId, SubjectId?, StartsAt, Notes?)`, `ConsumeSvcPackageRequest(CatalogItemId, Quantity, OrderId?, OrderItemId?, Notes?)`.
- [ ] **Validators** (append to `SvcValidators.cs`):

```csharp
public class CreateSvcPackageRequestValidator : AbstractValidator<CreateSvcPackageRequest>
{
    public CreateSvcPackageRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Price).GreaterThanOrEqualTo(0m);
        RuleFor(x => x.ValidityDays).GreaterThan(0).When(x => x.ValidityDays.HasValue);
        RuleFor(x => x.Description).MaximumLength(1000).When(x => x.Description is not null);
    }
}
public class UpdateSvcPackageRequestValidator : AbstractValidator<UpdateSvcPackageRequest>
{
    public UpdateSvcPackageRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.ValidityDays).GreaterThan(0).When(x => x.ValidityDays.HasValue);
        RuleFor(x => x.Description).MaximumLength(1000).When(x => x.Description is not null);
    }
}
public class UpdateSvcPackagePriceRequestValidator : AbstractValidator<UpdateSvcPackagePriceRequest>
{
    public UpdateSvcPackagePriceRequestValidator() => RuleFor(x => x.Price).GreaterThanOrEqualTo(0m);
}
public class AddSvcPackageItemRequestValidator : AbstractValidator<AddSvcPackageItemRequest>
{
    public AddSvcPackageItemRequestValidator()
    {
        RuleFor(x => x.CatalogItemId).NotEmpty();
        RuleFor(x => x.IncludedQuantity).GreaterThan(0m);
    }
}
public class UpdateSvcPackageItemRequestValidator : AbstractValidator<UpdateSvcPackageItemRequest>
{
    public UpdateSvcPackageItemRequestValidator() => RuleFor(x => x.IncludedQuantity).GreaterThan(0m);
}
public class AssignSvcCustomerPackageRequestValidator : AbstractValidator<AssignSvcCustomerPackageRequest>
{
    public AssignSvcCustomerPackageRequestValidator()
    {
        RuleFor(x => x.PackageId).NotEmpty();
        RuleFor(x => x.CustomerId).NotEmpty();
        RuleFor(x => x.StartsAt).Must(d => d.Kind == DateTimeKind.Utc)
            .WithMessage("StartsAt must be UTC (use a trailing Z).");
        RuleFor(x => x.Notes).MaximumLength(2000).When(x => x.Notes is not null);
    }
}
public class ConsumeSvcPackageRequestValidator : AbstractValidator<ConsumeSvcPackageRequest>
{
    public ConsumeSvcPackageRequestValidator()
    {
        RuleFor(x => x.CatalogItemId).NotEmpty();
        RuleFor(x => x.Quantity).GreaterThan(0m);
        RuleFor(x => x).Must(r => r.OrderItemId is null || r.OrderId is not null)
            .WithMessage("OrderId is required when OrderItemId is provided.");
        RuleFor(x => x.Notes).MaximumLength(2000).When(x => x.Notes is not null);
    }
}
```

Build → commit `feat(service): package DTOs + validators (PR5)`.

---

## Task 6: `SvcPackageService` (templates + items) + `PackagesController` + DI

`SvcPackageService` (inject `ISvcPackageRepository`, `ISvcPackageItemRepository`, `ISvcCatalogItemRepository`, `ICurrentTenant`):

- `GetAllAsync(bool? active)` / `GetByIdAsync` (with items) — standard.
- `CreateAsync` → `SvcPackage.Create(...)`, Add, Save (Added — no Update).
- `UpdateAsync` (UpdateDetails) / `UpdatePriceAsync` / `ActivateAsync` / `DeactivateAsync` — load (GetById, tracked), mutate, Update, Save.
- `AddItemAsync(packageId, req)` → load package (404); load catalog (`_catalog.GetByIdAsync` → 404; `!IsActive` → 422); `if (await _items.ExistsForCatalogAsync(packageId, req.CatalogItemId)) throw new DomainException("This catalog item is already in the package.")` (422); `SvcPackageItem.Create(..., catalog.Name, req.IncludedQuantity)`; Add; Save.
- `UpdateItemAsync(packageId, itemId, req)` → load item (404, must belong to package); `item.UpdateQuantity`; Update; Save.
- `RemoveItemAsync(packageId, itemId)` → load item (404, belongs to package); Remove; Save.

`PackagesController` (`api/v1/service/packages`, `[Authorize][RequireServiceModule]`): GET(?active=)/POST/GET{id}/PUT/`PUT {id}/price`/activate/deactivate + `POST {id}/items`, `PUT {id}/items/{itemId}`, `DELETE {id}/items/{itemId}`. Register `SvcPackageService` in Application DI. Build → commit.

---

## Task 7: `SvcCustomerPackageService` (assign + cancel + consume + usages) + `CustomerPackagesController` + DI

`SvcCustomerPackageService` (inject `ISvcCustomerPackageRepository`, `ISvcCustomerPackageItemRepository`, `ISvcPackageUsageRepository`, `ISvcPackageRepository`, `ICustomerRepository`, `ISvcSubjectRepository`, `ISvcOrderRepository`, `ISvcOrderItemRepository`, `ICurrentTenant`):

```csharp
public async Task<SvcCustomerPackageDto> AssignAsync(AssignSvcCustomerPackageRequest r, CancellationToken ct = default)
{
    var package = await _packages.GetByIdWithItemsAsync(r.PackageId, ct)
        ?? throw new NotFoundException("SvcPackage", r.PackageId);
    if (!package.IsActive)          throw new DomainException("Package is not active.");
    if (package.Items.Count == 0)   throw new DomainException("Package has no items to assign.");

    _ = await _customers.GetByIdAsync(r.CustomerId, ct) ?? throw new NotFoundException(nameof(Customer), r.CustomerId);
    if (r.SubjectId is { } sid)
    {
        var subject = await _subjects.GetByIdAsync(sid, ct) ?? throw new NotFoundException("SvcSubject", sid);
        if (subject.CustomerId != r.CustomerId) throw new DomainException("Subject does not belong to the customer.");
    }

    var expiresAt = package.ValidityDays is { } days ? r.StartsAt.AddDays(days) : (DateTime?)null;
    var cp = SvcCustomerPackage.Create(
        _currentTenant.Id, GenerateCode(), package.Id, r.CustomerId, r.SubjectId,
        r.StartsAt, expiresAt, package.Price, r.Notes);
    await _customerPackages.AddAsync(cp, ct);

    var balances = package.Items.Select(pi => SvcCustomerPackageItem.Create(
        _currentTenant.Id, cp.Id, pi.CatalogItemId, pi.NameSnapshot, pi.IncludedQuantity)).ToList();
    foreach (var b in balances) await _customerPackageItems.AddAsync(b, ct);

    // cp + balances tracked as Added → INSERTed by SaveChanges. Do NOT call Update on them.
    await _customerPackages.SaveChangesAsync(ct);
    return MapToDto(cp, balances, usages: []);
}

public async Task<SvcCustomerPackageDto> CancelAsync(Guid id, CancellationToken ct = default)
{
    var cp = await _customerPackages.GetByIdWithItemsAsync(id, ct) ?? throw new NotFoundException("SvcCustomerPackage", id);
    cp.Cancel();
    _customerPackages.Update(cp);
    await _customerPackages.SaveChangesAsync(ct);
    return MapToDto(cp, cp.Items, await _usages.GetByCustomerPackageAsync(id, ct));
}

public async Task<SvcCustomerPackageDto> ConsumeAsync(Guid id, ConsumeSvcPackageRequest r, CancellationToken ct = default)
{
    var cp = await _customerPackages.GetByIdWithItemsAsync(id, ct) ?? throw new NotFoundException("SvcCustomerPackage", id);
    if (cp.Status != SvcCustomerPackageStatus.Active) throw new DomainException($"Cannot consume from a {cp.Status} package.");
    if (cp.IsExpiredAt(DateTime.UtcNow))              throw new DomainException("Package has expired.");

    var balance = cp.Items.FirstOrDefault(i => i.CatalogItemId == r.CatalogItemId)
        ?? throw new NotFoundException("Package balance for catalog item", r.CatalogItemId);

    await ValidateOrderLinkAsync(r.OrderId, r.OrderItemId, cp, ct);

    balance.Consume(r.Quantity);                        // 422 if insufficient / non-positive
    _customerPackageItems.Update(balance);

    var usage = SvcPackageUsage.Create(
        _currentTenant.Id, cp.Id, balance.Id, r.CatalogItemId, r.Quantity, r.OrderId, r.OrderItemId, r.Notes);
    await _usages.AddAsync(usage, ct);

    if (cp.Items.All(i => i.RemainingQuantity == 0m))
    {
        cp.MarkConsumed();
        _customerPackages.Update(cp);
    }

    await _customerPackages.SaveChangesAsync(ct);
    return MapToDto(cp, cp.Items, await _usages.GetByCustomerPackageAsync(id, ct));
}

private async Task ValidateOrderLinkAsync(Guid? orderId, Guid? orderItemId, SvcCustomerPackage cp, CancellationToken ct)
{
    if (orderId is not { } oid) return;                 // orderItemId-without-orderId rejected by the validator (400)
    var order = await _orders.GetByIdAsync(oid, ct) ?? throw new NotFoundException("SvcOrder", oid);
    if (order.CustomerId != cp.CustomerId)
        throw new DomainException("Order belongs to a different customer than the package.");
    if (cp.SubjectId is { } sid && order.SubjectId != sid)
        throw new DomainException("Order subject does not match the package subject.");
    if (orderItemId is { } oiid)
    {
        var item = await _orderItems.GetByIdAsync(oiid, ct) ?? throw new NotFoundException("SvcOrderItem", oiid);
        if (item.OrderId != oid) throw new DomainException("Order item does not belong to the order.");
    }
}

private static string GenerateCode() => $"PKG-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid():N}"[..19].ToUpperInvariant();
```

`GetUsagesAsync(id)` → `_usages.GetByCustomerPackageAsync`. `GetAllAsync(filters)` / `GetByIdAsync` (with items + usages).

`CustomerPackagesController` (`api/v1/service/customer-packages`, gated): GET(filters)/POST(assign)/GET{id}/`POST {id}/cancel`/`POST {id}/consume`/`GET {id}/usages`. Register `SvcCustomerPackageService` in Application DI. Build → commit `feat(service): customer-package assign + consume + usages (PR5)`.

---

## Task 8: Integration tests

**Files:** `ServicePackagesTests.cs` (templates) + `ServiceCustomerPackagesTests.cs` (assign/consume).

`ServicePackagesTests`: gate 403/200; package create/list/get/update; activate/deactivate; item add/update/delete; item with inactive catalog → 422; duplicate catalog item → 422; payload 400; cross-tenant 404.

`ServiceCustomerPackagesTests`: assign creates balances + computes ExpiresAt + PriceSnapshot from package; assign inactive package → 422; assign package with no items → 422; subject/customer mismatch → 422; unknown customer → 404; consume reduces balance + usage listed; consume insufficient → 422; consume catalog-not-in-balance → 404; consume cancelled → 422; consume expired → 422 (assign with a past StartsAt + ValidityDays so ExpiresAt < now); consume zeroing all balances → Status `Consumed`; consume linked to a valid order/order-item (and assert the order's `totalAmount` is unchanged); consume with order of a different customer → 422; consume with subject mismatch → 422; orderItemId-without-orderId → 400; usage list; cross-tenant 404; gate 403/200; payload 400.

Foreign-tenant seed mirrors PR4 (`SvcPackage.Create`/`SvcCustomerPackage.Create`, set `StoreId` via `db.Entry(x).Property("StoreId").CurrentValue`). Helpers reuse PR1–PR4 routes (customer, catalog, subject, order via `/api/v1/service/orders`). All `startsAt` are UTC (`DateTime.UtcNow`-based). The "consume expired" test assigns with `startsAt = DateTime.UtcNow.AddDays(-10)` against a package with `validityDays = 1` so `ExpiresAt` is in the past.

- [ ] **Step 1:** Run new tests, watch fail (endpoints missing).
- [ ] **Step 2:** After Tasks 1–7, run again → green.
- [ ] **Step 3: Commit** — `test(service): integration coverage for packages (PR5)`

---

## Task 9: Full verification + PR

- [ ] `dotnet build Nexo.sln` → 0 errors.
- [ ] `dotnet test tests/Nexo.UnitTests` → green.
- [ ] `dotnet test tests/Nexo.IntegrationTests` → green (incl. unchanged PR2–PR4 tests).
- [ ] Re-read migration `Up()` — only 5 `CreateTable` + indexes + new FKs.
- [ ] `git diff --name-only origin/master` — only Service files + migration. No Auth/Redis/Stripe/SuperAdmin/Build, no frontend, no `dist`. StorageController + RecordsController untouched.
- [ ] Push + `gh pr create --base master` (no merge).

---

## Self-review (spec coverage)

Entities `SvcPackage`/`SvcPackageItem`/`SvcCustomerPackage`/`SvcCustomerPackageItem`/`SvcPackageUsage` (T1) ✔ · status machine + terminal/Consumed/Cancel (T1) ✔ · package CRUD + items (T6) ✔ · assign creates balances + ExpiresAt + PriceSnapshot (T7) ✔ · consume reduces balance + auto-Consumed + history (T7) ✔ · order link without touching OS total/status (T7 `ValidateOrderLinkAsync`, asserted in T8) ✔ · expired-consume blocked, no auto-mutation (T7) ✔ · all reference/ownership validations (T7) ✔ · endpoints templates + customer-packages + consume + usages (T6/T7) ✔ · reversal **deferred** (documented) ✔ · additive migration (T2/T3) ✔ · tests (T8) ✔ · gate + isolation ✔.

## Risks
1. **Reversal deferred** — operational corrections to a wrong consumption aren't possible in PR5; documented, owner-approved-if-too-big.
2. **No auto-Expired mutation** — an expired package stays `Active` in reads but consume is blocked (422); a future job/PR flips the status.
3. **Two StoreEntity aggregates with `Include`** — both children carry the tenant+store filter; verified by integration tests that balances load and consume works.
4. **`Code` uniqueness probabilistic** (Guid-derived) — negligible for a single-store v1.
5. **Large additive migration (5 tables, many FKs)** — Postgres allows the multiple cascade paths (tenant Cascade + parent Cascade + business Restrict); verified by `has-pending-model-changes` + migration inspection.
