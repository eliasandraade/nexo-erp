# Orken Service PR3 — Appointments / Agenda Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add the real Service agenda — `SvcAppointment` with a status state machine, per-professional overlap prevention, a price snapshot, and the customer/professional/catalog/subject reference rules — backend-only, tenant/store-isolated, gated by the service-family module gate.

**Architecture:** `SvcAppointment` is a `StoreEntity` (per-store agenda) with **no `IsActive`** (it has `Status` instead) — so it reuses the PR2 `ConfigureStoreScopedSvcEntityNoActive` EF helper. The domain entity owns its invariants (time order, required ids, price ≥ 0) and the **status transition matrix**; the application service orchestrates the cross-entity rules (existence/active of customer/professional/catalog, subject-belongs-to-customer, `RequiresSubject`, overlap, price snapshot from the catalog). Migration is strictly additive (one `CreateTable` + indexes + new FKs to existing tables).

**Tech Stack:** .NET 8, EF Core 8 (Npgsql/PostgreSQL, `timestamptz`), FluentValidation, xUnit + FluentAssertions + Testcontainers.

---

## Design decisions (locked, grounded in PR1/PR2)

| Concern | Decision | Why / precedent |
|---|---|---|
| `SvcAppointment` base | `StoreEntity`, no `IsActive` | Per-store agenda; uses `Status`. Reuses `ConfigureStoreScopedSvcEntityNoActive` (PR2). |
| Status storage | `SvcAppointmentStatus` enum, `.HasConversion<string>()` | Matches PR2 enums; global `JsonStringEnumConverter` → invalid status string auto-400s on bind. |
| Status transitions | Domain state machine (`CanTransition` matrix); invalid → `DomainException` → **422** | Spec transition table. |
| Overlap | Service checks `HasOverlapAsync` (blockers: Scheduled/Confirmed/InProgress); conflict → `ConflictException` → **409** | 409 is the precise HTTP semantic for a scheduling conflict (`ConflictException` already maps to 409). |
| Missing customer/professional/catalog/subject (incl. cross-tenant, hidden by query filter) | `NotFoundException` → **404** | PR2 precedent. |
| Professional/catalog **inactive** | `DomainException` → **422** | Entity exists but unusable — a domain rule, not "not found". |
| `RequiresSubject` without subject; subject not belonging to customer | `DomainException` → **422** | Cross-entity rules resolved in the service (it holds the catalog/subject entities). |
| Edit a terminal appointment | `DomainException` → **422** | `IsTerminal` guard in `Reschedule` + fail-fast in service. |
| Bad payload (missing ids, `StartsAt ≥ EndsAt`, invalid status string) | FluentValidation / binding → **400** | Controller runs `ValidateAndThrowAsync` first (PR1/PR2). |
| `PriceSnapshot` | Copied from `SvcCatalogItem.Price` at create **and** reschedule | Spec: protect history from future catalog price changes. |
| `SvcSettings` | **Deferred** (documented) | Not technically needed in PR3: `EndsAt` is explicit in the payload (no `DefaultSlotMinutes` needed), and no business-hours validation is in the hard rules. Overlap is appointment-vs-appointment. Keeps PR3 focused (YAGNI). |
| Date kind | `StartsAt`/`EndsAt` must be **UTC** (`Kind=Utc`) | Npgsql `timestamptz` rejects non-UTC `DateTime`. Tests send `...Z` ISO strings; existing audit columns already use `DateTime.UtcNow`. Verify no `EnableLegacyTimestampBehavior` is relied on. |

## File structure

**Domain** (`nexo-backend/src/Nexo.Domain/Modules/Service/`)
- Create `SvcAppointmentStatus.cs` — `enum { Scheduled, Confirmed, InProgress, Completed, NoShow, Cancelled }`
- Create `SvcAppointment.cs` — `StoreEntity`; Create / Reschedule / ChangeStatus + `IsTerminal` + transition matrix

**Application** (`nexo-backend/src/Nexo.Application/Modules/Service/`)
- Create `SvcAppointmentDtos.cs` — `ISvcAppointmentFields`, `SvcAppointmentDto`, Create/Update/ChangeStatus requests
- Create `SvcAppointmentService.cs`
- Modify `SvcValidators.cs` — add `ApplyAppointmentRules` + validator classes
- Create `Interfaces/ISvcAppointmentRepository.cs`
- Modify `DependencyInjection.cs` — register `SvcAppointmentService`

**Infrastructure** (`nexo-backend/src/Nexo.Infrastructure/`)
- Create `Persistence/Configurations/Modules/Service/SvcAppointmentConfiguration.cs`
- Create `Repositories/Modules/Service/SvcAppointmentRepository.cs`
- Modify `DependencyInjection.cs` — register the repository
- Modify `Persistence/NexoDbContext.cs` — add `SvcAppointments` DbSet
- Create migration `<ts>_AddServiceAppointments.cs` (generated)

**API** (`nexo-backend/src/Nexo.Api/`)
- Create `Controllers/Modules/Service/AppointmentsController.cs`

**Tests** (`nexo-backend/tests/`)
- Create `Nexo.UnitTests/Service/SvcAppointmentTests.cs`
- Create `Nexo.IntegrationTests/Service/ServiceAppointmentsTests.cs`

---

## Task 1: Domain — status enum + `SvcAppointment` (unit tested)

**Files:**
- Create: `nexo-backend/src/Nexo.Domain/Modules/Service/SvcAppointmentStatus.cs`
- Create: `nexo-backend/src/Nexo.Domain/Modules/Service/SvcAppointment.cs`
- Test: `nexo-backend/tests/Nexo.UnitTests/Service/SvcAppointmentTests.cs`

- [ ] **Step 1: Write failing unit tests**

```csharp
using FluentAssertions;
using Nexo.Domain.Exceptions;
using Nexo.Domain.Modules.Service;
using Xunit;

namespace Nexo.UnitTests.Service;

public class SvcAppointmentTests
{
    private static readonly Guid T = Guid.NewGuid();
    private static readonly Guid Cust = Guid.NewGuid();
    private static readonly Guid Prof = Guid.NewGuid();
    private static readonly Guid Item = Guid.NewGuid();
    private static readonly DateTime Start = new(2026, 6, 18, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime End   = new(2026, 6, 18, 11, 0, 0, DateTimeKind.Utc);

    private static SvcAppointment New(Guid? subjectId = null)
        => SvcAppointment.Create(T, Cust, Prof, Item, subjectId, Start, End, 50m, "note");

    [Fact]
    public void Create_sets_fields_and_defaults_scheduled()
    {
        var a = New();
        a.CustomerId.Should().Be(Cust);
        a.ProfessionalId.Should().Be(Prof);
        a.CatalogItemId.Should().Be(Item);
        a.SubjectId.Should().BeNull();
        a.StartsAt.Should().Be(Start);
        a.EndsAt.Should().Be(End);
        a.PriceSnapshot.Should().Be(50m);
        a.Status.Should().Be(SvcAppointmentStatus.Scheduled);
        a.IsTerminal.Should().BeFalse();
    }

    [Fact]
    public void Create_with_starts_after_ends_throws()
    {
        var act = () => SvcAppointment.Create(T, Cust, Prof, Item, null, End, Start, 50m);
        act.Should().Throw<DomainException>().WithMessage("*StartsAt*EndsAt*");
    }

    [Theory]
    [InlineData(true, false, false)]   // empty customer
    [InlineData(false, true, false)]   // empty professional
    [InlineData(false, false, true)]   // empty catalog item
    public void Create_with_empty_required_ids_throws(bool noCust, bool noProf, bool noItem)
    {
        var act = () => SvcAppointment.Create(
            T, noCust ? Guid.Empty : Cust, noProf ? Guid.Empty : Prof,
            noItem ? Guid.Empty : Item, null, Start, End, 50m);
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Create_with_negative_price_throws()
        => ((Action)(() => SvcAppointment.Create(T, Cust, Prof, Item, null, Start, End, -1m)))
            .Should().Throw<DomainException>();

    [Theory]
    [InlineData(SvcAppointmentStatus.Confirmed)]
    [InlineData(SvcAppointmentStatus.Cancelled)]
    [InlineData(SvcAppointmentStatus.NoShow)]
    public void Scheduled_allows(SvcAppointmentStatus to)
    {
        var a = New();
        a.ChangeStatus(to, "r");
        a.Status.Should().Be(to);
    }

    [Fact]
    public void Confirmed_to_in_progress_to_completed_is_allowed()
    {
        var a = New();
        a.ChangeStatus(SvcAppointmentStatus.Confirmed, null);
        a.ChangeStatus(SvcAppointmentStatus.InProgress, null);
        a.ChangeStatus(SvcAppointmentStatus.Completed, null);
        a.Status.Should().Be(SvcAppointmentStatus.Completed);
        a.IsTerminal.Should().BeTrue();
    }

    [Fact]
    public void Cancel_records_reason()
    {
        var a = New();
        a.ChangeStatus(SvcAppointmentStatus.Cancelled, "client asked");
        a.CancellationReason.Should().Be("client asked");
    }

    [Theory]
    [InlineData(SvcAppointmentStatus.InProgress)]  // Scheduled cannot jump to InProgress
    [InlineData(SvcAppointmentStatus.Completed)]   // Scheduled cannot jump to Completed
    public void Invalid_transition_from_scheduled_throws(SvcAppointmentStatus to)
        => ((Action)(() => New().ChangeStatus(to, null)))
            .Should().Throw<DomainException>().WithMessage("*status*");

    [Fact]
    public void Transition_from_terminal_throws()
    {
        var a = New();
        a.ChangeStatus(SvcAppointmentStatus.Cancelled, "x");
        ((Action)(() => a.ChangeStatus(SvcAppointmentStatus.Confirmed, null)))
            .Should().Throw<DomainException>();
    }

    [Fact]
    public void Reschedule_updates_fields_when_not_terminal()
    {
        var a = New();
        var ns = new DateTime(2026, 6, 19, 9, 0, 0, DateTimeKind.Utc);
        var ne = new DateTime(2026, 6, 19, 9, 30, 0, DateTimeKind.Utc);
        a.Reschedule(Cust, Prof, Item, null, ns, ne, 80m, "moved");
        a.StartsAt.Should().Be(ns);
        a.EndsAt.Should().Be(ne);
        a.PriceSnapshot.Should().Be(80m);
    }

    [Fact]
    public void Reschedule_terminal_throws()
    {
        var a = New();
        a.ChangeStatus(SvcAppointmentStatus.Completed.ToScheduledPath(), null); // see helper note
        // Drive to Completed properly:
    }
}
```

> Note: replace the last test body with the explicit drive-to-completed below (the `ToScheduledPath` placeholder is illustrative only — do not ship it):

```csharp
    [Fact]
    public void Reschedule_terminal_throws()
    {
        var a = New();
        a.ChangeStatus(SvcAppointmentStatus.Confirmed, null);
        a.ChangeStatus(SvcAppointmentStatus.InProgress, null);
        a.ChangeStatus(SvcAppointmentStatus.Completed, null);
        var act = () => a.Reschedule(Cust, Prof, Item, null, Start, End, 50m, null);
        act.Should().Throw<DomainException>().WithMessage("*Completed*");
    }
```

- [ ] **Step 2: Run, verify it fails** — `dotnet test tests/Nexo.UnitTests --filter SvcAppointmentTests` → FAIL (types missing).

- [ ] **Step 3: Implement the enum**

```csharp
namespace Nexo.Domain.Modules.Service;

/// <summary>
/// Lifecycle status of a <see cref="SvcAppointment"/>. Stored as a string. Transitions are
/// enforced by the entity's state machine; Completed/Cancelled/NoShow are terminal.
/// </summary>
public enum SvcAppointmentStatus
{
    Scheduled,
    Confirmed,
    InProgress,
    Completed,
    NoShow,
    Cancelled,
}
```

- [ ] **Step 4: Implement `SvcAppointment`**

```csharp
using Nexo.Domain.Common;
using Nexo.Domain.Exceptions;

namespace Nexo.Domain.Modules.Service;

/// <summary>
/// A booking in the Orken Service agenda — store-scoped. Links a customer, a professional, a
/// catalog item, and (optionally) a subject (pet/veículo/aluno). <see cref="PriceSnapshot"/>
/// copies the catalog price at booking/reschedule time so later catalog price changes never
/// rewrite history. The status state machine (Scheduled→Confirmed→InProgress→Completed, with
/// Cancelled/NoShow exits) is enforced here; cross-entity rules (active professional/catalog,
/// subject ownership, overlap) live in the application service.
/// </summary>
public class SvcAppointment : StoreEntity
{
    private SvcAppointment() { }                                   // EF Core
    private SvcAppointment(Guid tenantId) : base(tenantId) { }

    public Guid                 CustomerId         { get; private set; }
    public Guid                 ProfessionalId     { get; private set; }
    public Guid                 CatalogItemId      { get; private set; }
    public Guid?                SubjectId          { get; private set; }
    public DateTime             StartsAt           { get; private set; }
    public DateTime             EndsAt             { get; private set; }
    public SvcAppointmentStatus Status             { get; private set; }
    public string?              Notes              { get; private set; }
    public string?              CancellationReason { get; private set; }
    public decimal              PriceSnapshot      { get; private set; }

    public bool IsTerminal => Status is SvcAppointmentStatus.Completed
                                     or SvcAppointmentStatus.Cancelled
                                     or SvcAppointmentStatus.NoShow;

    public static SvcAppointment Create(
        Guid tenantId, Guid customerId, Guid professionalId, Guid catalogItemId,
        Guid? subjectId, DateTime startsAt, DateTime endsAt, decimal priceSnapshot, string? notes = null)
    {
        EnsureValid(customerId, professionalId, catalogItemId, startsAt, endsAt, priceSnapshot);
        return new SvcAppointment(tenantId)
        {
            CustomerId     = customerId,
            ProfessionalId = professionalId,
            CatalogItemId  = catalogItemId,
            SubjectId      = subjectId,
            StartsAt       = startsAt,
            EndsAt         = endsAt,
            PriceSnapshot  = priceSnapshot,
            Notes          = notes?.Trim(),
            Status         = SvcAppointmentStatus.Scheduled,
        };
    }

    public void Reschedule(
        Guid customerId, Guid professionalId, Guid catalogItemId,
        Guid? subjectId, DateTime startsAt, DateTime endsAt, decimal priceSnapshot, string? notes)
    {
        if (IsTerminal)
            throw new DomainException($"Cannot edit a {Status} appointment.");
        EnsureValid(customerId, professionalId, catalogItemId, startsAt, endsAt, priceSnapshot);

        CustomerId     = customerId;
        ProfessionalId = professionalId;
        CatalogItemId  = catalogItemId;
        SubjectId      = subjectId;
        StartsAt       = startsAt;
        EndsAt         = endsAt;
        PriceSnapshot  = priceSnapshot;
        Notes          = notes?.Trim();
        SetUpdatedAt();
    }

    public void ChangeStatus(SvcAppointmentStatus target, string? reason)
    {
        if (!CanTransition(Status, target))
            throw new DomainException($"Cannot change appointment status from {Status} to {target}.");

        Status = target;
        if (target == SvcAppointmentStatus.Cancelled)
            CancellationReason = reason?.Trim();
        SetUpdatedAt();
    }

    private static void EnsureValid(
        Guid customerId, Guid professionalId, Guid catalogItemId,
        DateTime startsAt, DateTime endsAt, decimal priceSnapshot)
    {
        if (customerId == Guid.Empty)     throw new DomainException("Customer is required.");
        if (professionalId == Guid.Empty) throw new DomainException("Professional is required.");
        if (catalogItemId == Guid.Empty)  throw new DomainException("Catalog item is required.");
        if (startsAt >= endsAt)           throw new DomainException("StartsAt must be before EndsAt.");
        if (priceSnapshot < 0m)           throw new DomainException("Price snapshot cannot be negative.");
    }

    private static bool CanTransition(SvcAppointmentStatus from, SvcAppointmentStatus to) => (from, to) switch
    {
        (SvcAppointmentStatus.Scheduled,  SvcAppointmentStatus.Confirmed)  => true,
        (SvcAppointmentStatus.Scheduled,  SvcAppointmentStatus.Cancelled)  => true,
        (SvcAppointmentStatus.Scheduled,  SvcAppointmentStatus.NoShow)     => true,
        (SvcAppointmentStatus.Confirmed,  SvcAppointmentStatus.InProgress) => true,
        (SvcAppointmentStatus.Confirmed,  SvcAppointmentStatus.Cancelled)  => true,
        (SvcAppointmentStatus.Confirmed,  SvcAppointmentStatus.NoShow)     => true,
        (SvcAppointmentStatus.InProgress, SvcAppointmentStatus.Completed)  => true,
        (SvcAppointmentStatus.InProgress, SvcAppointmentStatus.Cancelled)  => true,
        _ => false,
    };
}
```

- [ ] **Step 5: Run, verify pass** — `dotnet test tests/Nexo.UnitTests --filter SvcAppointmentTests` → PASS.
- [ ] **Step 6: Commit** — `feat(service): SvcAppointment domain entity + status machine (PR3)`

---

## Task 2: EF configuration + DbSet (model verified)

**Files:**
- Create: `nexo-backend/src/Nexo.Infrastructure/Persistence/Configurations/Modules/Service/SvcAppointmentConfiguration.cs`
- Modify: `nexo-backend/src/Nexo.Infrastructure/Persistence/NexoDbContext.cs`

- [ ] **Step 1: Create `SvcAppointmentConfiguration.cs`** (reuses the PR2 store-scoped-no-active helper)

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nexo.Domain.Entities;
using Nexo.Domain.Modules.Service;

namespace Nexo.Infrastructure.Persistence.Configurations.Modules.Service;

public class SvcAppointmentConfiguration : IEntityTypeConfiguration<SvcAppointment>
{
    public void Configure(EntityTypeBuilder<SvcAppointment> builder)
    {
        // Key, tenant/store columns + FKs, audit columns (no is_active — appointments use Status).
        builder.ConfigureStoreScopedSvcEntityNoActive("svc_appointments");

        builder.Property(x => x.CustomerId).HasColumnName("customer_id").IsRequired();
        builder.Property(x => x.ProfessionalId).HasColumnName("professional_id").IsRequired();
        builder.Property(x => x.CatalogItemId).HasColumnName("catalog_item_id").IsRequired();
        builder.Property(x => x.SubjectId).HasColumnName("subject_id");
        builder.Property(x => x.StartsAt).HasColumnName("starts_at").HasColumnType("timestamptz").IsRequired();
        builder.Property(x => x.EndsAt).HasColumnName("ends_at").HasColumnType("timestamptz").IsRequired();
        builder.Property(x => x.Status)
            .HasColumnName("status").HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.Notes).HasColumnName("notes").HasMaxLength(2000);
        builder.Property(x => x.CancellationReason).HasColumnName("cancellation_reason").HasMaxLength(500);
        builder.Property(x => x.PriceSnapshot)
            .HasColumnName("price_snapshot").HasColumnType("numeric(18,2)").IsRequired();

        builder.HasOne<Customer>().WithMany().HasForeignKey(x => x.CustomerId)
            .HasConstraintName("fk_svc_appointments_customers").OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<SvcProfessional>().WithMany().HasForeignKey(x => x.ProfessionalId)
            .HasConstraintName("fk_svc_appointments_professionals").OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<SvcCatalogItem>().WithMany().HasForeignKey(x => x.CatalogItemId)
            .HasConstraintName("fk_svc_appointments_catalog_items").OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<SvcSubject>().WithMany().HasForeignKey(x => x.SubjectId)
            .HasConstraintName("fk_svc_appointments_subjects").OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex("TenantId", "StoreId", "ProfessionalId", "StartsAt")
            .HasDatabaseName("ix_svc_appointments_professional_starts");
        builder.HasIndex(x => x.CustomerId).HasDatabaseName("ix_svc_appointments_customer_id");
    }
}
```

- [ ] **Step 2: Add DbSet to `NexoDbContext.cs`** — after `SvcRecordEntries`:

```csharp
        public DbSet<SvcAppointment>      SvcAppointments  => Set<SvcAppointment>();
```

- [ ] **Step 3: Build** — `dotnet build Nexo.sln` → success.
- [ ] **Step 4: Commit** — `feat(service): EF config + DbSet for SvcAppointment (PR3)`

---

## Task 3: Additive migration

**Files:**
- Create (generated): `.../Migrations/<timestamp>_AddServiceAppointments.cs` (+ Designer)

- [ ] **Step 1: Generate** — `cd nexo-backend && dotnet ef migrations add AddServiceAppointments -p src/Nexo.Infrastructure -s src/Nexo.Api -o Persistence/Migrations`
- [ ] **Step 2: PROVE additive.** `Up()` must contain ONLY: `CreateTable("svc_appointments", …)` (with its FK constraints to tenants/stores/customers/svc_professionals/svc_catalog_items/svc_subjects) + `CreateIndex(…)` for that table. It MUST NOT contain `DropTable`, `DropColumn`, `AlterColumn`, `RenameColumn`, or any operation on a pre-existing table. If anything else appears, STOP and report.
- [ ] **Step 3: Confirm clean** — `dotnet ef migrations has-pending-model-changes -p src/Nexo.Infrastructure -s src/Nexo.Api` → "No changes…".
- [ ] **Step 4: Commit** — `feat(service): additive migration AddServiceAppointments (PR3)`

---

## Task 4: Repository + interface + DI

**Files:**
- Create: `nexo-backend/src/Nexo.Application/Modules/Service/Interfaces/ISvcAppointmentRepository.cs`
- Create: `nexo-backend/src/Nexo.Infrastructure/Repositories/Modules/Service/SvcAppointmentRepository.cs`
- Modify: `nexo-backend/src/Nexo.Infrastructure/DependencyInjection.cs`

- [ ] **Step 1: `ISvcAppointmentRepository.cs`**

```csharp
using Nexo.Domain.Modules.Service;

namespace Nexo.Application.Modules.Service.Interfaces;

/// <summary>Repository for SvcAppointment. Tenant + store isolation enforced by the EF global query filter.</summary>
public interface ISvcAppointmentRepository
{
    Task<SvcAppointment?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<SvcAppointment>> GetAllAsync(
        DateTime? from, DateTime? to, Guid? professionalId, SvcAppointmentStatus? status,
        Guid? customerId, Guid? subjectId, CancellationToken ct = default);
    /// <summary>True if the professional has a blocking appointment (Scheduled/Confirmed/InProgress)
    /// overlapping [startsAt, endsAt). <paramref name="excludeId"/> skips the row being rescheduled.</summary>
    Task<bool> HasOverlapAsync(
        Guid professionalId, DateTime startsAt, DateTime endsAt, Guid? excludeId, CancellationToken ct = default);
    Task AddAsync(SvcAppointment entity, CancellationToken ct = default);
    void Update(SvcAppointment entity);
    Task SaveChangesAsync(CancellationToken ct = default);
}
```

- [ ] **Step 2: `SvcAppointmentRepository.cs`**

```csharp
using Microsoft.EntityFrameworkCore;
using Nexo.Application.Modules.Service.Interfaces;
using Nexo.Domain.Modules.Service;
using Nexo.Infrastructure.Persistence;

namespace Nexo.Infrastructure.Repositories.Modules.Service;

public class SvcAppointmentRepository : ISvcAppointmentRepository
{
    private readonly NexoDbContext _context;

    public SvcAppointmentRepository(NexoDbContext context) => _context = context;

    public async Task<SvcAppointment?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await _context.SvcAppointments.FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<IReadOnlyList<SvcAppointment>> GetAllAsync(
        DateTime? from, DateTime? to, Guid? professionalId, SvcAppointmentStatus? status,
        Guid? customerId, Guid? subjectId, CancellationToken ct = default)
    {
        var q = _context.SvcAppointments.AsQueryable();
        if (from is { } f)           q = q.Where(a => a.StartsAt >= f);
        if (to is { } t)             q = q.Where(a => a.StartsAt <= t);
        if (professionalId is { } p) q = q.Where(a => a.ProfessionalId == p);
        if (status is { } s)         q = q.Where(a => a.Status == s);
        if (customerId is { } c)     q = q.Where(a => a.CustomerId == c);
        if (subjectId is { } sub)    q = q.Where(a => a.SubjectId == sub);
        return await q.OrderBy(a => a.StartsAt).ToListAsync(ct);
    }

    public async Task<bool> HasOverlapAsync(
        Guid professionalId, DateTime startsAt, DateTime endsAt, Guid? excludeId, CancellationToken ct = default)
        => await _context.SvcAppointments.AnyAsync(a =>
            a.ProfessionalId == professionalId &&
            (a.Status == SvcAppointmentStatus.Scheduled ||
             a.Status == SvcAppointmentStatus.Confirmed ||
             a.Status == SvcAppointmentStatus.InProgress) &&
            a.StartsAt < endsAt && a.EndsAt > startsAt &&
            (excludeId == null || a.Id != excludeId), ct);

    public async Task AddAsync(SvcAppointment entity, CancellationToken ct = default)
        => await _context.SvcAppointments.AddAsync(entity, ct);

    public void Update(SvcAppointment entity) => _context.SvcAppointments.Update(entity);

    public async Task SaveChangesAsync(CancellationToken ct = default)
        => await _context.SaveChangesAsync(ct);
}
```

- [ ] **Step 3: Register in Infrastructure `DependencyInjection.cs`** — under the Service repositories block:

```csharp
        services.AddScoped<ISvcAppointmentRepository, SvcAppointmentRepository>();
```

- [ ] **Step 4: Build → success. Commit** — `feat(service): SvcAppointment repository + DI (PR3)`

---

## Task 5: DTOs + validators

**Files:**
- Create: `nexo-backend/src/Nexo.Application/Modules/Service/SvcAppointmentDtos.cs`
- Modify: `nexo-backend/src/Nexo.Application/Modules/Service/SvcValidators.cs`

- [ ] **Step 1: `SvcAppointmentDtos.cs`**

```csharp
using Nexo.Domain.Modules.Service;

namespace Nexo.Application.Modules.Service;

/// <summary>Editable appointment fields shared by create + update, validated by one rule set.</summary>
public interface ISvcAppointmentFields
{
    Guid     CustomerId { get; }
    Guid     ProfessionalId { get; }
    Guid     CatalogItemId { get; }
    Guid?    SubjectId { get; }
    DateTime StartsAt { get; }
    DateTime EndsAt { get; }
    string?  Notes { get; }
}

public sealed record SvcAppointmentDto(
    Guid                 Id,
    Guid                 StoreId,
    Guid                 CustomerId,
    Guid                 ProfessionalId,
    Guid                 CatalogItemId,
    Guid?                SubjectId,
    DateTime             StartsAt,
    DateTime             EndsAt,
    SvcAppointmentStatus Status,
    string?              Notes,
    string?              CancellationReason,
    decimal              PriceSnapshot,
    DateTime             CreatedAt,
    DateTime             UpdatedAt);

public sealed record CreateSvcAppointmentRequest(
    Guid     CustomerId,
    Guid     ProfessionalId,
    Guid     CatalogItemId,
    DateTime StartsAt,
    DateTime EndsAt,
    Guid?    SubjectId = null,
    string?  Notes     = null) : ISvcAppointmentFields;

public sealed record UpdateSvcAppointmentRequest(
    Guid     CustomerId,
    Guid     ProfessionalId,
    Guid     CatalogItemId,
    DateTime StartsAt,
    DateTime EndsAt,
    Guid?    SubjectId = null,
    string?  Notes     = null) : ISvcAppointmentFields;

public sealed record ChangeSvcAppointmentStatusRequest(
    SvcAppointmentStatus? Status,
    string?               Reason = null);
```

- [ ] **Step 2: Add to `SvcValidators.cs`** (inside `SvcValidationRules`, plus three validator classes). `SvcAppointmentStatus`/`ISvcAppointmentFields` are already reachable (`using Nexo.Domain.Modules.Service;` is present after PR2).

```csharp
    public static void ApplyAppointmentRules<T>(AbstractValidator<T> v) where T : ISvcAppointmentFields
    {
        v.RuleFor(x => x.CustomerId).NotEmpty().WithMessage("CustomerId is required.");
        v.RuleFor(x => x.ProfessionalId).NotEmpty().WithMessage("ProfessionalId is required.");
        v.RuleFor(x => x.CatalogItemId).NotEmpty().WithMessage("CatalogItemId is required.");
        v.RuleFor(x => x).Must(r => r.StartsAt < r.EndsAt)
            .WithMessage("StartsAt must be before EndsAt.");
        v.RuleFor(x => x.Notes).MaximumLength(2000).When(x => x.Notes is not null);
    }
```

```csharp
public class CreateSvcAppointmentRequestValidator : AbstractValidator<CreateSvcAppointmentRequest>
{
    public CreateSvcAppointmentRequestValidator() => SvcValidationRules.ApplyAppointmentRules(this);
}

public class UpdateSvcAppointmentRequestValidator : AbstractValidator<UpdateSvcAppointmentRequest>
{
    public UpdateSvcAppointmentRequestValidator() => SvcValidationRules.ApplyAppointmentRules(this);
}

public class ChangeSvcAppointmentStatusRequestValidator : AbstractValidator<ChangeSvcAppointmentStatusRequest>
{
    public ChangeSvcAppointmentStatusRequestValidator()
    {
        RuleFor(x => x.Status).NotNull().WithMessage("Status is required.")
            .IsInEnum().WithMessage("Invalid status.");
        RuleFor(x => x.Reason).MaximumLength(500).When(x => x.Reason is not null);
    }
}
```

- [ ] **Step 3: Build → success. Commit** — `feat(service): appointment DTOs + validators (PR3)`

---

## Task 6: Service + controller + DI

**Files:**
- Create: `nexo-backend/src/Nexo.Application/Modules/Service/SvcAppointmentService.cs`
- Modify: `nexo-backend/src/Nexo.Application/DependencyInjection.cs`
- Create: `nexo-backend/src/Nexo.Api/Controllers/Modules/Service/AppointmentsController.cs`

- [ ] **Step 1: `SvcAppointmentService.cs`**

```csharp
using Nexo.Application.Common.Interfaces;
using Nexo.Application.Modules.Service.Interfaces;
using Nexo.Domain.Entities;
using Nexo.Domain.Exceptions;
using Nexo.Domain.Modules.Service;

namespace Nexo.Application.Modules.Service;

/// <summary>
/// Use cases for the SvcAppointment aggregate. Resolves and validates the referenced customer,
/// professional, catalog item, and (optional) subject through tenant/store-filtered repositories
/// (cross-tenant rows are invisible → 404). Enforces active professional/catalog (422),
/// RequiresSubject + subject-belongs-to-customer (422), and per-professional overlap (409). The
/// price is snapshotted from the catalog at create/reschedule time.
/// </summary>
public class SvcAppointmentService
{
    private readonly ISvcAppointmentRepository _repo;
    private readonly ICustomerRepository       _customers;
    private readonly ISvcProfessionalRepository _professionals;
    private readonly ISvcCatalogItemRepository  _catalog;
    private readonly ISvcSubjectRepository      _subjects;
    private readonly ICurrentTenant             _currentTenant;

    public SvcAppointmentService(
        ISvcAppointmentRepository repo,
        ICustomerRepository customers,
        ISvcProfessionalRepository professionals,
        ISvcCatalogItemRepository catalog,
        ISvcSubjectRepository subjects,
        ICurrentTenant currentTenant)
    {
        _repo          = repo;
        _customers     = customers;
        _professionals = professionals;
        _catalog       = catalog;
        _subjects      = subjects;
        _currentTenant = currentTenant;
    }

    public async Task<IReadOnlyList<SvcAppointmentDto>> GetAllAsync(
        DateTime? from, DateTime? to, Guid? professionalId, SvcAppointmentStatus? status,
        Guid? customerId, Guid? subjectId, CancellationToken ct = default)
        => (await _repo.GetAllAsync(from, to, professionalId, status, customerId, subjectId, ct))
            .Select(MapToDto).ToList();

    public async Task<SvcAppointmentDto> GetByIdAsync(Guid id, CancellationToken ct = default)
        => MapToDto(await _repo.GetByIdAsync(id, ct) ?? throw new NotFoundException("SvcAppointment", id));

    public async Task<SvcAppointmentDto> CreateAsync(CreateSvcAppointmentRequest r, CancellationToken ct = default)
    {
        var price = await ResolveAndValidateRefsAsync(r.CustomerId, r.ProfessionalId, r.CatalogItemId, r.SubjectId, ct);
        await EnsureNoOverlapAsync(r.ProfessionalId, r.StartsAt, r.EndsAt, null, ct);

        var appt = SvcAppointment.Create(
            _currentTenant.Id, r.CustomerId, r.ProfessionalId, r.CatalogItemId,
            r.SubjectId, r.StartsAt, r.EndsAt, price, r.Notes);

        await _repo.AddAsync(appt, ct);
        await _repo.SaveChangesAsync(ct);
        return MapToDto(appt);
    }

    public async Task<SvcAppointmentDto> UpdateAsync(Guid id, UpdateSvcAppointmentRequest r, CancellationToken ct = default)
    {
        var appt = await _repo.GetByIdAsync(id, ct) ?? throw new NotFoundException("SvcAppointment", id);
        if (appt.IsTerminal)
            throw new DomainException($"Cannot edit a {appt.Status} appointment.");

        var price = await ResolveAndValidateRefsAsync(r.CustomerId, r.ProfessionalId, r.CatalogItemId, r.SubjectId, ct);
        await EnsureNoOverlapAsync(r.ProfessionalId, r.StartsAt, r.EndsAt, id, ct);

        appt.Reschedule(r.CustomerId, r.ProfessionalId, r.CatalogItemId, r.SubjectId, r.StartsAt, r.EndsAt, price, r.Notes);
        _repo.Update(appt);
        await _repo.SaveChangesAsync(ct);
        return MapToDto(appt);
    }

    public async Task<SvcAppointmentDto> ChangeStatusAsync(
        Guid id, ChangeSvcAppointmentStatusRequest r, CancellationToken ct = default)
    {
        var appt = await _repo.GetByIdAsync(id, ct) ?? throw new NotFoundException("SvcAppointment", id);
        appt.ChangeStatus(r.Status!.Value, r.Reason);   // Status is NotNull-validated upstream
        _repo.Update(appt);
        await _repo.SaveChangesAsync(ct);
        return MapToDto(appt);
    }

    private async Task<decimal> ResolveAndValidateRefsAsync(
        Guid customerId, Guid professionalId, Guid catalogItemId, Guid? subjectId, CancellationToken ct)
    {
        _ = await _customers.GetByIdAsync(customerId, ct)
            ?? throw new NotFoundException(nameof(Customer), customerId);

        var professional = await _professionals.GetByIdAsync(professionalId, ct)
            ?? throw new NotFoundException("SvcProfessional", professionalId);
        if (!professional.IsActive) throw new DomainException("Professional is not active.");

        var catalog = await _catalog.GetByIdAsync(catalogItemId, ct)
            ?? throw new NotFoundException("SvcCatalogItem", catalogItemId);
        if (!catalog.IsActive) throw new DomainException("Catalog item is not active.");

        if (catalog.RequiresSubject && subjectId is null)
            throw new DomainException("This service requires a subject.");

        if (subjectId is { } sid)
        {
            var subject = await _subjects.GetByIdAsync(sid, ct)
                ?? throw new NotFoundException("SvcSubject", sid);
            if (subject.CustomerId != customerId)
                throw new DomainException("Subject does not belong to the customer.");
        }

        return catalog.Price;
    }

    private async Task EnsureNoOverlapAsync(
        Guid professionalId, DateTime startsAt, DateTime endsAt, Guid? excludeId, CancellationToken ct)
    {
        if (await _repo.HasOverlapAsync(professionalId, startsAt, endsAt, excludeId, ct))
            throw new ConflictException("The professional already has an appointment in this time range.");
    }

    internal static SvcAppointmentDto MapToDto(SvcAppointment a) => new(
        Id:                 a.Id,
        StoreId:            a.StoreId,
        CustomerId:         a.CustomerId,
        ProfessionalId:     a.ProfessionalId,
        CatalogItemId:      a.CatalogItemId,
        SubjectId:          a.SubjectId,
        StartsAt:           a.StartsAt,
        EndsAt:             a.EndsAt,
        Status:             a.Status,
        Notes:              a.Notes,
        CancellationReason: a.CancellationReason,
        PriceSnapshot:      a.PriceSnapshot,
        CreatedAt:          a.CreatedAt,
        UpdatedAt:          a.UpdatedAt);
}
```

- [ ] **Step 2: Register in Application `DependencyInjection.cs`** — under the Service block:

```csharp
        services.AddScoped<SvcAppointmentService>();
```

- [ ] **Step 3: `AppointmentsController.cs`**

```csharp
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nexo.Api.Attributes;
using Nexo.Application.Modules.Service;
using Nexo.Domain.Modules.Service;

namespace Nexo.Api.Controllers.Modules.Service;

/// <summary>
/// Service agenda (ORKEN SERVICE) — appointments. Store-scoped. All endpoints require an active
/// service-family subscription. Per-professional overlap is rejected with 409; invalid status
/// transitions with 422.
/// </summary>
[ApiController]
[Route("api/v1/service/appointments")]
[Authorize]
[RequireServiceModule]
public class AppointmentsController : ControllerBase
{
    private readonly SvcAppointmentService                         _service;
    private readonly IValidator<CreateSvcAppointmentRequest>       _createValidator;
    private readonly IValidator<UpdateSvcAppointmentRequest>       _updateValidator;
    private readonly IValidator<ChangeSvcAppointmentStatusRequest> _statusValidator;

    public AppointmentsController(
        SvcAppointmentService                         service,
        IValidator<CreateSvcAppointmentRequest>       createValidator,
        IValidator<UpdateSvcAppointmentRequest>       updateValidator,
        IValidator<ChangeSvcAppointmentStatusRequest> statusValidator)
    {
        _service         = service;
        _createValidator = createValidator;
        _updateValidator = updateValidator;
        _statusValidator = statusValidator;
    }

    /// <summary>Lists appointments, filterable by date range, professional, status, customer, subject.</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<SvcAppointmentDto>>> GetAll(
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] Guid? professionalId,
        [FromQuery] SvcAppointmentStatus? status,
        [FromQuery] Guid? customerId,
        [FromQuery] Guid? subjectId,
        CancellationToken ct = default)
        => Ok(await _service.GetAllAsync(from, to, professionalId, status, customerId, subjectId, ct));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<SvcAppointmentDto>> GetById(Guid id, CancellationToken ct)
        => Ok(await _service.GetByIdAsync(id, ct));

    [HttpPost]
    public async Task<ActionResult<SvcAppointmentDto>> Create(
        [FromBody] CreateSvcAppointmentRequest request, CancellationToken ct)
    {
        await _createValidator.ValidateAndThrowAsync(request, ct);
        var dto = await _service.CreateAsync(request, ct);
        return CreatedAtAction(nameof(GetById), new { id = dto.Id }, dto);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<SvcAppointmentDto>> Update(
        Guid id, [FromBody] UpdateSvcAppointmentRequest request, CancellationToken ct)
    {
        await _updateValidator.ValidateAndThrowAsync(request, ct);
        return Ok(await _service.UpdateAsync(id, request, ct));
    }

    [HttpPatch("{id:guid}/status")]
    public async Task<ActionResult<SvcAppointmentDto>> ChangeStatus(
        Guid id, [FromBody] ChangeSvcAppointmentStatusRequest request, CancellationToken ct)
    {
        await _statusValidator.ValidateAndThrowAsync(request, ct);
        return Ok(await _service.ChangeStatusAsync(id, request, ct));
    }
}
```

- [ ] **Step 4: Build → success. Commit** — `feat(service): SvcAppointmentService + AppointmentsController (PR3)`

---

## Task 7: Integration tests (write first against the slice, watch fail, then green)

**File:** Create `nexo-backend/tests/Nexo.IntegrationTests/Service/ServiceAppointmentsTests.cs`

Covers: gate 403/200, create→get→list, status transitions valid + invalid (422), overlap rejected (409), cancel releases slot, no-show terminal, completed terminal, reschedule, terminal-edit rejected (422), inactive professional (422), inactive catalog (422), `RequiresSubject` enforced (422), subject/customer mismatch (422), invalid payload (400), cross-tenant not accessible (404).

Helpers (via API, reusing PR1/PR2 routes): create customer (`POST /api/customers`), professional (`POST /api/v1/service/professionals`), catalog (`POST /api/v1/service/catalog`, with `requiresSubject`), subject (`POST /api/v1/service/subjects`); deactivate via the `/{id}/deactivate` endpoints. **All `StartsAt`/`EndsAt` are UTC** (`DateTime.UtcNow`-based) so Npgsql accepts them on the `timestamptz` columns.

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nexo.Domain.Entities;
using Nexo.Domain.Enums;
using Nexo.Domain.Modules.Service;
using Nexo.Infrastructure.Persistence;
using Nexo.IntegrationTests.Common;
using Nexo.IntegrationTests.Helpers;
using Xunit;

namespace Nexo.IntegrationTests.Service;

[Collection("Integration")]
public class ServiceAppointmentsTests
{
    private const string ForeignTaxId = "77666555000203";
    private static readonly DateTime Base = DateTime.UtcNow.Date.AddDays(3).AddHours(10); // tomorrow-ish, UTC

    private readonly TestWebApplicationFactory _factory;
    public ServiceAppointmentsTests(TestWebApplicationFactory factory) => _factory = factory;

    // ── Gate ─────────────────────────────────────────────────────────────────
    [Fact]
    public async Task Appointments_forbidden_without_service_module()
    {
        var client = await AuthClientFactory.LoginAsync(_factory, "clara.boutique", "boutique@123");
        (await client.GetAsync("/api/v1/service/appointments")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Appointments_accessible_with_service_module()
    {
        var client = await AuthClientFactory.LoginAsAdminAsync(_factory);
        (await client.GetAsync("/api/v1/service/appointments")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ── Create / lifecycle ───────────────────────────────────────────────────
    [Fact]
    public async Task Create_then_get_and_drive_status_to_completed()
    {
        var c = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var ctx = await SetupAsync(c);
        var id = await CreateAppointmentAsync(c, ctx, Base, Base.AddHours(1));

        var got = await c.GetAsync($"/api/v1/service/appointments/{id}");
        got.StatusCode.Should().Be(HttpStatusCode.OK);
        (await got.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("status").GetString().Should().Be("Scheduled");

        (await Patch(c, id, "Confirmed")).Should().Be(HttpStatusCode.OK);
        (await Patch(c, id, "InProgress")).Should().Be(HttpStatusCode.OK);
        (await Patch(c, id, "Completed")).Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Invalid_status_transition_returns_422()
    {
        var c = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var ctx = await SetupAsync(c);
        var id = await CreateAppointmentAsync(c, ctx, Base.AddHours(2), Base.AddHours(3));
        // Scheduled → Completed is not allowed
        (await Patch(c, id, "Completed")).Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Overlap_for_same_professional_is_rejected_409_then_cancel_releases_slot()
    {
        var c = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var ctx = await SetupAsync(c);
        var s = Base.AddDays(1); var e = s.AddHours(1);
        var first = await CreateAppointmentAsync(c, ctx, s, e);

        // Overlapping 30 min later → 409
        var overlap = await PostAppointment(c, ctx, s.AddMinutes(30), e.AddMinutes(30));
        overlap.StatusCode.Should().Be(HttpStatusCode.Conflict);

        // Cancel the first, then the same slot is free → 201
        (await Patch(c, first, "Cancelled", "freeing")).Should().Be(HttpStatusCode.OK);
        var retry = await PostAppointment(c, ctx, s.AddMinutes(30), e.AddMinutes(30));
        retry.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task NoShow_and_Completed_are_terminal_and_block_edit()
    {
        var c = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var ctx = await SetupAsync(c);
        var id = await CreateAppointmentAsync(c, ctx, Base.AddDays(2), Base.AddDays(2).AddHours(1));
        (await Patch(c, id, "NoShow")).Should().Be(HttpStatusCode.OK);
        // editing a terminal appointment → 422
        var put = await c.PutAsJsonAsync($"/api/v1/service/appointments/{id}", new
        {
            customerId = ctx.CustomerId, professionalId = ctx.ProfessionalId, catalogItemId = ctx.CatalogItemId,
            startsAt = Base.AddDays(2).AddHours(5), endsAt = Base.AddDays(2).AddHours(6),
        });
        put.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Reschedule_changes_time()
    {
        var c = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var ctx = await SetupAsync(c);
        var id = await CreateAppointmentAsync(c, ctx, Base.AddDays(4), Base.AddDays(4).AddHours(1));
        var ns = Base.AddDays(4).AddHours(3); var ne = ns.AddHours(1);
        var put = await c.PutAsJsonAsync($"/api/v1/service/appointments/{id}", new
        {
            customerId = ctx.CustomerId, professionalId = ctx.ProfessionalId, catalogItemId = ctx.CatalogItemId,
            startsAt = ns, endsAt = ne,
        });
        put.StatusCode.Should().Be(HttpStatusCode.OK);
        (await put.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("startsAt").GetDateTime().ToUniversalTime().Should().Be(ns);
    }

    // ── Reference rules ──────────────────────────────────────────────────────
    [Fact]
    public async Task Inactive_professional_is_rejected_422()
    {
        var c = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var ctx = await SetupAsync(c);
        await c.PostAsync($"/api/v1/service/professionals/{ctx.ProfessionalId}/deactivate", null);
        var resp = await PostAppointment(c, ctx, Base.AddDays(5), Base.AddDays(5).AddHours(1));
        resp.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Inactive_catalog_item_is_rejected_422()
    {
        var c = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var ctx = await SetupAsync(c);
        await c.PostAsync($"/api/v1/service/catalog/{ctx.CatalogItemId}/deactivate", null);
        var resp = await PostAppointment(c, ctx, Base.AddDays(6), Base.AddDays(6).AddHours(1));
        resp.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task RequiresSubject_without_subject_is_rejected_422()
    {
        var c = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var customerId = await CreateCustomerAsync(c);
        var professionalId = await CreateProfessionalAsync(c);
        var catalogItemId = await CreateCatalogAsync(c, requiresSubject: true);
        var resp = await c.PostAsJsonAsync("/api/v1/service/appointments", new
        {
            customerId, professionalId, catalogItemId,
            startsAt = Base.AddDays(7), endsAt = Base.AddDays(7).AddHours(1),
        });
        resp.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Subject_belonging_to_another_customer_is_rejected_422()
    {
        var c = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var c1 = await CreateCustomerAsync(c);
        var c2 = await CreateCustomerAsync(c);
        var professionalId = await CreateProfessionalAsync(c);
        var catalogItemId = await CreateCatalogAsync(c, requiresSubject: true);
        var subjectOfC2 = await CreateSubjectAsync(c, c2);
        var resp = await c.PostAsJsonAsync("/api/v1/service/appointments", new
        {
            customerId = c1, professionalId, catalogItemId, subjectId = subjectOfC2,
            startsAt = Base.AddDays(8), endsAt = Base.AddDays(8).AddHours(1),
        });
        resp.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Invalid_payload_starts_after_ends_returns_400()
    {
        var c = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var ctx = await SetupAsync(c);
        var resp = await c.PostAsJsonAsync("/api/v1/service/appointments", new
        {
            customerId = ctx.CustomerId, professionalId = ctx.ProfessionalId, catalogItemId = ctx.CatalogItemId,
            startsAt = Base.AddDays(9).AddHours(2), endsAt = Base.AddDays(9),
        });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Appointment_from_another_tenant_is_not_accessible()
    {
        var foreignId = await SeedForeignAppointmentAsync();
        var c = await AuthClientFactory.LoginAsAdminAsync(_factory);
        (await c.GetAsync($"/api/v1/service/appointments/{foreignId}"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────
    private sealed record Ctx(Guid CustomerId, Guid ProfessionalId, Guid CatalogItemId);

    private static async Task<Ctx> SetupAsync(HttpClient c)
        => new(await CreateCustomerAsync(c), await CreateProfessionalAsync(c), await CreateCatalogAsync(c, false));

    private static async Task<Guid> CreateCustomerAsync(HttpClient c)
    {
        var r = await c.PostAsJsonAsync("/api/customers", new
        {
            personType = "Individual", name = "Cli " + Guid.NewGuid().ToString("N")[..8],
            documentType = "CPF", documentNumber = Guid.NewGuid().ToString("N")[..11],
        });
        r.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static async Task<Guid> CreateProfessionalAsync(HttpClient c)
    {
        var r = await c.PostAsJsonAsync("/api/v1/service/professionals", new { name = "Pro " + Guid.NewGuid().ToString("N")[..6] });
        r.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static async Task<Guid> CreateCatalogAsync(HttpClient c, bool requiresSubject)
    {
        var r = await c.PostAsJsonAsync("/api/v1/service/catalog", new
        {
            name = "Svc " + Guid.NewGuid().ToString("N")[..6], durationMinutes = 60, price = 120m, requiresSubject,
        });
        r.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static async Task<Guid> CreateSubjectAsync(HttpClient c, Guid customerId)
    {
        var r = await c.PostAsJsonAsync("/api/v1/service/subjects", new { customerId, kind = "Pet", displayName = "Rex" });
        r.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static Task<HttpResponseMessage> PostAppointment(HttpClient c, Ctx ctx, DateTime s, DateTime e)
        => c.PostAsJsonAsync("/api/v1/service/appointments", new
        {
            customerId = ctx.CustomerId, professionalId = ctx.ProfessionalId, catalogItemId = ctx.CatalogItemId,
            startsAt = s, endsAt = e,
        });

    private static async Task<Guid> CreateAppointmentAsync(HttpClient c, Ctx ctx, DateTime s, DateTime e)
    {
        var r = await PostAppointment(c, ctx, s, e);
        r.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static async Task<HttpStatusCode> Patch(HttpClient c, Guid id, string status, string? reason = null)
    {
        var r = await c.PatchAsJsonAsync($"/api/v1/service/appointments/{id}/status", new { status, reason });
        return r.StatusCode;
    }

    private async Task<Guid> SeedForeignAppointmentAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();

        var t = await db.Tenants.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.TaxId == ForeignTaxId);
        Guid storeId;
        if (t is null)
        {
            t = Tenant.Create("Appt Isolation Corp", ForeignTaxId, "admin@appt-iso.test");
            db.Tenants.Add(t);
            var store = Store.Create(t.Id, "AIC Store", "appt-iso-default");
            db.Stores.Add(store);
            await db.SaveChangesAsync();
            storeId = store.Id;
        }
        else storeId = await db.Stores.IgnoreQueryFilters().Where(s => s.TenantId == t.Id).Select(s => s.Id).FirstAsync();

        var cust = Customer.Create(t.Id, PersonType.Individual, "Foreign", DocumentType.Cpf, Guid.NewGuid().ToString("N")[..11]);
        db.Customers.Add(cust);
        var prof = SvcProfessional.Create(t.Id, "Foreign Pro");
        db.SvcProfessionals.Add(prof);
        db.Entry(prof).Property("StoreId").CurrentValue = storeId;
        var item = SvcCatalogItem.Create(t.Id, "Foreign Svc", 60, 100m);
        db.SvcCatalogItems.Add(item);
        db.Entry(item).Property("StoreId").CurrentValue = storeId;
        await db.SaveChangesAsync();

        var appt = SvcAppointment.Create(t.Id, cust.Id, prof.Id, item.Id, null,
            DateTime.UtcNow.AddDays(20), DateTime.UtcNow.AddDays(20).AddHours(1), 100m);
        db.SvcAppointments.Add(appt);
        db.Entry(appt).Property("StoreId").CurrentValue = storeId;
        await db.SaveChangesAsync();
        return appt.Id;
    }
}
```

> If `HttpClient.PatchAsJsonAsync` is unavailable, add `using System.Net.Http.Json;` (it ships with the test SDK) — it is the same extension namespace as `PostAsJsonAsync`.

- [ ] **Step 1: Run the new tests, watch them fail** (routes missing) — `dotnet test tests/Nexo.IntegrationTests --filter ServiceAppointmentsTests`. The 404-isolation test may pass coincidentally; the rest must fail.
- [ ] **Step 2: After Tasks 1–6 are implemented, run again → all green.**
- [ ] **Step 3: Commit** — `test(service): integration coverage for appointments/agenda (PR3)`

---

## Task 8: Full verification + PR

- [ ] **Step 1: Build** — `cd nexo-backend && dotnet build Nexo.sln` → 0 errors.
- [ ] **Step 2: Unit** — `dotnet test tests/Nexo.UnitTests` → green (incl. SvcAppointmentTests).
- [ ] **Step 3: Integration** — `dotnet test tests/Nexo.IntegrationTests` → green.
- [ ] **Step 4: Re-read migration `Up()`** — only `CreateTable(svc_appointments)` + indexes + new FKs.
- [ ] **Step 5: Diff scope** — `git diff --name-only origin/master` lists only Service files + the migration. No Auth/Redis/Stripe/SuperAdmin/Build, no frontend, no `dist`.
- [ ] **Step 6: Push + open PR (no merge)** — `gh pr create --base master`.

---

## Self-review (spec coverage)

- `SvcAppointment` entity + all fields (T1) ✔
- Status enum + transition matrix + invalid-transition guard (T1) ✔
- Overlap prevention (blockers Scheduled/Confirmed/InProgress; 409) (T4 repo + T6 service) ✔
- PriceSnapshot from catalog (T6) ✔
- Rules: active professional/catalog (422), RequiresSubject (422), subject-belongs-to-customer (422), terminal-edit (422), missing refs (404) (T6) ✔
- Endpoints GET(filters)/POST/GET{id}/PUT/PATCH status (T6) ✔
- `[Authorize]`+`[RequireServiceModule]` (T6) ✔
- Additive migration, proven (T3, T8) ✔
- `SvcSettings` deferred with justification (header + decisions table) ✔
- Tests: lifecycle, transitions valid/invalid, overlap+release, terminal, inactive prof/catalog, requiresSubject, subject mismatch, cross-tenant, gate, payload 400 (T7) ✔

## Risks
1. **`timestamptz` + DateTime kind** — `StartsAt`/`EndsAt` must be UTC or Npgsql throws. Tests use UTC; verify no reliance on `EnableLegacyTimestampBehavior`. If a non-UTC value reaches the DB it surfaces as 500 — covered by always sending `...Z`.
2. **Overlap race** — two concurrent creates could both pass the `HasOverlapAsync` check (no DB exclusion constraint). Acceptable for v1 (single-operator agenda); a future hardening could add a Postgres exclusion constraint. Documented.
3. **Reschedule re-snapshots price** — intentional (an explicit edit re-quotes the current catalog price); the snapshot still protects against *passive* future price drift. Documented in PR.
