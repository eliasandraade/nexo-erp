# Orken Service PR2 — Subjects + Records Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add the "service subject" (`SvcSubject` — pet/veículo/aluno/dependente) and internal "record entry" (`SvcRecordEntry` — observações/histórico + anexos por storageKey) to the Orken Service engine, backend-only, fully tenant/store-isolated and gated by the existing service-family module gate.

**Architecture:** Mirrors the PR1 foundation exactly (SvcProfessional/SvcCatalogItem). `SvcSubject` is a **TenantEntity** (a pet belongs to the customer and is shared across the tenant's stores). `SvcRecordEntry` is a **StoreEntity** (operational annotations are per-store). Both reuse: the EF global query filter for isolation, FluentValidation in controllers, `DomainException`→422 / `ValidationException`→400 / `NotFoundException`→404 middleware mapping, and the `IStoragePublicUrlResolver` read-time URL resolution decision from Build (storageKey is the durable primitive; public URLs are never persisted). Migration is strictly additive (two `CreateTable` + indexes).

**Tech Stack:** .NET 8, EF Core 8 (Npgsql/PostgreSQL, jsonb), FluentValidation, xUnit + FluentAssertions + Testcontainers.

---

## Design decisions (locked, grounded in the existing codebase)

| Concern | Decision | Why / precedent |
|---|---|---|
| `SvcSubject` base | `TenantEntity` | Customer is `TenantEntity` (shared across stores); the pet/vehicle follows the customer. Spec. |
| `SvcRecordEntry` base | `StoreEntity` | Operational records respect store/unit. Spec. |
| Enum storage | `.HasConversion<string>()` | Matches `Customer.PersonType`/`DocumentType`. Global `JsonStringEnumConverter` → invalid enum string in body auto-400s on bind. |
| `MetadataJson` / attachments column | `string?` mapped `HasColumnType("jsonb")` | Matches `Customer.AddressJson`, `ExtractionResult.LlmRawResponse`. |
| Attachment URL | NEVER persisted; resolve at read via `IStoragePublicUrlResolver.ResolvePublicUrl(storageKey)` | Build `BuildDailyLogPhoto` precedent. Durable primitive = storageKey. |
| Missing/foreign `CustomerId`/context | `NotFoundException` → **404** | Tenant query filter hides foreign rows → repo returns null → 404. Matches PR1 isolation test. |
| Blank `DisplayName`, missing `ContextType`/`ContextId`, "Text or Attachments" rule, reserved context type | FluentValidation → **400** | Controller runs `ValidateAndThrowAsync` first, same as `ProfessionalsController`. |
| Reserved context types (`Appointment`/`Order`/`Package`) | Declared in enum (reserved) but rejected by validator → **400** "not supported yet" | Spec: "Reservar para futuro, mas não aceitar ainda". Explicit, tested path. |
| `SvcRecordEntry` delete | **Hard delete** → 204 NoContent | Project has no soft-delete for operational child rows (`BuildDailyLogPhoto` hard-deletes). Records have no `IsActive`. Documented in PR body. |
| `AuthorUserId` | `ICurrentUser.UserId` | Standard. |
| Malformed `MetadataJson` | Validator parses with `JsonDocument.Parse` → **400** if invalid | Prevents a Postgres jsonb write throwing 500. |

## File structure

**Domain** (`nexo-backend/src/Nexo.Domain/Modules/Service/`)
- Create `SvcSubjectKind.cs` — `enum { Pet, Vehicle, Student, Dependent, Other }`
- Create `SvcSubject.cs` — `TenantEntity`; Create/UpdateDetails/UpdateMetadata/Activate/Deactivate
- Create `SvcRecordContextType.cs` — `enum { Customer, Subject, Appointment, Order, Package }` (only Customer/Subject accepted in v1)
- Create `SvcRecordEntry.cs` — `StoreEntity`; Create only (append-only)

**Application** (`nexo-backend/src/Nexo.Application/Modules/Service/`)
- Create `SvcSubjectDtos.cs` — `ISvcSubjectFields`, `SvcSubjectDto`, Create/Update requests
- Create `SvcRecordEntryDtos.cs` — `SvcRecordEntryDto`, `SvcRecordAttachmentInput`, `SvcRecordAttachmentDto`, `CreateSvcRecordEntryRequest`
- Create `SvcSubjectService.cs`
- Create `SvcRecordEntryService.cs`
- Modify `SvcValidators.cs` — add `ApplySubjectRules` + `ApplyRecordRules` + `BeValidJson` helper, and the 3 validator classes
- Create `Interfaces/ISvcSubjectRepository.cs`
- Create `Interfaces/ISvcRecordEntryRepository.cs`
- Modify `DependencyInjection.cs` — register `SvcSubjectService`, `SvcRecordEntryService`

**Infrastructure** (`nexo-backend/src/Nexo.Infrastructure/`)
- Modify `Persistence/Configurations/Modules/Service/SvcConfigurationExtensions.cs` — extract shared core, add `ConfigureTenantScopedSvcEntity` + `ConfigureStoreScopedSvcEntityNoActive` (keep `ConfigureStoreScopedSvcEntity` model-identical)
- Create `Persistence/Configurations/Modules/Service/SvcSubjectConfiguration.cs`
- Create `Persistence/Configurations/Modules/Service/SvcRecordEntryConfiguration.cs`
- Create `Repositories/Modules/Service/SvcSubjectRepository.cs`
- Create `Repositories/Modules/Service/SvcRecordEntryRepository.cs`
- Modify `DependencyInjection.cs` — register the two repositories
- Modify `Persistence/NexoDbContext.cs` — add `SvcSubjects`, `SvcRecordEntries` DbSets
- Create migration `Persistence/Migrations/<ts>_AddServiceSubjectsAndRecords.cs` (generated)

**API** (`nexo-backend/src/Nexo.Api/`)
- Create `Controllers/Modules/Service/SubjectsController.cs`
- Create `Controllers/Modules/Service/RecordsController.cs`
- Modify `Controllers/Integrations/StorageController.cs` — add `["service-record"] = "service/records"` context

**Tests** (`nexo-backend/tests/`)
- Create `Nexo.UnitTests/Service/SvcSubjectTests.cs`
- Create `Nexo.UnitTests/Service/SvcRecordEntryTests.cs`
- Create `Nexo.IntegrationTests/Service/ServiceSubjectsTests.cs`
- Create `Nexo.IntegrationTests/Service/ServiceRecordsTests.cs`
- Modify `Nexo.UnitTests/Integrations/StorageControllerTests.cs` (or integration `StorageUploadTests`) — assert `service-record` → `service/records`

---

## Task 1: Domain — enums + `SvcSubject` (unit tested)

**Files:**
- Create: `nexo-backend/src/Nexo.Domain/Modules/Service/SvcSubjectKind.cs`
- Create: `nexo-backend/src/Nexo.Domain/Modules/Service/SvcSubject.cs`
- Test: `nexo-backend/tests/Nexo.UnitTests/Service/SvcSubjectTests.cs`

- [ ] **Step 1: Write failing unit tests**

```csharp
// SvcSubjectTests.cs
using FluentAssertions;
using Nexo.Domain.Exceptions;
using Nexo.Domain.Modules.Service;
using Xunit;

namespace Nexo.UnitTests.Service;

public class SvcSubjectTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Cust   = Guid.NewGuid();

    [Fact]
    public void Create_sets_fields_and_defaults_active()
    {
        var s = SvcSubject.Create(Tenant, Cust, SvcSubjectKind.Pet, "Rex", "{\"species\":\"dog\"}", "friendly");
        s.CustomerId.Should().Be(Cust);
        s.Kind.Should().Be(SvcSubjectKind.Pet);
        s.DisplayName.Should().Be("Rex");
        s.MetadataJson.Should().Be("{\"species\":\"dog\"}");
        s.Notes.Should().Be("friendly");
        s.IsActive.Should().BeTrue();
    }

    [Fact]
    public void Create_trims_display_name()
        => SvcSubject.Create(Tenant, Cust, SvcSubjectKind.Vehicle, "  Civic  ").DisplayName.Should().Be("Civic");

    [Fact]
    public void Create_with_empty_customer_throws()
    {
        var act = () => SvcSubject.Create(Tenant, Guid.Empty, SvcSubjectKind.Pet, "Rex");
        act.Should().Throw<DomainException>().WithMessage("*customer*");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_with_blank_display_name_throws(string name)
    {
        var act = () => SvcSubject.Create(Tenant, Cust, SvcSubjectKind.Pet, name);
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void UpdateDetails_changes_kind_name_notes()
    {
        var s = SvcSubject.Create(Tenant, Cust, SvcSubjectKind.Pet, "Rex");
        s.UpdateDetails(SvcSubjectKind.Other, "Rex II", "older");
        s.Kind.Should().Be(SvcSubjectKind.Other);
        s.DisplayName.Should().Be("Rex II");
        s.Notes.Should().Be("older");
    }

    [Fact]
    public void UpdateMetadata_replaces_json()
    {
        var s = SvcSubject.Create(Tenant, Cust, SvcSubjectKind.Pet, "Rex");
        s.UpdateMetadata("{\"breed\":\"shih-tzu\"}");
        s.MetadataJson.Should().Be("{\"breed\":\"shih-tzu\"}");
    }

    [Fact]
    public void Activate_deactivate_toggles()
    {
        var s = SvcSubject.Create(Tenant, Cust, SvcSubjectKind.Pet, "Rex");
        s.Deactivate(); s.IsActive.Should().BeFalse();
        s.Activate();   s.IsActive.Should().BeTrue();
    }
}
```

- [ ] **Step 2: Run, verify fail** — `dotnet test nexo-backend/tests/Nexo.UnitTests --filter SvcSubjectTests` → FAIL (types missing).

- [ ] **Step 3: Implement the enum**

```csharp
// SvcSubjectKind.cs
namespace Nexo.Domain.Modules.Service;

/// <summary>
/// The kind of subject a service is performed on, when it is not the paying customer
/// themselves. Stored as a string (HasConversion&lt;string&gt;). Per-type detail lives in
/// <see cref="SvcSubject.MetadataJson"/>.
/// </summary>
public enum SvcSubjectKind
{
    Pet,
    Vehicle,
    Student,
    Dependent,
    Other,
}
```

- [ ] **Step 4: Implement `SvcSubject`**

```csharp
// SvcSubject.cs
using Nexo.Domain.Common;
using Nexo.Domain.Exceptions;

namespace Nexo.Domain.Modules.Service;

/// <summary>
/// The real subject of a service when it differs from the paying customer — a pet (tutor),
/// a vehicle (owner), a student/dependent (responsible party). Tenant-scoped (a
/// <see cref="TenantEntity"/>) because it belongs to the customer and may be served at more
/// than one store of the tenant. When a vertical does not need a distinct subject, records
/// reference the customer directly and no SvcSubject is created.
///
/// <see cref="MetadataJson"/> is free-form per-kind detail (species/breed, plate/model, level…)
/// stored as jsonb so the shape can evolve without migrations.
/// </summary>
public class SvcSubject : TenantEntity
{
    private SvcSubject() { }                                   // EF Core
    private SvcSubject(Guid tenantId) : base(tenantId) { }

    public Guid           CustomerId   { get; private set; }
    public SvcSubjectKind Kind         { get; private set; }
    public string         DisplayName  { get; private set; } = string.Empty;
    public string?        MetadataJson { get; private set; }
    public string?        Notes        { get; private set; }
    public bool           IsActive     { get; private set; }

    public static SvcSubject Create(
        Guid           tenantId,
        Guid           customerId,
        SvcSubjectKind kind,
        string         displayName,
        string?        metadataJson = null,
        string?        notes        = null)
    {
        if (customerId == Guid.Empty)
            throw new DomainException("Subject customer is required.");
        if (string.IsNullOrWhiteSpace(displayName))
            throw new DomainException("Subject display name is required.");

        return new SvcSubject(tenantId)
        {
            CustomerId   = customerId,
            Kind         = kind,
            DisplayName  = displayName.Trim(),
            MetadataJson = metadataJson,
            Notes        = notes?.Trim(),
            IsActive     = true,
        };
    }

    public void UpdateDetails(SvcSubjectKind kind, string displayName, string? notes)
    {
        if (string.IsNullOrWhiteSpace(displayName))
            throw new DomainException("Subject display name is required.");

        Kind        = kind;
        DisplayName = displayName.Trim();
        Notes       = notes?.Trim();
        SetUpdatedAt();
    }

    public void UpdateMetadata(string? metadataJson)
    {
        MetadataJson = metadataJson;
        SetUpdatedAt();
    }

    public void Activate()   { IsActive = true;  SetUpdatedAt(); }
    public void Deactivate() { IsActive = false; SetUpdatedAt(); }
}
```

- [ ] **Step 5: Run, verify pass** — `dotnet test nexo-backend/tests/Nexo.UnitTests --filter SvcSubjectTests` → PASS.

- [ ] **Step 6: Commit** — `git add -A && git commit -m "feat(service): SvcSubject domain entity (PR2)"`

---

## Task 2: Domain — `SvcRecordContextType` + `SvcRecordEntry` (unit tested)

**Files:**
- Create: `nexo-backend/src/Nexo.Domain/Modules/Service/SvcRecordContextType.cs`
- Create: `nexo-backend/src/Nexo.Domain/Modules/Service/SvcRecordEntry.cs`
- Test: `nexo-backend/tests/Nexo.UnitTests/Service/SvcRecordEntryTests.cs`

- [ ] **Step 1: Write failing unit tests**

```csharp
// SvcRecordEntryTests.cs
using FluentAssertions;
using Nexo.Domain.Exceptions;
using Nexo.Domain.Modules.Service;
using Xunit;

namespace Nexo.UnitTests.Service;

public class SvcRecordEntryTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Ctx    = Guid.NewGuid();
    private static readonly Guid Author = Guid.NewGuid();

    [Fact]
    public void Create_with_text_only_is_valid()
    {
        var r = SvcRecordEntry.Create(Tenant, SvcRecordContextType.Customer, Ctx, Author, "first visit", null);
        r.ContextType.Should().Be(SvcRecordContextType.Customer);
        r.ContextId.Should().Be(Ctx);
        r.AuthorUserId.Should().Be(Author);
        r.Text.Should().Be("first visit");
        r.AttachmentsJson.Should().BeNull();
    }

    [Fact]
    public void Create_with_attachments_only_is_valid()
    {
        var r = SvcRecordEntry.Create(Tenant, SvcRecordContextType.Subject, Ctx, Author, null, "[{\"storageKey\":\"k\"}]");
        r.AttachmentsJson.Should().NotBeNull();
        r.Text.Should().BeNull();
    }

    [Fact]
    public void Create_with_neither_text_nor_attachments_throws()
    {
        var act = () => SvcRecordEntry.Create(Tenant, SvcRecordContextType.Customer, Ctx, Author, "  ", null);
        act.Should().Throw<DomainException>().WithMessage("*text*attachment*");
    }

    [Fact]
    public void Create_with_empty_context_id_throws()
    {
        var act = () => SvcRecordEntry.Create(Tenant, SvcRecordContextType.Customer, Guid.Empty, Author, "x", null);
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Create_with_empty_author_throws()
    {
        var act = () => SvcRecordEntry.Create(Tenant, SvcRecordContextType.Customer, Ctx, Guid.Empty, "x", null);
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Create_trims_text()
        => SvcRecordEntry.Create(Tenant, SvcRecordContextType.Customer, Ctx, Author, "  hi  ", null).Text.Should().Be("hi");
}
```

- [ ] **Step 2: Run, verify fail.**

- [ ] **Step 3: Implement the enum**

```csharp
// SvcRecordContextType.cs
namespace Nexo.Domain.Modules.Service;

/// <summary>
/// What a <see cref="SvcRecordEntry"/> is attached to. Only <see cref="Customer"/> and
/// <see cref="Subject"/> are accepted in v1; the remaining values are reserved for later PRs
/// (agenda/OS/pacotes) and are rejected at the validation layer until then.
/// </summary>
public enum SvcRecordContextType
{
    Customer,
    Subject,
    Appointment, // reserved — PR3
    Order,       // reserved — PR4
    Package,     // reserved — PR5
}
```

- [ ] **Step 4: Implement `SvcRecordEntry`**

```csharp
// SvcRecordEntry.cs
using Nexo.Domain.Common;
using Nexo.Domain.Exceptions;

namespace Nexo.Domain.Modules.Service;

/// <summary>
/// An internal annotation / history note in the Service engine — "observações internas",
/// "histórico de atendimento". Store-scoped (a <see cref="StoreEntity"/>) operational record,
/// append-only (no edit; hard delete only). Attaches to a <see cref="SvcRecordContextType"/>
/// (customer or subject in v1).
///
/// NOTE (v1 scope): this is NOT a regulated medical record (não é prontuário CFM/CFO,
/// prescrição, nem sistema hospitalar) — it is a free-text note plus durable attachment
/// references. <see cref="AttachmentsJson"/> stores only durable fields (storageKey, fileName,
/// contentType, sizeBytes, caption); the public URL is composed at read time, never persisted.
/// </summary>
public class SvcRecordEntry : StoreEntity
{
    private SvcRecordEntry() { }                                   // EF Core
    private SvcRecordEntry(Guid tenantId) : base(tenantId) { }

    public SvcRecordContextType ContextType     { get; private set; }
    public Guid                 ContextId       { get; private set; }
    public Guid                 AuthorUserId    { get; private set; }
    public string?              Text            { get; private set; }
    public string?              AttachmentsJson { get; private set; }

    public static SvcRecordEntry Create(
        Guid                 tenantId,
        SvcRecordContextType contextType,
        Guid                 contextId,
        Guid                 authorUserId,
        string?              text,
        string?              attachmentsJson)
    {
        if (contextId == Guid.Empty)
            throw new DomainException("Record context id is required.");
        if (authorUserId == Guid.Empty)
            throw new DomainException("Record author is required.");
        if (string.IsNullOrWhiteSpace(text) && string.IsNullOrWhiteSpace(attachmentsJson))
            throw new DomainException("A record must have text or at least one attachment.");

        return new SvcRecordEntry(tenantId)
        {
            ContextType     = contextType,
            ContextId       = contextId,
            AuthorUserId    = authorUserId,
            Text            = string.IsNullOrWhiteSpace(text) ? null : text.Trim(),
            AttachmentsJson = string.IsNullOrWhiteSpace(attachmentsJson) ? null : attachmentsJson,
        };
    }
}
```

- [ ] **Step 5: Run, verify pass.**
- [ ] **Step 6: Commit** — `feat(service): SvcRecordEntry domain entity (PR2)`

---

## Task 3: EF configurations + DbContext DbSets (model verified, no PR1 drift)

**Files:**
- Modify: `nexo-backend/src/Nexo.Infrastructure/Persistence/Configurations/Modules/Service/SvcConfigurationExtensions.cs`
- Create: `.../Service/SvcSubjectConfiguration.cs`
- Create: `.../Service/SvcRecordEntryConfiguration.cs`
- Modify: `nexo-backend/src/Nexo.Infrastructure/Persistence/NexoDbContext.cs`

- [ ] **Step 1: Refactor `SvcConfigurationExtensions.cs`** — extract a shared core so the two new shapes (tenant-scoped-with-active, store-scoped-no-active) reuse it; keep `ConfigureStoreScopedSvcEntity` producing an **identical** model.

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nexo.Domain.Common;
using Nexo.Domain.Entities;

namespace Nexo.Infrastructure.Persistence.Configurations.Modules.Service;

/// <summary>
/// Shared EF mapping for the Service (svc_*) tables: key, tenant/store columns + FKs, optional
/// is_active, audit columns, and the standard indexes. Constraint and index names derive from
/// the table name so each entity keeps its own. Entity-specific columns are mapped by the
/// calling configuration. Verified model-identical for the PR1 tables with
/// `dotnet ef migrations has-pending-model-changes`.
/// </summary>
internal static class SvcConfigurationExtensions
{
    // Common to every svc_* table: table+schema, PK, tenant column + FK, audit columns.
    private static void ConfigureKeyTenantAudit<T>(this EntityTypeBuilder<T> b, string table)
        where T : TenantEntity
    {
        b.ToTable(table, "nexo");
        b.HasKey(x => x.Id);

        b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        b.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz").IsRequired();
        b.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamptz").IsRequired();

        b.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(x => x.TenantId)
            .HasConstraintName($"fk_{table}_tenants")
            .OnDelete(DeleteBehavior.Cascade);
    }

    // store_id column + FK + its index, for StoreEntity tables.
    private static void ConfigureStore<T>(this EntityTypeBuilder<T> b, string table)
        where T : StoreEntity
    {
        b.Property(x => x.StoreId).HasColumnName("store_id").IsRequired();

        b.HasOne<Store>()
            .WithMany()
            .HasForeignKey(x => x.StoreId)
            .HasConstraintName($"fk_{table}_stores")
            .OnDelete(DeleteBehavior.Restrict);

        b.HasIndex("StoreId").HasDatabaseName($"ix_{table}_store_id");
    }

    private static void ConfigureIsActive<T>(this EntityTypeBuilder<T> b) where T : class
        => b.Property<bool>("IsActive").HasColumnName("is_active").HasDefaultValue(true).IsRequired();

    /// <summary>Store-scoped + is_active (svc_professionals, svc_catalog_items). Model unchanged from PR1.</summary>
    public static void ConfigureStoreScopedSvcEntity<T>(this EntityTypeBuilder<T> b, string table)
        where T : StoreEntity
    {
        b.ConfigureKeyTenantAudit(table);
        b.ConfigureStore(table);
        b.ConfigureIsActive();
        b.HasIndex("TenantId", "StoreId", "IsActive").HasDatabaseName($"ix_{table}_tenant_store_active");
    }

    /// <summary>Tenant-scoped + is_active (svc_subjects). Caller adds the customer_id index.</summary>
    public static void ConfigureTenantScopedSvcEntity<T>(this EntityTypeBuilder<T> b, string table)
        where T : TenantEntity
    {
        b.ConfigureKeyTenantAudit(table);
        b.ConfigureIsActive();
    }

    /// <summary>Store-scoped, NO is_active (svc_record_entries — append-only annotations).</summary>
    public static void ConfigureStoreScopedSvcEntityNoActive<T>(this EntityTypeBuilder<T> b, string table)
        where T : StoreEntity
    {
        b.ConfigureKeyTenantAudit(table);
        b.ConfigureStore(table);
    }
}
```

- [ ] **Step 2: Verify PR1 model is unchanged BEFORE adding the new entities to the context.** Temporarily (entities not yet in DbContext) run:

Run: `cd nexo-backend && dotnet ef migrations has-pending-model-changes -p src/Nexo.Infrastructure -s src/Nexo.Api`
Expected: **"No changes have been made to the model since the last migration."** (the refactor alone is model-neutral). If it reports changes referencing `svc_professionals`/`svc_catalog_items`, STOP and fix the refactor.

- [ ] **Step 3: Create `SvcSubjectConfiguration.cs`**

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nexo.Domain.Entities;
using Nexo.Domain.Modules.Service;

namespace Nexo.Infrastructure.Persistence.Configurations.Modules.Service;

public class SvcSubjectConfiguration : IEntityTypeConfiguration<SvcSubject>
{
    public void Configure(EntityTypeBuilder<SvcSubject> builder)
    {
        // Key, tenant column + FK, is_active, audit columns.
        builder.ConfigureTenantScopedSvcEntity("svc_subjects");

        builder.Property(x => x.CustomerId).HasColumnName("customer_id").IsRequired();
        builder.Property(x => x.Kind)
            .HasColumnName("kind").HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.DisplayName).HasColumnName("display_name").HasMaxLength(200).IsRequired();
        builder.Property(x => x.MetadataJson).HasColumnName("metadata_json").HasColumnType("jsonb");
        builder.Property(x => x.Notes).HasColumnName("notes").HasMaxLength(2000);

        builder.HasOne<Customer>()
            .WithMany()
            .HasForeignKey(x => x.CustomerId)
            .HasConstraintName("fk_svc_subjects_customers")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.CustomerId).HasDatabaseName("ix_svc_subjects_customer_id");
        builder.HasIndex("TenantId", "IsActive").HasDatabaseName("ix_svc_subjects_tenant_active");
    }
}
```

- [ ] **Step 4: Create `SvcRecordEntryConfiguration.cs`**

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nexo.Domain.Modules.Service;

namespace Nexo.Infrastructure.Persistence.Configurations.Modules.Service;

public class SvcRecordEntryConfiguration : IEntityTypeConfiguration<SvcRecordEntry>
{
    public void Configure(EntityTypeBuilder<SvcRecordEntry> builder)
    {
        // Key, tenant/store columns + FKs, audit columns (no is_active — records are append-only).
        builder.ConfigureStoreScopedSvcEntityNoActive("svc_record_entries");

        builder.Property(x => x.ContextType)
            .HasColumnName("context_type").HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.ContextId).HasColumnName("context_id").IsRequired();
        builder.Property(x => x.AuthorUserId).HasColumnName("author_user_id").IsRequired();
        builder.Property(x => x.Text).HasColumnName("text").HasMaxLength(10000);
        builder.Property(x => x.AttachmentsJson).HasColumnName("attachments_json").HasColumnType("jsonb");

        builder.HasIndex("TenantId", "StoreId", "ContextType", "ContextId")
            .HasDatabaseName("ix_svc_record_entries_context");
    }
}
```

Note: `ContextId` is intentionally a plain Guid column (no FK) — it is polymorphic (customer or subject), so referential integrity is enforced in the application layer, not the DB.

- [ ] **Step 5: Add DbSets to `NexoDbContext.cs`** — in the Service section (after `SvcCatalogItems`):

```csharp
        public DbSet<SvcSubject>          SvcSubjects      => Set<SvcSubject>();
        public DbSet<SvcRecordEntry>      SvcRecordEntries => Set<SvcRecordEntry>();
```

- [ ] **Step 6: Build** — `cd nexo-backend && dotnet build Nexo.sln` → success.
- [ ] **Step 7: Commit** — `feat(service): EF config + DbSets for SvcSubject/SvcRecordEntry (PR2)`

---

## Task 4: Additive migration

**Files:**
- Create (generated): `nexo-backend/src/Nexo.Infrastructure/Persistence/Migrations/<timestamp>_AddServiceSubjectsAndRecords.cs` (+ `.Designer.cs`)

- [ ] **Step 1: Generate the migration**

Run: `cd nexo-backend && dotnet ef migrations add AddServiceSubjectsAndRecords -p src/Nexo.Infrastructure -s src/Nexo.Api -o Persistence/Migrations`

- [ ] **Step 2: PROVE the migration is purely additive.** Open the generated `Up()` and confirm it contains ONLY:
  - `CreateTable("svc_subjects", …)`
  - `CreateTable("svc_record_entries", …)`
  - `CreateIndex(…)` for the new tables

It MUST NOT contain any of: `DropTable`, `DropColumn`, `AlterColumn`, `RenameColumn`, `AddColumn`/`CreateIndex`/`DropIndex` against `svc_professionals`, `svc_catalog_items`, or any pre-existing table. If anything outside the two new tables appears, STOP and report — the Task-3 refactor leaked a model change.

- [ ] **Step 3: Sanity-check the model is now clean**

Run: `dotnet ef migrations has-pending-model-changes -p src/Nexo.Infrastructure -s src/Nexo.Api`
Expected: "No changes…".

- [ ] **Step 4: Commit** — `feat(service): additive migration AddServiceSubjectsAndRecords (PR2)`

---

## Task 5: Repositories + interfaces + DI

**Files:**
- Create: `nexo-backend/src/Nexo.Application/Modules/Service/Interfaces/ISvcSubjectRepository.cs`
- Create: `nexo-backend/src/Nexo.Application/Modules/Service/Interfaces/ISvcRecordEntryRepository.cs`
- Create: `nexo-backend/src/Nexo.Infrastructure/Repositories/Modules/Service/SvcSubjectRepository.cs`
- Create: `nexo-backend/src/Nexo.Infrastructure/Repositories/Modules/Service/SvcRecordEntryRepository.cs`
- Modify: `nexo-backend/src/Nexo.Infrastructure/DependencyInjection.cs`

- [ ] **Step 1: `ISvcSubjectRepository.cs`**

```csharp
using Nexo.Domain.Modules.Service;

namespace Nexo.Application.Modules.Service.Interfaces;

/// <summary>Repository for SvcSubject. Tenant isolation is enforced by the EF global query filter.</summary>
public interface ISvcSubjectRepository
{
    Task<SvcSubject?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<SvcSubject>> GetAllAsync(
        Guid? customerId = null, SvcSubjectKind? kind = null, bool? active = null, CancellationToken ct = default);
    Task AddAsync(SvcSubject entity, CancellationToken ct = default);
    void Update(SvcSubject entity);
    Task SaveChangesAsync(CancellationToken ct = default);
}
```

- [ ] **Step 2: `ISvcRecordEntryRepository.cs`**

```csharp
using Nexo.Domain.Modules.Service;

namespace Nexo.Application.Modules.Service.Interfaces;

/// <summary>Repository for SvcRecordEntry. Tenant + store isolation enforced by the EF global query filter.</summary>
public interface ISvcRecordEntryRepository
{
    Task<SvcRecordEntry?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<SvcRecordEntry>> GetByContextAsync(
        SvcRecordContextType contextType, Guid contextId, CancellationToken ct = default);
    Task AddAsync(SvcRecordEntry entity, CancellationToken ct = default);
    void Remove(SvcRecordEntry entity);
    Task SaveChangesAsync(CancellationToken ct = default);
}
```

- [ ] **Step 3: `SvcSubjectRepository.cs`**

```csharp
using Microsoft.EntityFrameworkCore;
using Nexo.Application.Modules.Service.Interfaces;
using Nexo.Domain.Modules.Service;
using Nexo.Infrastructure.Persistence;

namespace Nexo.Infrastructure.Repositories.Modules.Service;

public class SvcSubjectRepository : ISvcSubjectRepository
{
    private readonly NexoDbContext _context;

    public SvcSubjectRepository(NexoDbContext context) => _context = context;

    public async Task<SvcSubject?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await _context.SvcSubjects.FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<IReadOnlyList<SvcSubject>> GetAllAsync(
        Guid? customerId = null, SvcSubjectKind? kind = null, bool? active = null, CancellationToken ct = default)
    {
        var query = _context.SvcSubjects.AsQueryable();
        if (customerId is { } cid) query = query.Where(x => x.CustomerId == cid);
        if (kind is { } k)         query = query.Where(x => x.Kind == k);
        if (active is { } a)       query = query.Where(x => x.IsActive == a);

        return await query
            .OrderByDescending(x => x.IsActive)
            .ThenBy(x => x.DisplayName)
            .ToListAsync(ct);
    }

    public async Task AddAsync(SvcSubject entity, CancellationToken ct = default)
        => await _context.SvcSubjects.AddAsync(entity, ct);

    public void Update(SvcSubject entity) => _context.SvcSubjects.Update(entity);

    public async Task SaveChangesAsync(CancellationToken ct = default)
        => await _context.SaveChangesAsync(ct);
}
```

- [ ] **Step 4: `SvcRecordEntryRepository.cs`**

```csharp
using Microsoft.EntityFrameworkCore;
using Nexo.Application.Modules.Service.Interfaces;
using Nexo.Domain.Modules.Service;
using Nexo.Infrastructure.Persistence;

namespace Nexo.Infrastructure.Repositories.Modules.Service;

public class SvcRecordEntryRepository : ISvcRecordEntryRepository
{
    private readonly NexoDbContext _context;

    public SvcRecordEntryRepository(NexoDbContext context) => _context = context;

    public async Task<SvcRecordEntry?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await _context.SvcRecordEntries.FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<IReadOnlyList<SvcRecordEntry>> GetByContextAsync(
        SvcRecordContextType contextType, Guid contextId, CancellationToken ct = default)
        => await _context.SvcRecordEntries
            .Where(x => x.ContextType == contextType && x.ContextId == contextId)
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync(ct);

    public async Task AddAsync(SvcRecordEntry entity, CancellationToken ct = default)
        => await _context.SvcRecordEntries.AddAsync(entity, ct);

    public void Remove(SvcRecordEntry entity) => _context.SvcRecordEntries.Remove(entity);

    public async Task SaveChangesAsync(CancellationToken ct = default)
        => await _context.SaveChangesAsync(ct);
}
```

- [ ] **Step 5: Register in Infrastructure `DependencyInjection.cs`** — under the Service module repositories block:

```csharp
        services.AddScoped<ISvcSubjectRepository, SvcSubjectRepository>();
        services.AddScoped<ISvcRecordEntryRepository, SvcRecordEntryRepository>();
```

- [ ] **Step 6: Build** → success. **Commit** — `feat(service): repositories + DI for subjects/records (PR2)`

---

## Task 6: Subject DTOs + validators

**Files:**
- Create: `nexo-backend/src/Nexo.Application/Modules/Service/SvcSubjectDtos.cs`
- Modify: `nexo-backend/src/Nexo.Application/Modules/Service/SvcValidators.cs`

- [ ] **Step 1: `SvcSubjectDtos.cs`**

```csharp
using Nexo.Domain.Modules.Service;

namespace Nexo.Application.Modules.Service;

/// <summary>Editable subject fields shared by create + update, validated by one rule set.</summary>
public interface ISvcSubjectFields
{
    SvcSubjectKind Kind { get; }
    string         DisplayName { get; }
    string?        MetadataJson { get; }
    string?        Notes { get; }
}

public sealed record SvcSubjectDto(
    Guid           Id,
    Guid           CustomerId,
    SvcSubjectKind Kind,
    string         DisplayName,
    string?        MetadataJson,
    string?        Notes,
    bool           IsActive,
    DateTime       CreatedAt,
    DateTime       UpdatedAt);

public sealed record CreateSvcSubjectRequest(
    Guid           CustomerId,
    SvcSubjectKind Kind,
    string         DisplayName,
    string?        MetadataJson = null,
    string?        Notes        = null) : ISvcSubjectFields;

/// <summary>Update editable details + metadata in one PUT. CustomerId is fixed at creation.</summary>
public sealed record UpdateSvcSubjectRequest(
    SvcSubjectKind Kind,
    string         DisplayName,
    string?        MetadataJson = null,
    string?        Notes        = null) : ISvcSubjectFields;
```

- [ ] **Step 2: Extend `SvcValidators.cs`** — add to `SvcValidationRules` a JSON guard + subject rules, and the two validator classes.

Add `using` at top: `using System.Text.Json;`

Inside `SvcValidationRules` add:

```csharp
    public static bool BeValidJsonOrNull(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return true;
        try { using var _ = JsonDocument.Parse(json); return true; }
        catch (JsonException) { return false; }
    }

    public static void ApplySubjectRules<T>(AbstractValidator<T> v) where T : ISvcSubjectFields
    {
        v.RuleFor(x => x.Kind).IsInEnum().WithMessage("Invalid subject kind.");
        v.RuleFor(x => x.DisplayName)
            .NotEmpty().WithMessage("Subject display name is required.")
            .MaximumLength(200).WithMessage("Display name must not exceed 200 characters.");
        v.RuleFor(x => x.Notes).MaximumLength(2000).When(x => x.Notes is not null);
        v.RuleFor(x => x.MetadataJson)
            .Must(BeValidJsonOrNull).WithMessage("MetadataJson must be valid JSON.");
    }
```

Add the validator classes (after the existing catalog ones):

```csharp
public class CreateSvcSubjectRequestValidator : AbstractValidator<CreateSvcSubjectRequest>
{
    public CreateSvcSubjectRequestValidator()
    {
        RuleFor(x => x.CustomerId).NotEmpty().WithMessage("CustomerId is required.");
        SvcValidationRules.ApplySubjectRules(this);
    }
}

public class UpdateSvcSubjectRequestValidator : AbstractValidator<UpdateSvcSubjectRequest>
{
    public UpdateSvcSubjectRequestValidator() => SvcValidationRules.ApplySubjectRules(this);
}
```

- [ ] **Step 3: Build** → success. **Commit** — `feat(service): subject DTOs + validators (PR2)`

---

## Task 7: `SvcSubjectService` + `SubjectsController` + DI

**Files:**
- Create: `nexo-backend/src/Nexo.Application/Modules/Service/SvcSubjectService.cs`
- Modify: `nexo-backend/src/Nexo.Application/DependencyInjection.cs`
- Create: `nexo-backend/src/Nexo.Api/Controllers/Modules/Service/SubjectsController.cs`

- [ ] **Step 1: `SvcSubjectService.cs`**

```csharp
using Nexo.Application.Common.Interfaces;
using Nexo.Application.Modules.Service.Interfaces;
using Nexo.Domain.Entities;
using Nexo.Domain.Exceptions;
using Nexo.Domain.Modules.Service;

namespace Nexo.Application.Modules.Service;

/// <summary>
/// Use cases for the SvcSubject aggregate. Tenant isolation is enforced by the EF global query
/// filter; the referenced Customer is validated through ICustomerRepository (also tenant-filtered),
/// so a customer from another tenant is invisible → NotFound → 404 (blocks cross-tenant linking).
/// </summary>
public class SvcSubjectService
{
    private readonly ISvcSubjectRepository _repo;
    private readonly ICustomerRepository   _customers;
    private readonly ICurrentTenant        _currentTenant;

    public SvcSubjectService(
        ISvcSubjectRepository repo, ICustomerRepository customers, ICurrentTenant currentTenant)
    {
        _repo          = repo;
        _customers     = customers;
        _currentTenant = currentTenant;
    }

    public async Task<IReadOnlyList<SvcSubjectDto>> GetAllAsync(
        Guid? customerId = null, SvcSubjectKind? kind = null, bool? active = null, CancellationToken ct = default)
        => (await _repo.GetAllAsync(customerId, kind, active, ct)).Select(MapToDto).ToList();

    public async Task<SvcSubjectDto> GetByIdAsync(Guid id, CancellationToken ct = default)
        => MapToDto(await _repo.GetByIdAsync(id, ct) ?? throw new NotFoundException("SvcSubject", id));

    public async Task<SvcSubjectDto> CreateAsync(CreateSvcSubjectRequest request, CancellationToken ct = default)
    {
        await EnsureCustomerExistsAsync(request.CustomerId, ct);

        var subject = SvcSubject.Create(
            tenantId:     _currentTenant.Id,
            customerId:   request.CustomerId,
            kind:         request.Kind,
            displayName:  request.DisplayName,
            metadataJson: request.MetadataJson,
            notes:        request.Notes);

        await _repo.AddAsync(subject, ct);
        await _repo.SaveChangesAsync(ct);
        return MapToDto(subject);
    }

    public async Task<SvcSubjectDto> UpdateAsync(Guid id, UpdateSvcSubjectRequest request, CancellationToken ct = default)
    {
        var subject = await _repo.GetByIdAsync(id, ct) ?? throw new NotFoundException("SvcSubject", id);

        subject.UpdateDetails(request.Kind, request.DisplayName, request.Notes);
        subject.UpdateMetadata(request.MetadataJson);

        _repo.Update(subject);
        await _repo.SaveChangesAsync(ct);
        return MapToDto(subject);
    }

    public async Task<SvcSubjectDto> ActivateAsync(Guid id, CancellationToken ct = default)
        => await ToggleAsync(id, activate: true, ct);

    public async Task<SvcSubjectDto> DeactivateAsync(Guid id, CancellationToken ct = default)
        => await ToggleAsync(id, activate: false, ct);

    private async Task<SvcSubjectDto> ToggleAsync(Guid id, bool activate, CancellationToken ct)
    {
        var subject = await _repo.GetByIdAsync(id, ct) ?? throw new NotFoundException("SvcSubject", id);
        if (activate) subject.Activate(); else subject.Deactivate();
        _repo.Update(subject);
        await _repo.SaveChangesAsync(ct);
        return MapToDto(subject);
    }

    private async Task EnsureCustomerExistsAsync(Guid customerId, CancellationToken ct)
    {
        _ = await _customers.GetByIdAsync(customerId, ct)
            ?? throw new NotFoundException(nameof(Customer), customerId);
    }

    internal static SvcSubjectDto MapToDto(SvcSubject s) => new(
        Id:           s.Id,
        CustomerId:   s.CustomerId,
        Kind:         s.Kind,
        DisplayName:  s.DisplayName,
        MetadataJson: s.MetadataJson,
        Notes:        s.Notes,
        IsActive:     s.IsActive,
        CreatedAt:    s.CreatedAt,
        UpdatedAt:    s.UpdatedAt);
}
```

- [ ] **Step 2: Register in Application `DependencyInjection.cs`** — under the Service module block:

```csharp
        services.AddScoped<SvcSubjectService>();
```

- [ ] **Step 3: `SubjectsController.cs`**

```csharp
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nexo.Api.Attributes;
using Nexo.Application.Modules.Service;
using Nexo.Domain.Modules.Service;

namespace Nexo.Api.Controllers.Modules.Service;

/// <summary>
/// Service subjects (ORKEN SERVICE) — pet/veículo/aluno/dependente. Tenant-scoped (shared
/// across the tenant's stores). All endpoints require an active service-family subscription.
/// </summary>
[ApiController]
[Route("api/v1/service/subjects")]
[Authorize]
[RequireServiceModule]
public class SubjectsController : ControllerBase
{
    private readonly SvcSubjectService                   _service;
    private readonly IValidator<CreateSvcSubjectRequest> _createValidator;
    private readonly IValidator<UpdateSvcSubjectRequest> _updateValidator;

    public SubjectsController(
        SvcSubjectService                   service,
        IValidator<CreateSvcSubjectRequest> createValidator,
        IValidator<UpdateSvcSubjectRequest> updateValidator)
    {
        _service         = service;
        _createValidator = createValidator;
        _updateValidator = updateValidator;
    }

    /// <summary>Lists subjects, optionally filtered by customer, kind, and active state.</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<SvcSubjectDto>>> GetAll(
        [FromQuery] Guid? customerId,
        [FromQuery] SvcSubjectKind? kind,
        [FromQuery] bool? active,
        CancellationToken ct = default)
        => Ok(await _service.GetAllAsync(customerId, kind, active, ct));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<SvcSubjectDto>> GetById(Guid id, CancellationToken ct)
        => Ok(await _service.GetByIdAsync(id, ct));

    [HttpPost]
    public async Task<ActionResult<SvcSubjectDto>> Create(
        [FromBody] CreateSvcSubjectRequest request, CancellationToken ct)
    {
        await _createValidator.ValidateAndThrowAsync(request, ct);
        var dto = await _service.CreateAsync(request, ct);
        return CreatedAtAction(nameof(GetById), new { id = dto.Id }, dto);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<SvcSubjectDto>> Update(
        Guid id, [FromBody] UpdateSvcSubjectRequest request, CancellationToken ct)
    {
        await _updateValidator.ValidateAndThrowAsync(request, ct);
        return Ok(await _service.UpdateAsync(id, request, ct));
    }

    [HttpPost("{id:guid}/activate")]
    public async Task<ActionResult<SvcSubjectDto>> Activate(Guid id, CancellationToken ct)
        => Ok(await _service.ActivateAsync(id, ct));

    [HttpPost("{id:guid}/deactivate")]
    public async Task<ActionResult<SvcSubjectDto>> Deactivate(Guid id, CancellationToken ct)
        => Ok(await _service.DeactivateAsync(id, ct));
}
```

- [ ] **Step 4: Build** → success. **Commit** — `feat(service): SvcSubjectService + SubjectsController (PR2)`

---

## Task 8: Record DTOs + validator + `SvcRecordEntryService` + `RecordsController` + DI

**Files:**
- Create: `nexo-backend/src/Nexo.Application/Modules/Service/SvcRecordEntryDtos.cs`
- Modify: `nexo-backend/src/Nexo.Application/Modules/Service/SvcValidators.cs`
- Create: `nexo-backend/src/Nexo.Application/Modules/Service/SvcRecordEntryService.cs`
- Modify: `nexo-backend/src/Nexo.Application/DependencyInjection.cs`
- Create: `nexo-backend/src/Nexo.Api/Controllers/Modules/Service/RecordsController.cs`

- [ ] **Step 1: `SvcRecordEntryDtos.cs`**

```csharp
using Nexo.Domain.Modules.Service;

namespace Nexo.Application.Modules.Service;

/// <summary>Durable attachment metadata persisted in the record's jsonb column. No public URL.</summary>
public sealed record SvcRecordAttachmentInput(
    string  StorageKey,
    string? FileName    = null,
    string? ContentType = null,
    long?   SizeBytes   = null,
    string? Caption     = null);

/// <summary>Attachment as returned on read: durable fields + the URL resolved from StorageKey.</summary>
public sealed record SvcRecordAttachmentDto(
    string  StorageKey,
    string? FileName,
    string? ContentType,
    long?   SizeBytes,
    string? Caption,
    string? Url);

public sealed record SvcRecordEntryDto(
    Guid                            Id,
    Guid                            StoreId,
    SvcRecordContextType            ContextType,
    Guid                            ContextId,
    Guid                            AuthorUserId,
    string?                         Text,
    IReadOnlyList<SvcRecordAttachmentDto> Attachments,
    DateTime                        CreatedAt);

public sealed record CreateSvcRecordEntryRequest(
    SvcRecordContextType?                ContextType,
    Guid?                               ContextId,
    string?                             Text = null,
    IReadOnlyList<SvcRecordAttachmentInput>? Attachments = null);
```

- [ ] **Step 2: Add record validator to `SvcValidators.cs`**

```csharp
public class CreateSvcRecordEntryRequestValidator : AbstractValidator<CreateSvcRecordEntryRequest>
{
    private static readonly SvcRecordContextType[] Supported =
        { SvcRecordContextType.Customer, SvcRecordContextType.Subject };

    public CreateSvcRecordEntryRequestValidator()
    {
        RuleFor(x => x.ContextType)
            .NotNull().WithMessage("ContextType is required.")
            .Must(ct => ct is null || Supported.Contains(ct.Value))
            .WithMessage("ContextType is not supported yet. Use Customer or Subject.");

        RuleFor(x => x.ContextId)
            .NotNull().NotEqual(Guid.Empty).WithMessage("ContextId is required.");

        RuleFor(x => x)
            .Must(r => !string.IsNullOrWhiteSpace(r.Text) || (r.Attachments is { Count: > 0 }))
            .WithMessage("A record must have text or at least one attachment.");

        RuleForEach(x => x.Attachments)
            .Must(a => !string.IsNullOrWhiteSpace(a.StorageKey))
            .WithMessage("Each attachment must have a storageKey.")
            .When(x => x.Attachments is not null);
    }
}
```

- [ ] **Step 3: `SvcRecordEntryService.cs`**

```csharp
using System.Text.Json;
using Nexo.Application.Common.Interfaces;
using Nexo.Application.Integrations.Contracts;
using Nexo.Application.Modules.Service.Interfaces;
using Nexo.Domain.Entities;
using Nexo.Domain.Exceptions;
using Nexo.Domain.Modules.Service;

namespace Nexo.Application.Modules.Service;

/// <summary>
/// Use cases for SvcRecordEntry — internal annotations/history with durable attachment refs.
/// The context (customer or subject) is validated through tenant-filtered repositories, so a
/// cross-tenant context is invisible → NotFound → 404. Attachment public URLs are composed at
/// read time from the durable storageKey (IStoragePublicUrlResolver); they are never persisted.
/// </summary>
public class SvcRecordEntryService
{
    private readonly ISvcRecordEntryRepository  _repo;
    private readonly ISvcSubjectRepository      _subjects;
    private readonly ICustomerRepository        _customers;
    private readonly ICurrentTenant             _currentTenant;
    private readonly ICurrentUser               _currentUser;
    private readonly IStoragePublicUrlResolver  _urls;

    public SvcRecordEntryService(
        ISvcRecordEntryRepository repo,
        ISvcSubjectRepository     subjects,
        ICustomerRepository       customers,
        ICurrentTenant            currentTenant,
        ICurrentUser              currentUser,
        IStoragePublicUrlResolver urls)
    {
        _repo          = repo;
        _subjects      = subjects;
        _customers     = customers;
        _currentTenant = currentTenant;
        _currentUser   = currentUser;
        _urls          = urls;
    }

    public async Task<IReadOnlyList<SvcRecordEntryDto>> GetByContextAsync(
        SvcRecordContextType contextType, Guid contextId, CancellationToken ct = default)
        => (await _repo.GetByContextAsync(contextType, contextId, ct)).Select(MapToDto).ToList();

    public async Task<SvcRecordEntryDto> GetByIdAsync(Guid id, CancellationToken ct = default)
        => MapToDto(await _repo.GetByIdAsync(id, ct) ?? throw new NotFoundException("SvcRecordEntry", id));

    public async Task<SvcRecordEntryDto> CreateAsync(CreateSvcRecordEntryRequest request, CancellationToken ct = default)
    {
        // ContextType / ContextId are NotNull-validated upstream.
        var contextType = request.ContextType!.Value;
        var contextId   = request.ContextId!.Value;

        await EnsureContextExistsAsync(contextType, contextId, ct);

        var attachmentsJson = (request.Attachments is { Count: > 0 })
            ? JsonSerializer.Serialize(request.Attachments)
            : null;

        var entry = SvcRecordEntry.Create(
            tenantId:        _currentTenant.Id,
            contextType:     contextType,
            contextId:       contextId,
            authorUserId:    _currentUser.UserId,
            text:            request.Text,
            attachmentsJson: attachmentsJson);

        await _repo.AddAsync(entry, ct);
        await _repo.SaveChangesAsync(ct);
        return MapToDto(entry);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var entry = await _repo.GetByIdAsync(id, ct) ?? throw new NotFoundException("SvcRecordEntry", id);
        _repo.Remove(entry);
        await _repo.SaveChangesAsync(ct);
    }

    private async Task EnsureContextExistsAsync(SvcRecordContextType type, Guid id, CancellationToken ct)
    {
        switch (type)
        {
            case SvcRecordContextType.Customer:
                _ = await _customers.GetByIdAsync(id, ct) ?? throw new NotFoundException(nameof(Customer), id);
                break;
            case SvcRecordContextType.Subject:
                _ = await _subjects.GetByIdAsync(id, ct) ?? throw new NotFoundException("SvcSubject", id);
                break;
            default:
                // Defense in depth — the validator already rejects reserved context types.
                throw new DomainException("Context type is not supported yet.");
        }
    }

    private SvcRecordEntryDto MapToDto(SvcRecordEntry e) => new(
        Id:           e.Id,
        StoreId:      e.StoreId,
        ContextType:  e.ContextType,
        ContextId:    e.ContextId,
        AuthorUserId: e.AuthorUserId,
        Text:         e.Text,
        Attachments:  ParseAttachments(e.AttachmentsJson),
        CreatedAt:    e.CreatedAt);

    private IReadOnlyList<SvcRecordAttachmentDto> ParseAttachments(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<SvcRecordAttachmentDto>();
        var raw = JsonSerializer.Deserialize<List<SvcRecordAttachmentInput>>(json)
                  ?? new List<SvcRecordAttachmentInput>();
        return raw.Select(a => new SvcRecordAttachmentDto(
            StorageKey:  a.StorageKey,
            FileName:    a.FileName,
            ContentType: a.ContentType,
            SizeBytes:   a.SizeBytes,
            Caption:     a.Caption,
            Url:         _urls.ResolvePublicUrl(a.StorageKey))).ToList();
    }
}
```

- [ ] **Step 4: Register in Application `DependencyInjection.cs`**

```csharp
        services.AddScoped<SvcRecordEntryService>();
```

- [ ] **Step 5: `RecordsController.cs`**

```csharp
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nexo.Api.Attributes;
using Nexo.Application.Modules.Service;
using Nexo.Domain.Modules.Service;

namespace Nexo.Api.Controllers.Modules.Service;

/// <summary>
/// Service record entries (ORKEN SERVICE) — internal notes / history with durable attachment
/// references. Store-scoped. v1 contexts: Customer and Subject only. Append-only; DELETE is a
/// hard delete. All endpoints require an active service-family subscription.
/// </summary>
[ApiController]
[Route("api/v1/service/records")]
[Authorize]
[RequireServiceModule]
public class RecordsController : ControllerBase
{
    private readonly SvcRecordEntryService                   _service;
    private readonly IValidator<CreateSvcRecordEntryRequest> _createValidator;

    public RecordsController(
        SvcRecordEntryService service, IValidator<CreateSvcRecordEntryRequest> createValidator)
    {
        _service         = service;
        _createValidator = createValidator;
    }

    /// <summary>Lists records for one context (both contextType and contextId are required).</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<SvcRecordEntryDto>>> GetByContext(
        [FromQuery] SvcRecordContextType? contextType,
        [FromQuery] Guid? contextId,
        CancellationToken ct = default)
    {
        if (contextType is null || contextId is null || contextId == Guid.Empty)
            return BadRequest(new { error = "contextType and contextId are required." });
        if (contextType is not (SvcRecordContextType.Customer or SvcRecordContextType.Subject))
            return BadRequest(new { error = "ContextType is not supported yet. Use Customer or Subject." });

        return Ok(await _service.GetByContextAsync(contextType.Value, contextId.Value, ct));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<SvcRecordEntryDto>> GetById(Guid id, CancellationToken ct)
        => Ok(await _service.GetByIdAsync(id, ct));

    [HttpPost]
    public async Task<ActionResult<SvcRecordEntryDto>> Create(
        [FromBody] CreateSvcRecordEntryRequest request, CancellationToken ct)
    {
        await _createValidator.ValidateAndThrowAsync(request, ct);
        var dto = await _service.CreateAsync(request, ct);
        return CreatedAtAction(nameof(GetById), new { id = dto.Id }, dto);
    }

    /// <summary>Hard delete (records have no soft-delete state). Returns 204.</summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await _service.DeleteAsync(id, ct);
        return NoContent();
    }
}
```

- [ ] **Step 6: Build** → success. **Commit** — `feat(service): SvcRecordEntryService + RecordsController (PR2)`

---

## Task 9: Storage context `service-record`

**Files:**
- Modify: `nexo-backend/src/Nexo.Api/Controllers/Integrations/StorageController.cs`

- [ ] **Step 1:** Add to the `ContextPaths` dictionary:

```csharp
            ["build-daily-log"]  = "build/daily-logs",
            ["service-record"]   = "service/records",
```

- [ ] **Step 2: Build.** **Commit** — `feat(service): storage context service-record → service/records (PR2)`

---

## Task 10: Integration tests

**Files:**
- Create: `nexo-backend/tests/Nexo.IntegrationTests/Service/ServiceSubjectsTests.cs`
- Create: `nexo-backend/tests/Nexo.IntegrationTests/Service/ServiceRecordsTests.cs`
- Modify (storage assertion): `nexo-backend/tests/Nexo.UnitTests/Integrations/StorageControllerTests.cs` — add a case asserting `service-record` maps under `service/records` (follow the file's existing style; if it tests `ContextPaths` indirectly via upload, add an analogous case).

Shared helper to create a real customer for the current (admin) tenant via the API:

```csharp
private static async Task<Guid> CreateCustomerAsync(HttpClient client)
{
    var resp = await client.PostAsJsonAsync("/api/customers", new
    {
        personType = "Individual",
        name = "Tutor " + Guid.NewGuid().ToString("N")[..8],
        documentType = "CPF",
        documentNumber = Guid.NewGuid().ToString("N")[..11],
    });
    resp.StatusCode.Should().Be(HttpStatusCode.Created);
    return (await resp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
}
```

- [ ] **Step 1: `ServiceSubjectsTests.cs`** — covers: gate (403 no-service via `clara.boutique`, 200 with service), full lifecycle (create/get/update/deactivate/activate), filter by kind, filter by customer, blank displayName → 400, invalid kind → 400, create with foreign customer → 404, cross-tenant subject not accessible → 404.

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nexo.Domain.Entities;
using Nexo.Domain.Modules.Service;
using Nexo.Infrastructure.Persistence;
using Nexo.IntegrationTests.Common;
using Nexo.IntegrationTests.Helpers;
using Xunit;

namespace Nexo.IntegrationTests.Service;

[Collection("Integration")]
public class ServiceSubjectsTests
{
    private readonly TestWebApplicationFactory _factory;
    public ServiceSubjectsTests(TestWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Subjects_forbidden_without_service_module()
    {
        var client = await AuthClientFactory.LoginAsync(_factory, "clara.boutique", "boutique@123");
        (await client.GetAsync("/api/v1/service/subjects")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Subjects_accessible_with_service_module()
    {
        var client = await AuthClientFactory.LoginAsAdminAsync(_factory);
        (await client.GetAsync("/api/v1/service/subjects")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Subject_create_get_update_deactivate_activate()
    {
        var client = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var customerId = await CreateCustomerAsync(client);

        var createResp = await client.PostAsJsonAsync("/api/v1/service/subjects", new
        {
            customerId,
            kind = "Pet",
            displayName = "Rex",
            metadataJson = "{\"species\":\"dog\",\"breed\":\"shih-tzu\"}",
            notes = "friendly",
        });
        createResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await createResp.Content.ReadFromJsonAsync<JsonElement>();
        var id = created.GetProperty("id").GetGuid();
        created.GetProperty("kind").GetString().Should().Be("Pet");
        created.GetProperty("customerId").GetGuid().Should().Be(customerId);
        created.GetProperty("isActive").GetBoolean().Should().BeTrue();

        (await client.GetAsync($"/api/v1/service/subjects/{id}")).StatusCode.Should().Be(HttpStatusCode.OK);

        var updateResp = await client.PutAsJsonAsync($"/api/v1/service/subjects/{id}", new
        {
            kind = "Other",
            displayName = "Rex II",
            metadataJson = "{\"note\":\"older\"}",
            notes = "senior",
        });
        updateResp.StatusCode.Should().Be(HttpStatusCode.OK);
        (await updateResp.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("displayName").GetString().Should().Be("Rex II");

        var deact = await client.PostAsync($"/api/v1/service/subjects/{id}/deactivate", null);
        (await deact.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("isActive").GetBoolean().Should().BeFalse();
        var act = await client.PostAsync($"/api/v1/service/subjects/{id}/activate", null);
        (await act.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("isActive").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Subject_list_filters_by_customer_and_kind()
    {
        var client = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var c1 = await CreateCustomerAsync(client);
        var c2 = await CreateCustomerAsync(client);
        await client.PostAsJsonAsync("/api/v1/service/subjects", new { customerId = c1, kind = "Pet", displayName = "A" });
        await client.PostAsJsonAsync("/api/v1/service/subjects", new { customerId = c1, kind = "Vehicle", displayName = "B" });
        await client.PostAsJsonAsync("/api/v1/service/subjects", new { customerId = c2, kind = "Pet", displayName = "C" });

        var byCustomer = await client.GetFromJsonAsync<JsonElement>($"/api/v1/service/subjects?customerId={c1}");
        byCustomer.EnumerateArray().Should().HaveCount(2);

        var byKind = await client.GetFromJsonAsync<JsonElement>($"/api/v1/service/subjects?customerId={c1}&kind=Vehicle");
        byKind.EnumerateArray().Should().ContainSingle()
            .Which.GetProperty("displayName").GetString().Should().Be("B");
    }

    [Fact]
    public async Task Subject_create_with_blank_display_name_returns_400()
    {
        var client = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var customerId = await CreateCustomerAsync(client);
        var resp = await client.PostAsJsonAsync("/api/v1/service/subjects",
            new { customerId, kind = "Pet", displayName = "" });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Subject_create_with_invalid_kind_returns_400()
    {
        var client = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var customerId = await CreateCustomerAsync(client);
        var resp = await client.PostAsJsonAsync("/api/v1/service/subjects",
            new { customerId, kind = "Spaceship", displayName = "X" });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Subject_create_with_foreign_customer_returns_404()
    {
        var foreignCustomerId = await SeedForeignCustomerAsync();
        var client = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var resp = await client.PostAsJsonAsync("/api/v1/service/subjects",
            new { customerId = foreignCustomerId, kind = "Pet", displayName = "Ghost" });
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Subject_from_another_tenant_is_not_accessible()
    {
        var foreignId = await SeedForeignSubjectAsync();
        await AssertSubjectExistsInDbAsync(foreignId);

        var client = await AuthClientFactory.LoginAsAdminAsync(_factory);
        (await client.GetAsync($"/api/v1/service/subjects/{foreignId}"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────
    private static async Task<Guid> CreateCustomerAsync(HttpClient client) { /* as shown above */ }

    private async Task<(Guid TenantId, Guid CustomerId)> SeedForeignTenantWithCustomerAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        const string taxId = "77666555000199";
        var t = await db.Tenants.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.TaxId == taxId);
        if (t is null)
        {
            t = Tenant.Create("Subject Isolation Corp", taxId, "admin@subj-iso.test");
            db.Tenants.Add(t);
            await db.SaveChangesAsync();
        }
        var cust = await db.Customers.IgnoreQueryFilters().FirstOrDefaultAsync(c => c.TenantId == t.Id);
        if (cust is null)
        {
            cust = Customer.Create(t.Id, Nexo.Domain.Enums.PersonType.Individual, "Foreign Tutor",
                Nexo.Domain.Enums.DocumentType.CPF, Guid.NewGuid().ToString("N")[..11]);
            db.Customers.Add(cust);
            await db.SaveChangesAsync();
        }
        return (t.Id, cust.Id);
    }

    private async Task<Guid> SeedForeignCustomerAsync()
        => (await SeedForeignTenantWithCustomerAsync()).CustomerId;

    private async Task<Guid> SeedForeignSubjectAsync()
    {
        var (tenantId, customerId) = await SeedForeignTenantWithCustomerAsync();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        var subj = SvcSubject.Create(tenantId, customerId, SvcSubjectKind.Pet, "Foreign Rex");
        db.SvcSubjects.Add(subj);
        await db.SaveChangesAsync();
        return subj.Id;
    }

    private async Task AssertSubjectExistsInDbAsync(Guid id)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        (await db.SvcSubjects.IgnoreQueryFilters().AnyAsync(s => s.Id == id))
            .Should().BeTrue("seeding failed — test would be vacuously 404");
    }
}
```
(Inline the `CreateCustomerAsync` body shown above into the file.)

- [ ] **Step 2: `ServiceRecordsTests.cs`** — covers: create record for Customer (201), create for Subject (201), list by context, get by id, delete (204) then get → 404, "text or attachments required" → 400, attachment-only record stores storageKey and resolves no URL when storage disabled, reserved context type (Appointment) → 400, foreign customer context → 404, cross-tenant record not accessible → 404, gate 403 without service.

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nexo.Domain.Entities;
using Nexo.Domain.Modules.Service;
using Nexo.Infrastructure.Persistence;
using Nexo.IntegrationTests.Common;
using Nexo.IntegrationTests.Helpers;
using Xunit;

namespace Nexo.IntegrationTests.Service;

[Collection("Integration")]
public class ServiceRecordsTests
{
    private readonly TestWebApplicationFactory _factory;
    public ServiceRecordsTests(TestWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Records_forbidden_without_service_module()
    {
        var client = await AuthClientFactory.LoginAsync(_factory, "clara.boutique", "boutique@123");
        var resp = await client.GetAsync(
            $"/api/v1/service/records?contextType=Customer&contextId={Guid.NewGuid()}");
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Record_create_for_customer_then_list_and_get_and_delete()
    {
        var client = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var customerId = await CreateCustomerAsync(client);

        var createResp = await client.PostAsJsonAsync("/api/v1/service/records", new
        {
            contextType = "Customer",
            contextId = customerId,
            text = "primeira visita",
        });
        createResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var id = (await createResp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var list = await client.GetFromJsonAsync<JsonElement>(
            $"/api/v1/service/records?contextType=Customer&contextId={customerId}");
        list.EnumerateArray().Select(r => r.GetProperty("id").GetGuid()).Should().Contain(id);

        (await client.GetAsync($"/api/v1/service/records/{id}")).StatusCode.Should().Be(HttpStatusCode.OK);

        (await client.DeleteAsync($"/api/v1/service/records/{id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await client.GetAsync($"/api/v1/service/records/{id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Record_create_for_subject_with_attachment()
    {
        var client = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var customerId = await CreateCustomerAsync(client);
        var subjResp = await client.PostAsJsonAsync("/api/v1/service/subjects",
            new { customerId, kind = "Pet", displayName = "Rex" });
        var subjectId = (await subjResp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var createResp = await client.PostAsJsonAsync("/api/v1/service/records", new
        {
            contextType = "Subject",
            contextId = subjectId,
            attachments = new[] { new { storageKey = "tenants/x/service/records/abc.jpg", fileName = "abc.jpg", contentType = "image/jpeg", sizeBytes = 1234L, caption = "antes" } },
        });
        createResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var dto = await createResp.Content.ReadFromJsonAsync<JsonElement>();
        var att = dto.GetProperty("attachments").EnumerateArray().Single();
        att.GetProperty("storageKey").GetString().Should().Be("tenants/x/service/records/abc.jpg");
        // Storage is not configured in tests → URL resolves to null (honest "unavailable").
        att.GetProperty("url").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task Record_create_without_text_or_attachments_returns_400()
    {
        var client = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var customerId = await CreateCustomerAsync(client);
        var resp = await client.PostAsJsonAsync("/api/v1/service/records",
            new { contextType = "Customer", contextId = customerId });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Record_create_with_reserved_context_type_returns_400()
    {
        var client = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var resp = await client.PostAsJsonAsync("/api/v1/service/records",
            new { contextType = "Appointment", contextId = Guid.NewGuid(), text = "x" });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Record_create_with_foreign_customer_context_returns_404()
    {
        var foreignCustomerId = await SeedForeignCustomerAsync();
        var client = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var resp = await client.PostAsJsonAsync("/api/v1/service/records",
            new { contextType = "Customer", contextId = foreignCustomerId, text = "x" });
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Record_from_another_tenant_is_not_accessible()
    {
        var foreignId = await SeedForeignRecordAsync();
        var client = await AuthClientFactory.LoginAsAdminAsync(_factory);
        (await client.GetAsync($"/api/v1/service/records/{foreignId}"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // Helpers: CreateCustomerAsync (as in subjects tests); SeedForeignCustomerAsync and
    // SeedForeignRecordAsync follow the foreign-tenant seeding pattern (assign StoreId
    // explicitly on the StoreEntity record, mirroring ServiceFoundationTests).
}
```

For `SeedForeignRecordAsync`, mirror `ServiceFoundationTests.SeedForeignTenantProfessionalAsync`: create/find foreign tenant + store, build `SvcRecordEntry.Create(tenantId, SvcRecordContextType.Customer, someGuid, someAuthorGuid, "foreign", null)`, set `db.Entry(entry).Property("StoreId").CurrentValue = storeBId`, save, return id.

- [ ] **Step 3: Run the new tests** — `dotnet test nexo-backend/tests/Nexo.IntegrationTests --filter "ServiceSubjectsTests|ServiceRecordsTests"`. Fix until green.
- [ ] **Step 4: Commit** — `test(service): integration coverage for subjects + records (PR2)`

---

## Task 11: Full verification + Sonar self-check + PR

- [ ] **Step 1: Build** — `cd nexo-backend && dotnet build Nexo.sln` → 0 errors/warnings.
- [ ] **Step 2: Unit tests** — `dotnet test tests/Nexo.UnitTests` → all green (incl. new SvcSubject/SvcRecordEntry).
- [ ] **Step 3: Integration tests** — `dotnet test tests/Nexo.IntegrationTests` → all green (Docker/Testcontainers required).
- [ ] **Step 4: Re-confirm migration is additive** — re-read the `Up()` once more; confirm only `CreateTable(svc_subjects)`, `CreateTable(svc_record_entries)`, `CreateIndex(...)`.
- [ ] **Step 5: Sonar self-check** — scan new code for duplicated blocks (the EF-config refactor is the single source of truth; controllers/services follow PR1 shapes without copy-paste). Confirm migrations remain excluded from CPD (SonarCloud UI; no in-repo `sonar-project.properties` reintroduced).
- [ ] **Step 6: Confirm untouched** — `git diff --name-only origin/master` lists ONLY Service/Storage/test files + the migration. No Auth, Redis, Stripe, SuperAdmin, Build files. No frontend route/screen.
- [ ] **Step 7: Push + open PR (no merge)** — `git push -u origin feature/orken-service-v1-pr2-subjects-records` then `gh pr create --base master --title "feat(service): PR2 — subjects + records" --body "…"`. Do NOT merge or deploy.

---

## Self-review (spec coverage)

- Entities `SvcSubject` (T1), `SvcRecordEntry` (T2) ✔
- Subjects CRUD + activate/deactivate endpoints (T7); Records list/create/get/delete (T8) ✔
- Tenant/store isolation via global query filter (configs T3, repos T5); cross-tenant blocked tests (T10) ✔
- `RequireServiceModule` gate on both controllers (T7/T8); 403/200 tests (T10) ✔
- Storage `service-record` → `service/records` (T9) + assertion (T10) ✔
- Additive migration, proven CreateTable/CreateIndex only (T4, T11-S4) ✔
- Attachments persist durable fields only; URL resolved at read (T8 service + DTO) ✔
- Reserved context types rejected (validator T8; test T10) ✔
- Validations: blank name, invalid kind, customer-exists, text-or-attachments, malformed metadata JSON ✔
- No Auth/Redis/Stripe/SuperAdmin/Build touched; no fake frontend (T11-S6) ✔

## Risks

1. **EF-config refactor drift** — sharing the mapping core could, if done wrong, alter the PR1 `svc_professionals`/`svc_catalog_items` model and leak into the migration. Mitigated by `has-pending-model-changes` (T3-S2) and migration inspection (T4-S2). If drift appears, revert to leaving `ConfigureStoreScopedSvcEntity` fully intact and accept minor duplication in the two new helpers.
2. **Integration tests need Docker** (Testcontainers). If unavailable in the run environment, unit tests + build still validate domain/wiring; note it in the report.
3. **`ContextId` has no DB FK** (polymorphic) — integrity is app-enforced. Acceptable and documented; matches the spec's generic-subject design.
4. **Attachment URL is null in tests** (storage unconfigured) — asserted as the honest behavior, mirroring Build.
