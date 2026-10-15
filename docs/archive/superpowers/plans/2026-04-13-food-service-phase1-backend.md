# Orken Food Service — Phase 1: Backend Foundation

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add all backend infrastructure for the Food Service module — StoreId isolation, modifier groups, FoodServiceSettings, updated order schema with couvert/service fee, SignalR hub, and updated integration tests.

**Architecture:** All restaurante entities (RestArea, RestTable, RestOrder, RestRecipeCard) migrate from `TenantEntity` to `StoreEntity` for automatic store-scoped query filtering. New entities (ProductModifierGroup, ProductModifier, RestOrderItemModifier, FoodServiceSettings) are added. OrderService is updated to handle OrderType, modifiers, couvert, and service fee. A SignalR hub (`RestaurantHub`) is added as a real-time event bus — all OrderService mutations emit events after DB commit.

**Tech Stack:** .NET 8, ASP.NET Core, EF Core 8, PostgreSQL, Microsoft.AspNetCore.SignalR (built-in)

**Source of truth:** `docs/superpowers/specs/2026-04-12-orken-food-service-design.md`

---

## File Map

### New Files
| File | Purpose |
|------|---------|
| `nexo-backend/src/Nexo.Domain/Modules/Restaurante/RestOrderType.cs` | OrderType enum |
| `nexo-backend/src/Nexo.Domain/Modules/Restaurante/ProductModifierGroup.cs` | Modifier group entity |
| `nexo-backend/src/Nexo.Domain/Modules/Restaurante/ProductModifier.cs` | Modifier option entity |
| `nexo-backend/src/Nexo.Domain/Modules/Restaurante/RestOrderItemModifier.cs` | Snapshot of applied modifier |
| `nexo-backend/src/Nexo.Domain/Modules/Restaurante/FoodServiceSettings.cs` | Per-store operational config |
| `nexo-backend/src/Nexo.Infrastructure/Persistence/Configurations/Modules/Restaurante/ProductModifierGroupConfiguration.cs` | EF config |
| `nexo-backend/src/Nexo.Infrastructure/Persistence/Configurations/Modules/Restaurante/ProductModifierConfiguration.cs` | EF config |
| `nexo-backend/src/Nexo.Infrastructure/Persistence/Configurations/Modules/Restaurante/RestOrderItemModifierConfiguration.cs` | EF config |
| `nexo-backend/src/Nexo.Infrastructure/Persistence/Configurations/Modules/Restaurante/FoodServiceSettingsConfiguration.cs` | EF config |
| `nexo-backend/src/Nexo.Application/Modules/Restaurante/Interfaces/IModifierGroupRepository.cs` | Repository interface |
| `nexo-backend/src/Nexo.Application/Modules/Restaurante/Interfaces/IFoodServiceSettingsRepository.cs` | Repository interface |
| `nexo-backend/src/Nexo.Application/Modules/Restaurante/ModifierGroupService.cs` | Modifier CRUD service |
| `nexo-backend/src/Nexo.Application/Modules/Restaurante/FoodServiceSettingsService.cs` | Settings service |
| `nexo-backend/src/Nexo.Infrastructure/Repositories/Modules/Restaurante/ModifierGroupRepository.cs` | Repository impl |
| `nexo-backend/src/Nexo.Infrastructure/Repositories/Modules/Restaurante/FoodServiceSettingsRepository.cs` | Repository impl |
| `nexo-backend/src/Nexo.Api/Controllers/Modules/Restaurante/ModifierGroupsController.cs` | HTTP endpoints |
| `nexo-backend/src/Nexo.Api/Controllers/Modules/Restaurante/FoodServiceSettingsController.cs` | HTTP endpoints |
| `nexo-backend/src/Nexo.Application/Common/Interfaces/IRestaurantNotificationService.cs` | SignalR abstraction |
| `nexo-backend/src/Nexo.Infrastructure/Hubs/RestaurantHub.cs` | SignalR hub |
| `nexo-backend/src/Nexo.Infrastructure/Hubs/RestaurantNotificationService.cs` | Hub context wrapper |

### Modified Files
| File | Change |
|------|--------|
| `RestArea.cs`, `RestTable.cs`, `RestOrder.cs`, `RestRecipeCard.cs` | Inherit StoreEntity instead of TenantEntity |
| `RestOrderItem.cs` | Add Modifiers backing list + ApplyModifier method + updated Total |
| `RestAreaConfiguration.cs`, `RestTableConfiguration.cs`, `RestOrderConfiguration.cs`, `RestRecipeCardConfiguration.cs` | Add store_id column + FK + updated indexes |
| `RestOrderItemConfiguration.cs` | Add Modifiers navigation |
| `RestauranteDtos.cs` | Add new request/response types |
| `OrderService.cs` | Update Open/AddItem/Close/Pay + inject SignalR |
| `IOrderRepository.cs` + `OrderRepository.cs` | Add GetOrdersByTableIdAsync + TrackModifier |
| `TablesController.cs` | Add GET /tables/{id}/orders |
| `OrdersController.cs` | Update request shapes |
| `NexoDbContext.cs` | Add new DbSets |
| `DependencyInjection.cs` (Application) | Register new services |
| `DependencyInjection.cs` (Infrastructure) | Register new repos + SignalR |
| `Program.cs` | Add SignalR + JWT query-string handler |
| `ConfirmSaleRequest` in `SalesDtos.cs` | Add `SurchargesAmount` parameter |
| `SaleService.ConfirmAsync` | Apply SurchargesAmount to total |
| `RestauranteFlowTests.cs` | Cover modifiers, couvert, service fee, store isolation |

---

## Task B-01: StoreEntity Inheritance — rest_areas, rest_tables, rest_orders, rest_recipe_cards

**Files:**
- Modify: `nexo-backend/src/Nexo.Domain/Modules/Restaurante/RestArea.cs`
- Modify: `nexo-backend/src/Nexo.Domain/Modules/Restaurante/RestTable.cs`
- Modify: `nexo-backend/src/Nexo.Domain/Modules/Restaurante/RestOrder.cs`
- Modify: `nexo-backend/src/Nexo.Domain/Modules/Restaurante/RestRecipeCard.cs`
- Modify: `nexo-backend/src/Nexo.Infrastructure/Persistence/Configurations/Modules/Restaurante/RestAreaConfiguration.cs`
- Modify: `nexo-backend/src/Nexo.Infrastructure/Persistence/Configurations/Modules/Restaurante/RestTableConfiguration.cs`
- Modify: `nexo-backend/src/Nexo.Infrastructure/Persistence/Configurations/Modules/Restaurante/RestOrderConfiguration.cs`
- Modify: `nexo-backend/src/Nexo.Infrastructure/Persistence/Configurations/Modules/Restaurante/RestRecipeCardConfiguration.cs`
- Modify: `nexo-backend/src/Nexo.Infrastructure/Persistence/NexoDbContext.cs`
- Modify: `nexo-backend/src/Nexo.Infrastructure/Repositories/Modules/Restaurante/TableRepository.cs`

- [ ] **Step 1: Change RestArea, RestTable, RestOrder, RestRecipeCard to inherit StoreEntity**

In each of the four entity files, change the base class declaration and remove the explicit Tenant FK config (the interceptor handles it):

`RestArea.cs` — change line 9:
```csharp
public class RestArea : StoreEntity   // was TenantEntity
{
    private RestArea() { }
    private RestArea(Guid tenantId) : base(tenantId) { }
    // rest of file unchanged
```

`RestTable.cs` — change line 17:
```csharp
public class RestTable : StoreEntity  // was TenantEntity
{
    private RestTable() { }
    private RestTable(Guid tenantId) : base(tenantId) { }
    // rest of file unchanged
```

`RestOrder.cs` — change line 20:
```csharp
public class RestOrder : StoreEntity  // was TenantEntity
{
    private RestOrder() { }
    private RestOrder(Guid tenantId) : base(tenantId) { }
    // rest of file unchanged
```

`RestRecipeCard.cs` — change line 19:
```csharp
public class RestRecipeCard : StoreEntity  // was TenantEntity
{
    private RestRecipeCard() { }
    private RestRecipeCard(Guid tenantId) : base(tenantId) { }
    // rest of file unchanged
```

- [ ] **Step 2: Update EF configurations to add store_id**

`RestAreaConfiguration.cs` — add after TenantId property line and update indexes:
```csharp
builder.Property(x => x.StoreId).HasColumnName("store_id").IsRequired();

builder.HasOne<Nexo.Domain.Entities.Store>()
    .WithMany()
    .HasForeignKey(x => x.StoreId)
    .HasConstraintName("fk_rest_areas_stores")
    .OnDelete(DeleteBehavior.Restrict);

// Replace: ix_rest_areas_tenant_id_name
builder.HasIndex(x => new { x.TenantId, x.StoreId, x.Name })
    .IsUnique()
    .HasDatabaseName("ix_rest_areas_tenant_store_name");
```
Remove the old `HasOne<Tenant>()` FK config line (interceptor handles it now via StoreEntity).

`RestTableConfiguration.cs` — add after TenantId:
```csharp
builder.Property(x => x.StoreId).HasColumnName("store_id").IsRequired();

builder.HasOne<Nexo.Domain.Entities.Store>()
    .WithMany()
    .HasForeignKey(x => x.StoreId)
    .HasConstraintName("fk_rest_tables_stores")
    .OnDelete(DeleteBehavior.Restrict);

// Replace: ix_rest_tables_tenant_number
builder.HasIndex(x => new { x.TenantId, x.StoreId, x.Number })
    .IsUnique()
    .HasDatabaseName("ix_rest_tables_tenant_store_number");
```

`RestOrderConfiguration.cs` — add after TenantId:
```csharp
builder.Property(x => x.StoreId).HasColumnName("store_id").IsRequired();

builder.HasOne<Nexo.Domain.Entities.Store>()
    .WithMany()
    .HasForeignKey(x => x.StoreId)
    .HasConstraintName("fk_rest_orders_stores")
    .OnDelete(DeleteBehavior.Restrict);
```

`RestRecipeCardConfiguration.cs` — add after TenantId and replace unique index:
```csharp
builder.Property(x => x.StoreId).HasColumnName("store_id").IsRequired();

builder.HasOne<Nexo.Domain.Entities.Store>()
    .WithMany()
    .HasForeignKey(x => x.StoreId)
    .HasConstraintName("fk_rest_recipe_cards_stores")
    .OnDelete(DeleteBehavior.Restrict);

// Replace: ix_rest_recipe_cards_tenant_product (was unique per tenant)
// New: unique per (tenant, store, product) — each store has its own recipe
builder.HasIndex(x => new { x.TenantId, x.StoreId, x.ProductId })
    .IsUnique()
    .HasDatabaseName("ix_rest_recipe_cards_tenant_store_product");
```

- [ ] **Step 3: Update TableRepository raw SQL query to include store_id**

`TableRepository.cs` — `GetByIdForUpdateAsync`, line 30-31:
```csharp
var tables = await _context.RestTables
    .FromSqlRaw(
        "SELECT * FROM nexo.rest_tables WHERE id = {0} AND tenant_id = {1} AND store_id = {2} FOR UPDATE",
        id, _context.CurrentTenantIdForFilter, _context.CurrentStoreIdForFilter)
    .Include(x => x.Area)
    .ToListAsync(ct);
```

- [ ] **Step 4: Generate migration**

Run from repo root:
```bash
cd nexo-backend
dotnet ef migrations add AddRestauranteStoreIsolation \
  --project src/Nexo.Infrastructure \
  --startup-project src/Nexo.Api \
  --context NexoDbContext
```

Open the generated migration file and verify it contains:
- `ADD COLUMN store_id uuid NOT NULL` for rest_areas, rest_tables, rest_orders, rest_recipe_cards
- `CREATE INDEX ix_rest_areas_tenant_store_name`
- `DROP INDEX ix_rest_recipe_cards_tenant_product` + `CREATE UNIQUE INDEX ix_rest_recipe_cards_tenant_store_product`

If the migration looks correct, apply it locally:
```bash
dotnet ef database update \
  --project src/Nexo.Infrastructure \
  --startup-project src/Nexo.Api
```
Expected output: `Done.`

- [ ] **Step 5: Build to verify no compile errors**
```bash
dotnet build src/Nexo.Api/Nexo.Api.csproj
```
Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 6: Commit**
```bash
git add nexo-backend/src/Nexo.Domain/Modules/Restaurante/RestArea.cs
git add nexo-backend/src/Nexo.Domain/Modules/Restaurante/RestTable.cs
git add nexo-backend/src/Nexo.Domain/Modules/Restaurante/RestOrder.cs
git add nexo-backend/src/Nexo.Domain/Modules/Restaurante/RestRecipeCard.cs
git add nexo-backend/src/Nexo.Infrastructure/Persistence/Configurations/Modules/Restaurante/
git add nexo-backend/src/Nexo.Infrastructure/Repositories/Modules/Restaurante/TableRepository.cs
git add nexo-backend/src/Nexo.Infrastructure/Persistence/Migrations/
git commit -m "feat(restaurante): add store isolation to all restaurante entities"
```

---

## Task B-02: ProductModifierGroup + ProductModifier Entities

**Files:**
- Create: `nexo-backend/src/Nexo.Domain/Modules/Restaurante/ProductModifierGroup.cs`
- Create: `nexo-backend/src/Nexo.Domain/Modules/Restaurante/ProductModifier.cs`
- Create: `nexo-backend/src/Nexo.Infrastructure/Persistence/Configurations/Modules/Restaurante/ProductModifierGroupConfiguration.cs`
- Create: `nexo-backend/src/Nexo.Infrastructure/Persistence/Configurations/Modules/Restaurante/ProductModifierConfiguration.cs`
- Modify: `nexo-backend/src/Nexo.Infrastructure/Persistence/NexoDbContext.cs`

- [ ] **Step 1: Create ProductModifierGroup entity**

```csharp
// nexo-backend/src/Nexo.Domain/Modules/Restaurante/ProductModifierGroup.cs
using Nexo.Domain.Common;
using Nexo.Domain.Exceptions;

namespace Nexo.Domain.Modules.Restaurante;

/// <summary>
/// Groups modifier options for a product (e.g. "Ponto da carne", "Adicionais").
/// Tenant-scoped (not store-scoped) — modifier groups belong to the product catalog.
/// IsRequired=true means the waiter must select at least one option before adding the item.
/// MaxSelections=1 → radio; MaxSelections>1 → multi-select.
/// v1: flat price adjustment only. No conditional logic.
/// </summary>
public class ProductModifierGroup : TenantEntity
{
    private ProductModifierGroup() { }
    private ProductModifierGroup(Guid tenantId) : base(tenantId) { }

    public Guid   ProductId     { get; private set; }
    public string Name          { get; private set; } = string.Empty;
    public bool   IsRequired    { get; private set; }
    public short  MaxSelections { get; private set; }
    public short  SortOrder     { get; private set; }
    public bool   IsActive      { get; private set; }

    private readonly List<ProductModifier> _modifiers = [];
    public IReadOnlyList<ProductModifier> Modifiers => _modifiers.AsReadOnly();

    public static ProductModifierGroup Create(
        Guid tenantId, Guid productId, string name,
        bool isRequired = false, short maxSelections = 1, short sortOrder = 0)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("Modifier group name is required.");
        if (maxSelections < 1)
            throw new DomainException("MaxSelections must be at least 1.");

        return new ProductModifierGroup(tenantId)
        {
            ProductId     = productId,
            Name          = name.Trim(),
            IsRequired    = isRequired,
            MaxSelections = maxSelections,
            SortOrder     = sortOrder,
            IsActive      = true,
        };
    }

    public void Update(string name, bool isRequired, short maxSelections, short sortOrder)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("Modifier group name is required.");
        Name          = name.Trim();
        IsRequired    = isRequired;
        MaxSelections = maxSelections;
        SortOrder     = sortOrder;
        SetUpdatedAt();
    }

    public void Activate()   { IsActive = true;  SetUpdatedAt(); }
    public void Deactivate() { IsActive = false; SetUpdatedAt(); }
}
```

- [ ] **Step 2: Create ProductModifier entity**

```csharp
// nexo-backend/src/Nexo.Domain/Modules/Restaurante/ProductModifier.cs
using Nexo.Domain.Common;
using Nexo.Domain.Exceptions;

namespace Nexo.Domain.Modules.Restaurante;

/// <summary>
/// A single option within a modifier group (e.g. "Ao ponto", "Extra queijo").
/// PriceAdjustment is a flat delta: +2.50, 0, or -1.00. No conditional logic.
/// </summary>
public class ProductModifier : TenantEntity
{
    private ProductModifier() { }
    private ProductModifier(Guid tenantId) : base(tenantId) { }

    public Guid    GroupId         { get; private set; }
    public string  Name            { get; private set; } = string.Empty;
    public decimal PriceAdjustment { get; private set; }
    public short   SortOrder       { get; private set; }
    public bool    IsActive        { get; private set; }

    // Navigation
    public ProductModifierGroup? Group { get; private set; }

    public static ProductModifier Create(
        Guid tenantId, Guid groupId, string name,
        decimal priceAdjustment = 0, short sortOrder = 0)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("Modifier name is required.");

        return new ProductModifier(tenantId)
        {
            GroupId         = groupId,
            Name            = name.Trim(),
            PriceAdjustment = priceAdjustment,
            SortOrder       = sortOrder,
            IsActive        = true,
        };
    }

    public void Update(string name, decimal priceAdjustment, short sortOrder)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("Modifier name is required.");
        Name            = name.Trim();
        PriceAdjustment = priceAdjustment;
        SortOrder       = sortOrder;
        SetUpdatedAt();
    }

    public void Activate()   { IsActive = true;  SetUpdatedAt(); }
    public void Deactivate() { IsActive = false; SetUpdatedAt(); }
}
```

- [ ] **Step 3: Create EF configurations**

```csharp
// ProductModifierGroupConfiguration.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nexo.Domain.Modules.Restaurante;

namespace Nexo.Infrastructure.Persistence.Configurations.Modules.Restaurante;

public class ProductModifierGroupConfiguration : IEntityTypeConfiguration<ProductModifierGroup>
{
    public void Configure(EntityTypeBuilder<ProductModifierGroup> builder)
    {
        builder.ToTable("product_modifier_groups", "nexo");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.ProductId).HasColumnName("product_id").IsRequired();
        builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(100).IsRequired();
        builder.Property(x => x.IsRequired).HasColumnName("is_required").IsRequired();
        builder.Property(x => x.MaxSelections).HasColumnName("max_selections").IsRequired();
        builder.Property(x => x.SortOrder).HasColumnName("sort_order").HasDefaultValue((short)0).IsRequired();
        builder.Property(x => x.IsActive).HasColumnName("is_active").HasDefaultValue(true).IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamptz").IsRequired();

        builder.HasMany(x => x.Modifiers)
            .WithOne(x => x.Group)
            .HasForeignKey(x => x.GroupId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Nexo.Domain.Entities.Product>()
            .WithMany()
            .HasForeignKey(x => x.ProductId)
            .HasConstraintName("fk_product_modifier_groups_products")
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Nexo.Domain.Entities.Tenant>()
            .WithMany()
            .HasForeignKey(x => x.TenantId)
            .HasConstraintName("fk_product_modifier_groups_tenants")
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => new { x.TenantId, x.ProductId })
            .HasDatabaseName("ix_product_modifier_groups_tenant_product");
    }
}
```

```csharp
// ProductModifierConfiguration.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nexo.Domain.Modules.Restaurante;

namespace Nexo.Infrastructure.Persistence.Configurations.Modules.Restaurante;

public class ProductModifierConfiguration : IEntityTypeConfiguration<ProductModifier>
{
    public void Configure(EntityTypeBuilder<ProductModifier> builder)
    {
        builder.ToTable("product_modifiers", "nexo");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.GroupId).HasColumnName("group_id").IsRequired();
        builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(100).IsRequired();
        builder.Property(x => x.PriceAdjustment).HasColumnName("price_adjustment")
            .HasColumnType("numeric(18,2)").HasDefaultValue(0m).IsRequired();
        builder.Property(x => x.SortOrder).HasColumnName("sort_order").HasDefaultValue((short)0).IsRequired();
        builder.Property(x => x.IsActive).HasColumnName("is_active").HasDefaultValue(true).IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamptz").IsRequired();

        builder.HasOne<Nexo.Domain.Entities.Tenant>()
            .WithMany()
            .HasForeignKey(x => x.TenantId)
            .HasConstraintName("fk_product_modifiers_tenants")
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => new { x.TenantId, x.GroupId })
            .HasDatabaseName("ix_product_modifiers_tenant_group");
    }
}
```

- [ ] **Step 4: Add DbSets to NexoDbContext**

In `NexoDbContext.cs`, inside the `// ── Módulo Restaurante` block, add:
```csharp
public DbSet<ProductModifierGroup> ProductModifierGroups => Set<ProductModifierGroup>();
public DbSet<ProductModifier>      ProductModifiers      => Set<ProductModifier>();
```

- [ ] **Step 5: Generate migration**
```bash
cd nexo-backend
dotnet ef migrations add AddModifierGroups \
  --project src/Nexo.Infrastructure \
  --startup-project src/Nexo.Api
dotnet ef database update --project src/Nexo.Infrastructure --startup-project src/Nexo.Api
```

- [ ] **Step 6: Build**
```bash
dotnet build src/Nexo.Api/Nexo.Api.csproj
```
Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 7: Commit**
```bash
git add nexo-backend/src/Nexo.Domain/Modules/Restaurante/ProductModifierGroup.cs
git add nexo-backend/src/Nexo.Domain/Modules/Restaurante/ProductModifier.cs
git add nexo-backend/src/Nexo.Infrastructure/Persistence/Configurations/Modules/Restaurante/
git add nexo-backend/src/Nexo.Infrastructure/Persistence/NexoDbContext.cs
git add nexo-backend/src/Nexo.Infrastructure/Persistence/Migrations/
git commit -m "feat(restaurante): add ProductModifierGroup and ProductModifier entities"
```

---

## Task B-03: RestOrderItemModifier Snapshot Entity

**Files:**
- Create: `nexo-backend/src/Nexo.Domain/Modules/Restaurante/RestOrderItemModifier.cs`
- Create: `nexo-backend/src/Nexo.Infrastructure/Persistence/Configurations/Modules/Restaurante/RestOrderItemModifierConfiguration.cs`
- Modify: `nexo-backend/src/Nexo.Domain/Modules/Restaurante/RestOrderItem.cs`
- Modify: `nexo-backend/src/Nexo.Infrastructure/Persistence/Configurations/Modules/Restaurante/RestOrderItemConfiguration.cs`
- Modify: `nexo-backend/src/Nexo.Application/Modules/Restaurante/Interfaces/IOrderRepository.cs`
- Modify: `nexo-backend/src/Nexo.Infrastructure/Repositories/Modules/Restaurante/OrderRepository.cs`
- Modify: `nexo-backend/src/Nexo.Infrastructure/Persistence/NexoDbContext.cs`

- [ ] **Step 1: Create RestOrderItemModifier entity**

```csharp
// nexo-backend/src/Nexo.Domain/Modules/Restaurante/RestOrderItemModifier.cs
using Nexo.Domain.Common;

namespace Nexo.Domain.Modules.Restaurante;

/// <summary>
/// Snapshot of a modifier applied to an order item.
/// Snapshots prevent historical data corruption when modifier prices change after ordering.
/// </summary>
public class RestOrderItemModifier : TenantEntity
{
    private RestOrderItemModifier() { }
    private RestOrderItemModifier(Guid tenantId) : base(tenantId) { }

    public Guid    OrderItemId    { get; private set; }
    public Guid    ModifierId     { get; private set; }
    public string  LabelSnapshot  { get; private set; } = string.Empty;
    public decimal PriceSnapshot  { get; private set; }

    public static RestOrderItemModifier Create(
        Guid tenantId, Guid orderItemId, Guid modifierId,
        string labelSnapshot, decimal priceSnapshot)
        => new RestOrderItemModifier(tenantId)
        {
            OrderItemId   = orderItemId,
            ModifierId    = modifierId,
            LabelSnapshot = labelSnapshot,
            PriceSnapshot = priceSnapshot,
        };
}
```

- [ ] **Step 2: Update RestOrderItem to support modifiers**

Replace the entire `RestOrderItem.cs` with:
```csharp
using Nexo.Domain.Common;
using Nexo.Domain.Exceptions;

namespace Nexo.Domain.Modules.Restaurante;

public class RestOrderItem : TenantEntity
{
    private RestOrderItem() { }
    private RestOrderItem(Guid tenantId) : base(tenantId) { }

    public Guid               OrderId   { get; private set; }
    public Guid               ProductId { get; private set; }
    public decimal            Quantity  { get; private set; }
    public decimal            UnitPrice { get; private set; }
    public decimal            Total     { get; private set; }  // updated by ApplyModifier
    public string?            Notes     { get; private set; }
    public RestOrderItemStatus Status   { get; private set; }

    public DateTime? SentToKitchenAt { get; private set; }
    public DateTime? PreparedAt      { get; private set; }
    public DateTime? DeliveredAt     { get; private set; }
    public DateTime? CancelledAt     { get; private set; }

    // Navigation
    public RestOrder?                        Order   { get; private set; }
    public Nexo.Domain.Entities.Product?     Product { get; private set; }

    private readonly List<RestOrderItemModifier> _modifiers = [];
    public IReadOnlyList<RestOrderItemModifier> Modifiers => _modifiers.AsReadOnly();

    public static RestOrderItem Create(
        Guid tenantId, Guid orderId, Guid productId,
        decimal quantity, decimal unitPrice, string? notes = null)
    {
        if (quantity <= 0)
            throw new DomainException("Order item quantity must be greater than zero.");
        if (unitPrice < 0)
            throw new DomainException("Unit price cannot be negative.");

        return new RestOrderItem(tenantId)
        {
            OrderId   = orderId,
            ProductId = productId,
            Quantity  = quantity,
            UnitPrice = unitPrice,
            Total     = quantity * unitPrice,
            Notes     = notes?.Trim(),
            Status    = RestOrderItemStatus.Pending,
        };
    }

    /// <summary>
    /// Applies a modifier snapshot to this item.
    /// Updates Total: += priceAdjustment * Quantity.
    /// Called by OrderService.AddItemAsync after item is created.
    /// </summary>
    public RestOrderItemModifier ApplyModifier(
        Guid tenantId, Guid modifierId, string labelSnapshot, decimal priceAdjustment)
    {
        var modifier = RestOrderItemModifier.Create(
            tenantId, Id, modifierId, labelSnapshot, priceAdjustment);
        _modifiers.Add(modifier);
        Total += priceAdjustment * Quantity;
        SetUpdatedAt();
        return modifier;
    }

    public void SetPreparing()
    {
        if (Status != RestOrderItemStatus.Pending)
            throw new DomainException($"Item can only go to Preparing from Pending (current: {Status}).");
        Status          = RestOrderItemStatus.Preparing;
        SentToKitchenAt = DateTime.UtcNow;
        SetUpdatedAt();
    }

    public void SetReady()
    {
        if (Status != RestOrderItemStatus.Preparing)
            throw new DomainException($"Item can only go to Ready from Preparing (current: {Status}).");
        Status     = RestOrderItemStatus.Ready;
        PreparedAt = DateTime.UtcNow;
        SetUpdatedAt();
    }

    public void SetDelivered()
    {
        if (Status != RestOrderItemStatus.Ready)
            throw new DomainException($"Item can only be Delivered from Ready (current: {Status}).");
        Status      = RestOrderItemStatus.Delivered;
        DeliveredAt = DateTime.UtcNow;
        SetUpdatedAt();
    }

    public void Cancel()
    {
        if (Status == RestOrderItemStatus.Delivered)
            throw new DomainException("Cannot cancel an already delivered item.");
        if (Status == RestOrderItemStatus.Cancelled)
            throw new DomainException("Item is already cancelled.");
        Status      = RestOrderItemStatus.Cancelled;
        CancelledAt = DateTime.UtcNow;
        SetUpdatedAt();
    }

    public bool IsActive => Status != RestOrderItemStatus.Cancelled;
}
```

- [ ] **Step 3: Create RestOrderItemModifierConfiguration**

```csharp
// RestOrderItemModifierConfiguration.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nexo.Domain.Modules.Restaurante;

namespace Nexo.Infrastructure.Persistence.Configurations.Modules.Restaurante;

public class RestOrderItemModifierConfiguration : IEntityTypeConfiguration<RestOrderItemModifier>
{
    public void Configure(EntityTypeBuilder<RestOrderItemModifier> builder)
    {
        builder.ToTable("rest_order_item_modifiers", "nexo");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.OrderItemId).HasColumnName("order_item_id").IsRequired();
        builder.Property(x => x.ModifierId).HasColumnName("modifier_id").IsRequired();
        builder.Property(x => x.LabelSnapshot).HasColumnName("label_snapshot").HasMaxLength(100).IsRequired();
        builder.Property(x => x.PriceSnapshot).HasColumnName("price_snapshot")
            .HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamptz").IsRequired();

        builder.HasOne<Nexo.Domain.Entities.Tenant>()
            .WithMany()
            .HasForeignKey(x => x.TenantId)
            .HasConstraintName("fk_rest_order_item_modifiers_tenants")
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => new { x.TenantId, x.OrderItemId })
            .HasDatabaseName("ix_rest_order_item_modifiers_item");
    }
}
```

- [ ] **Step 4: Update RestOrderItemConfiguration to add Modifiers navigation**

In `RestOrderItemConfiguration.cs`, add before the closing brace:
```csharp
builder.HasMany(x => x.Modifiers)
    .WithOne()
    .HasForeignKey(x => x.OrderItemId)
    .HasConstraintName("fk_rest_order_item_modifiers_items")
    .OnDelete(DeleteBehavior.Cascade);
```

- [ ] **Step 5: Add TrackModifier to IOrderRepository and OrderRepository**

`IOrderRepository.cs` — add:
```csharp
/// <summary>Tracks a new modifier snapshot as Added (same pattern as TrackItem).</summary>
void TrackModifier(RestOrderItemModifier modifier);
```

`OrderRepository.cs` — add:
```csharp
public void TrackModifier(RestOrderItemModifier modifier)
    => _context.Entry(modifier).State = EntityState.Added;
```

- [ ] **Step 6: Add DbSet to NexoDbContext**

```csharp
public DbSet<RestOrderItemModifier> RestOrderItemModifiers => Set<RestOrderItemModifier>();
```

Also update `GetByIdWithItemsAsync` in `OrderRepository.cs` to include modifiers:
```csharp
public async Task<RestOrder?> GetByIdWithItemsAsync(Guid id, CancellationToken ct = default)
    => await _context.RestOrders
        .Include(x => x.Table)
        .Include(x => x.Items)
            .ThenInclude(i => i.Product)
        .Include(x => x.Items)
            .ThenInclude(i => i.Modifiers)
        .FirstOrDefaultAsync(x => x.Id == id, ct);
```

- [ ] **Step 7: Generate migration and build**
```bash
cd nexo-backend
dotnet ef migrations add AddRestOrderItemModifiers \
  --project src/Nexo.Infrastructure --startup-project src/Nexo.Api
dotnet ef database update --project src/Nexo.Infrastructure --startup-project src/Nexo.Api
dotnet build src/Nexo.Api/Nexo.Api.csproj
```
Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 8: Commit**
```bash
git add nexo-backend/src/Nexo.Domain/Modules/Restaurante/RestOrderItemModifier.cs
git add nexo-backend/src/Nexo.Domain/Modules/Restaurante/RestOrderItem.cs
git add nexo-backend/src/Nexo.Infrastructure/
git commit -m "feat(restaurante): add RestOrderItemModifier snapshot entity"
```

---

## Task B-04: FoodServiceSettings Entity

**Files:**
- Create: `nexo-backend/src/Nexo.Domain/Modules/Restaurante/FoodServiceSettings.cs`
- Create: `nexo-backend/src/Nexo.Infrastructure/Persistence/Configurations/Modules/Restaurante/FoodServiceSettingsConfiguration.cs`
- Modify: `nexo-backend/src/Nexo.Infrastructure/Persistence/NexoDbContext.cs`

- [ ] **Step 1: Create FoodServiceSettings entity**

```csharp
// nexo-backend/src/Nexo.Domain/Modules/Restaurante/FoodServiceSettings.cs
using Nexo.Domain.Common;

namespace Nexo.Domain.Modules.Restaurante;

/// <summary>
/// Per-store operational configuration for the Food Service module.
/// One record per (TenantId, StoreId) — enforced by unique index.
/// Inherits StoreEntity: auto-injected StoreId, filtered by current store.
/// </summary>
public class FoodServiceSettings : StoreEntity
{
    private FoodServiceSettings() { }
    private FoodServiceSettings(Guid tenantId) : base(tenantId) { }

    public string StoreType          { get; private set; } = "restaurant"; // "restaurant"|"bar"|"pub"

    public bool     CouvertEnabled        { get; private set; }
    public decimal? CouvertPricePerPerson { get; private set; }
    public bool     CouvertAutomatic      { get; private set; }

    public bool     ServiceFeeEnabled  { get; private set; }
    public decimal? ServiceFeePercent  { get; private set; }

    /// <summary>Comma-separated enabled order types, e.g. "DineIn,Counter,Takeaway".</summary>
    public string OrderTypesEnabled    { get; private set; } = "DineIn,Counter,Takeaway";

    public static FoodServiceSettings CreateDefault(Guid tenantId)
        => new FoodServiceSettings(tenantId)
        {
            StoreType            = "restaurant",
            CouvertEnabled       = false,
            CouvertAutomatic     = false,
            ServiceFeeEnabled    = false,
            OrderTypesEnabled    = "DineIn,Counter,Takeaway",
        };

    public void Update(
        string storeType,
        bool couvertEnabled, decimal? couvertPricePerPerson, bool couvertAutomatic,
        bool serviceFeeEnabled, decimal? serviceFeePercent,
        string orderTypesEnabled)
    {
        StoreType             = storeType;
        CouvertEnabled        = couvertEnabled;
        CouvertPricePerPerson = couvertEnabled ? couvertPricePerPerson : null;
        CouvertAutomatic      = couvertEnabled && couvertAutomatic;
        ServiceFeeEnabled     = serviceFeeEnabled;
        ServiceFeePercent     = serviceFeeEnabled ? serviceFeePercent : null;
        OrderTypesEnabled     = orderTypesEnabled;
        SetUpdatedAt();
    }
}
```

- [ ] **Step 2: Create EF configuration**

```csharp
// FoodServiceSettingsConfiguration.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nexo.Domain.Modules.Restaurante;

namespace Nexo.Infrastructure.Persistence.Configurations.Modules.Restaurante;

public class FoodServiceSettingsConfiguration : IEntityTypeConfiguration<FoodServiceSettings>
{
    public void Configure(EntityTypeBuilder<FoodServiceSettings> builder)
    {
        builder.ToTable("food_service_settings", "nexo");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.StoreId).HasColumnName("store_id").IsRequired();
        builder.Property(x => x.StoreType).HasColumnName("store_type").HasMaxLength(20)
            .HasDefaultValue("restaurant").IsRequired();
        builder.Property(x => x.CouvertEnabled).HasColumnName("couvert_enabled")
            .HasDefaultValue(false).IsRequired();
        builder.Property(x => x.CouvertPricePerPerson).HasColumnName("couvert_price_per_person")
            .HasColumnType("numeric(18,2)");
        builder.Property(x => x.CouvertAutomatic).HasColumnName("couvert_automatic")
            .HasDefaultValue(false).IsRequired();
        builder.Property(x => x.ServiceFeeEnabled).HasColumnName("service_fee_enabled")
            .HasDefaultValue(false).IsRequired();
        builder.Property(x => x.ServiceFeePercent).HasColumnName("service_fee_percent")
            .HasColumnType("numeric(5,2)");
        builder.Property(x => x.OrderTypesEnabled).HasColumnName("order_types_enabled")
            .HasMaxLength(100).HasDefaultValue("DineIn,Counter,Takeaway").IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamptz").IsRequired();

        builder.HasOne<Nexo.Domain.Entities.Store>()
            .WithMany()
            .HasForeignKey(x => x.StoreId)
            .HasConstraintName("fk_food_service_settings_stores")
            .OnDelete(DeleteBehavior.Restrict);

        // One settings record per store
        builder.HasIndex(x => new { x.TenantId, x.StoreId })
            .IsUnique()
            .HasDatabaseName("ix_food_service_settings_tenant_store");
    }
}
```

- [ ] **Step 3: Add DbSet to NexoDbContext**

```csharp
public DbSet<FoodServiceSettings> FoodServiceSettings => Set<FoodServiceSettings>();
```

- [ ] **Step 4: Generate migration and build**
```bash
dotnet ef migrations add AddFoodServiceSettings \
  --project src/Nexo.Infrastructure --startup-project src/Nexo.Api
dotnet ef database update --project src/Nexo.Infrastructure --startup-project src/Nexo.Api
dotnet build src/Nexo.Api/Nexo.Api.csproj
```

- [ ] **Step 5: Commit**
```bash
git add nexo-backend/src/Nexo.Domain/Modules/Restaurante/FoodServiceSettings.cs
git add nexo-backend/src/Nexo.Infrastructure/
git commit -m "feat(restaurante): add FoodServiceSettings entity"
```

---

## Task B-05: RestOrder Schema — OrderType, PartySize, CouvertAmount, ServiceFeeAmount, nullable TableId

**Files:**
- Create: `nexo-backend/src/Nexo.Domain/Modules/Restaurante/RestOrderType.cs`
- Modify: `nexo-backend/src/Nexo.Domain/Modules/Restaurante/RestOrder.cs`
- Modify: `nexo-backend/src/Nexo.Infrastructure/Persistence/Configurations/Modules/Restaurante/RestOrderConfiguration.cs`
- Modify: `nexo-backend/src/Nexo.Application/Modules/Restaurante/RestauranteDtos.cs`

- [ ] **Step 1: Create RestOrderType enum**

```csharp
// nexo-backend/src/Nexo.Domain/Modules/Restaurante/RestOrderType.cs
namespace Nexo.Domain.Modules.Restaurante;

public enum RestOrderType
{
    DineIn,    // mesa obrigatória
    Counter,   // balcão — sem mesa
    Takeaway,  // retirada — sem mesa
    Delivery,  // entrega — sem mesa (v2)
}
```

- [ ] **Step 2: Update RestOrder.cs**

Replace the entire `RestOrder.cs` with the updated version:
```csharp
using Nexo.Domain.Common;
using Nexo.Domain.Exceptions;

namespace Nexo.Domain.Modules.Restaurante;

public class RestOrder : StoreEntity
{
    private RestOrder() { }
    private RestOrder(Guid tenantId) : base(tenantId) { }

    public int             OrderNumber     { get; private set; }
    public RestOrderStatus Status          { get; private set; }
    public RestOrderType   OrderType       { get; private set; }
    public Guid?           TableId         { get; private set; }  // null for Counter/Takeaway
    public int?            PartySize       { get; private set; }
    public Guid            WaiterId        { get; private set; }
    public Guid?           CustomerId      { get; private set; }
    public Guid?           SaleId          { get; private set; }
    public decimal         CouvertAmount   { get; private set; }
    public decimal         ServiceFeeAmount{ get; private set; }
    public string?         Notes           { get; private set; }

    public DateTime  OpenedAt    { get; private set; }
    public DateTime? ClosedAt    { get; private set; }
    public DateTime? CancelledAt { get; private set; }

    public RestTable? Table { get; private set; }

    private readonly List<RestOrderItem> _items = [];
    public IReadOnlyList<RestOrderItem> Items => _items.AsReadOnly();

    // ── Computed ──────────────────────────────────────────────────────────────
    public decimal ItemsSubtotal => _items.Where(i => i.IsActive).Sum(i => i.Total);
    public decimal Total         => ItemsSubtotal + CouvertAmount + ServiceFeeAmount;

    // Keep Subtotal for backward compatibility with existing code
    public decimal Subtotal => ItemsSubtotal;

    public IReadOnlyList<RestOrderItem> ActiveItems => _items.Where(i => i.IsActive).ToList();

    // ── Factory ───────────────────────────────────────────────────────────────
    public static RestOrder Create(
        Guid tenantId, int orderNumber,
        RestOrderType orderType, Guid? tableId,
        int? partySize, Guid waiterId,
        decimal couvertAmount = 0,
        Guid? customerId = null, string? notes = null)
    {
        if (orderType == RestOrderType.DineIn && tableId is null)
            throw new DomainException("DineIn orders require a table.");

        return new RestOrder(tenantId)
        {
            OrderNumber   = orderNumber,
            Status        = RestOrderStatus.Open,
            OrderType     = orderType,
            TableId       = tableId,
            PartySize     = partySize,
            WaiterId      = waiterId,
            CustomerId    = customerId,
            CouvertAmount = couvertAmount >= 0 ? couvertAmount : 0,
            Notes         = notes?.Trim(),
            OpenedAt      = DateTime.UtcNow,
        };
    }

    // ── Items ─────────────────────────────────────────────────────────────────
    public RestOrderItem AddItem(
        Guid tenantId, Guid productId,
        decimal quantity, decimal unitPrice, string? notes = null)
    {
        EnsureModifiable();
        var item = RestOrderItem.Create(tenantId, Id, productId, quantity, unitPrice, notes);
        _items.Add(item);
        return item;
    }

    public void CancelItem(Guid itemId)
    {
        EnsureModifiable();
        var item = _items.FirstOrDefault(x => x.Id == itemId)
            ?? throw new NotFoundException("OrderItem", itemId);
        item.Cancel();
    }

    // ── Couvert and service fee ───────────────────────────────────────────────
    public void SetPartySize(int partySize)
    {
        if (partySize <= 0) throw new DomainException("Party size must be greater than zero.");
        PartySize = partySize;
        SetUpdatedAt();
    }

    public void SetCouvert(decimal amount)
    {
        CouvertAmount = amount >= 0 ? amount : 0;
        SetUpdatedAt();
    }

    public void SetServiceFee(decimal amount)
    {
        ServiceFeeAmount = amount >= 0 ? amount : 0;
        SetUpdatedAt();
    }

    // ── State machine ─────────────────────────────────────────────────────────
    public void SetInPreparation()
    {
        if (Status != RestOrderStatus.Open)
            throw new DomainException($"Order must be Open to move to InPreparation (current: {Status}).");
        Status = RestOrderStatus.InPreparation;
        SetUpdatedAt();
    }

    public void SetReady()
    {
        if (Status is not (RestOrderStatus.Open or RestOrderStatus.InPreparation))
            throw new DomainException($"Order cannot be set to Ready from {Status}.");
        Status = RestOrderStatus.Ready;
        SetUpdatedAt();
    }

    public void Close(Guid saleId)
    {
        if (Status is RestOrderStatus.Closed or RestOrderStatus.Cancelled)
            throw new DomainException($"Cannot close an order with status {Status}.");
        if (!ActiveItems.Any())
            throw new DomainException("Cannot close an order with no active items.");
        Status   = RestOrderStatus.Closed;
        SaleId   = saleId;
        ClosedAt = DateTime.UtcNow;
        SetUpdatedAt();
    }

    public void MarkPaid()
    {
        if (Status != RestOrderStatus.Closed)
            throw new DomainException($"Cannot mark order as Paid from status {Status}.");
        Status = RestOrderStatus.Paid;
        SetUpdatedAt();
    }

    public void Cancel()
    {
        if (Status == RestOrderStatus.Closed)
            throw new DomainException("Order is already Closed. Cancel the Sale instead.");
        if (Status == RestOrderStatus.Paid)
            throw new DomainException("Order is already Paid and cannot be cancelled.");
        if (Status == RestOrderStatus.Cancelled)
            throw new DomainException("Order is already Cancelled.");
        Status      = RestOrderStatus.Cancelled;
        CancelledAt = DateTime.UtcNow;
        SetUpdatedAt();
    }

    public bool IsModifiable =>
        Status is RestOrderStatus.Open or RestOrderStatus.InPreparation or RestOrderStatus.Ready;

    private void EnsureModifiable()
    {
        if (!IsModifiable)
            throw new DomainException($"Order cannot be modified in status {Status}.");
    }
}
```

- [ ] **Step 3: Update RestOrderConfiguration.cs**

Add new column mappings after StoreId:
```csharp
builder.Property(x => x.OrderType)
    .HasColumnName("order_type")
    .HasMaxLength(20)
    .HasConversion<string>()
    .HasDefaultValue(RestOrderType.DineIn)
    .IsRequired();
builder.Property(x => x.TableId).HasColumnName("table_id");   // nullable now
builder.Property(x => x.PartySize).HasColumnName("party_size");
builder.Property(x => x.CouvertAmount).HasColumnName("couvert_amount")
    .HasColumnType("numeric(18,2)").HasDefaultValue(0m).IsRequired();
builder.Property(x => x.ServiceFeeAmount).HasColumnName("service_fee_amount")
    .HasColumnType("numeric(18,2)").HasDefaultValue(0m).IsRequired();
```

Remove the existing `builder.Property(x => x.TableId).HasColumnName("table_id").IsRequired();` line (no longer required).

Add partial unique index for "one active order per table":
```csharp
// Partial unique: at most one active order per (tenant, store, table)
// table_id IS NOT NULL filters out Counter/Takeaway orders
builder.HasIndex(x => new { x.TenantId, x.StoreId, x.TableId })
    .IsUnique()
    .HasFilter("table_id IS NOT NULL AND status NOT IN ('Closed','Paid','Cancelled')")
    .HasDatabaseName("ix_rest_orders_one_active_per_table");
```

Ignore computed properties:
```csharp
builder.Ignore(x => x.ActiveItems);
builder.Ignore(x => x.ItemsSubtotal);
builder.Ignore(x => x.Total);
builder.Ignore(x => x.Subtotal);
```

- [ ] **Step 4: Update RestauranteDtos.cs — OpenOrderRequest and OrderDto**

Replace the ORDER section in `RestauranteDtos.cs`:
```csharp
// ═══════════════════════════════════════════════════════════
// ORDER
// ═══════════════════════════════════════════════════════════

public record OpenOrderRequest(
    string       OrderType,           // "DineIn"|"Counter"|"Takeaway"
    Guid?        TableId    = null,
    int?         PartySize  = null,
    Guid?        CustomerId = null,
    string?      Notes      = null);

public record AddOrderItemRequest(
    Guid         ProductId,
    decimal      Quantity,
    string?      Notes      = null,
    List<ApplyModifierRequest>? Modifiers = null);

public record ApplyModifierRequest(Guid ModifierId);

public record UpdateOrderItemStatusRequest(string Status);

public record PayOrderRequest(
    List<PaymentInputDto> Payments,
    int?                  PartySize = null);   // set here when CouvertAutomatic=false

public record PaymentInputDto(string Method, string Type, decimal Amount, DateTime? DueDate = null);

public record OrderItemModifierDto(
    Guid   ModifierId,
    string LabelSnapshot,
    decimal PriceSnapshot);

public record OrderItemDto(
    Guid    Id, Guid ProductId, string ProductName,
    decimal Quantity, decimal UnitPrice, decimal Total,
    string  Status, string? Notes,
    IReadOnlyList<OrderItemModifierDto> Modifiers,
    DateTime? SentToKitchenAt, DateTime? PreparedAt,
    DateTime? DeliveredAt, DateTime? CancelledAt);

public record OrderDto(
    Guid    Id, int OrderNumber, string Status, string OrderType,
    Guid?   TableId, string? TableNumber,
    int?    PartySize,
    Guid    WaiterId, Guid? CustomerId, Guid? SaleId,
    decimal ItemsSubtotal, decimal CouvertAmount, decimal ServiceFeeAmount, decimal Total,
    string? Notes,
    DateTime OpenedAt, DateTime? ClosedAt, DateTime? CancelledAt,
    IReadOnlyList<OrderItemDto> Items);

public record CloseOrderResponse(
    Guid OrderId, Guid SaleId, decimal Total, string Message);
```

- [ ] **Step 5: Generate migration and build**
```bash
dotnet ef migrations add UpdateRestOrderSchema \
  --project src/Nexo.Infrastructure --startup-project src/Nexo.Api
dotnet ef database update --project src/Nexo.Infrastructure --startup-project src/Nexo.Api
dotnet build src/Nexo.Api/Nexo.Api.csproj
```
Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 6: Commit**
```bash
git add nexo-backend/src/Nexo.Domain/Modules/Restaurante/
git add nexo-backend/src/Nexo.Application/Modules/Restaurante/RestauranteDtos.cs
git add nexo-backend/src/Nexo.Infrastructure/Persistence/
git commit -m "feat(restaurante): update RestOrder schema — OrderType, PartySize, couvert, service fee"
```

---

## Task B-06: Add SurchargesAmount to SaleService + Update ConfirmSaleRequest

**Files:**
- Modify: `nexo-backend/src/Nexo.Application/Features/Sales/SalesDtos.cs` (or wherever ConfirmSaleRequest is defined)
- Modify: `nexo-backend/src/Nexo.Application/Features/Sales/SaleService.cs`

> **Why:** The Order's CouvertAmount + ServiceFeeAmount must be included in the sale total for correct cash movement recording. We add `SurchargesAmount` to `ConfirmSaleRequest` — SaleService adds it to the sale total before confirming.

- [ ] **Step 1: Locate ConfirmSaleRequest**
```bash
grep -r "ConfirmSaleRequest" nexo-backend/src --include="*.cs" -l
```

- [ ] **Step 2: Add SurchargesAmount to ConfirmSaleRequest**

Find the record definition and add the parameter:
```csharp
public record ConfirmSaleRequest(
    List<PaymentInput> Payments,
    decimal? DiscountAmount    = null,
    decimal? TaxAmount         = null,
    decimal? SurchargesAmount  = null,   // couvert + service fee (restaurant orders)
    ISet<Guid>? SkipStockProductIds = null);
```

- [ ] **Step 3: Update SaleService.ConfirmAsync to apply SurchargesAmount**

Find where the sale total is calculated in `SaleService.ConfirmAsync` and add:
```csharp
// After items subtotal is computed, before payment validation:
if (request.SurchargesAmount.HasValue && request.SurchargesAmount.Value > 0)
    sale.AddSurcharges(request.SurchargesAmount.Value);
```

Then add `AddSurcharges` to the Sale entity (or use whatever the Sale entity provides for extra charges). If Sale doesn't have this method, find how TaxAmount is applied and follow the same pattern for SurchargesAmount.

- [ ] **Step 4: Build**
```bash
dotnet build src/Nexo.Api/Nexo.Api.csproj
```
Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 5: Commit**
```bash
git add nexo-backend/src/Nexo.Application/Features/Sales/
git commit -m "feat(sales): add SurchargesAmount to ConfirmSaleRequest for restaurant orders"
```

---

## Task B-07: ModifierGroupService + Repository

**Files:**
- Create: `nexo-backend/src/Nexo.Application/Modules/Restaurante/Interfaces/IModifierGroupRepository.cs`
- Create: `nexo-backend/src/Nexo.Application/Modules/Restaurante/ModifierGroupService.cs`
- Create: `nexo-backend/src/Nexo.Infrastructure/Repositories/Modules/Restaurante/ModifierGroupRepository.cs`
- Modify: `nexo-backend/src/Nexo.Application/DependencyInjection.cs`
- Modify: `nexo-backend/src/Nexo.Infrastructure/DependencyInjection.cs`

- [ ] **Step 1: Create IModifierGroupRepository**

```csharp
// IModifierGroupRepository.cs
using Nexo.Domain.Modules.Restaurante;

namespace Nexo.Application.Modules.Restaurante.Interfaces;

public interface IModifierGroupRepository
{
    Task<IReadOnlyList<ProductModifierGroup>> GetByProductIdAsync(Guid productId, CancellationToken ct = default);
    Task<ProductModifierGroup?> GetByIdWithModifiersAsync(Guid id, CancellationToken ct = default);
    Task<ProductModifier?> GetModifierByIdAsync(Guid modifierId, CancellationToken ct = default);
    Task AddGroupAsync(ProductModifierGroup group, CancellationToken ct = default);
    void TrackModifier(ProductModifier modifier);
    Task SaveChangesAsync(CancellationToken ct = default);
}
```

- [ ] **Step 2: Create ModifierGroupService**

```csharp
// ModifierGroupService.cs
using Nexo.Application.Common.Interfaces;
using Nexo.Application.Modules.Restaurante.Interfaces;
using Nexo.Domain.Exceptions;
using Nexo.Domain.Modules.Restaurante;

namespace Nexo.Application.Modules.Restaurante;

public class ModifierGroupService
{
    private readonly IModifierGroupRepository _repo;
    private readonly ICurrentTenant           _currentTenant;

    public ModifierGroupService(IModifierGroupRepository repo, ICurrentTenant currentTenant)
    {
        _repo          = repo;
        _currentTenant = currentTenant;
    }

    public async Task<IReadOnlyList<ModifierGroupDto>> GetByProductAsync(Guid productId, CancellationToken ct = default)
    {
        var groups = await _repo.GetByProductIdAsync(productId, ct);
        return groups.Select(Map).ToList();
    }

    public async Task<ModifierGroupDto> CreateGroupAsync(CreateModifierGroupRequest req, CancellationToken ct = default)
    {
        var group = ProductModifierGroup.Create(
            _currentTenant.Id, req.ProductId, req.Name,
            req.IsRequired, req.MaxSelections, req.SortOrder);
        await _repo.AddGroupAsync(group, ct);
        await _repo.SaveChangesAsync(ct);
        return Map(group);
    }

    public async Task<ModifierGroupDto> UpdateGroupAsync(Guid groupId, UpdateModifierGroupRequest req, CancellationToken ct = default)
    {
        var group = await _repo.GetByIdWithModifiersAsync(groupId, ct)
            ?? throw new NotFoundException("ModifierGroup", groupId);
        group.Update(req.Name, req.IsRequired, req.MaxSelections, req.SortOrder);
        await _repo.SaveChangesAsync(ct);
        return Map(group);
    }

    public async Task<ModifierGroupDto> AddModifierAsync(Guid groupId, CreateModifierRequest req, CancellationToken ct = default)
    {
        var group = await _repo.GetByIdWithModifiersAsync(groupId, ct)
            ?? throw new NotFoundException("ModifierGroup", groupId);
        var modifier = ProductModifier.Create(
            _currentTenant.Id, groupId, req.Name, req.PriceAdjustment, req.SortOrder);
        _repo.TrackModifier(modifier);
        await _repo.SaveChangesAsync(ct);
        return Map(group);
    }

    public async Task<ModifierGroupDto> UpdateModifierAsync(Guid groupId, Guid modifierId, UpdateModifierRequest req, CancellationToken ct = default)
    {
        var modifier = await _repo.GetModifierByIdAsync(modifierId, ct)
            ?? throw new NotFoundException("Modifier", modifierId);
        if (modifier.GroupId != groupId)
            throw new DomainException("Modifier does not belong to this group.");
        modifier.Update(req.Name, req.PriceAdjustment, req.SortOrder);
        await _repo.SaveChangesAsync(ct);
        var group = await _repo.GetByIdWithModifiersAsync(groupId, ct)
            ?? throw new NotFoundException("ModifierGroup", groupId);
        return Map(group);
    }

    public async Task DeleteModifierAsync(Guid groupId, Guid modifierId, CancellationToken ct = default)
    {
        var modifier = await _repo.GetModifierByIdAsync(modifierId, ct)
            ?? throw new NotFoundException("Modifier", modifierId);
        if (modifier.GroupId != groupId)
            throw new DomainException("Modifier does not belong to this group.");
        modifier.Deactivate();
        await _repo.SaveChangesAsync(ct);
    }

    private static ModifierGroupDto Map(ProductModifierGroup g) => new(
        g.Id, g.ProductId, g.Name, g.IsRequired, g.MaxSelections, g.SortOrder, g.IsActive,
        g.Modifiers.Select(m => new ModifierDto(m.Id, m.Name, m.PriceAdjustment, m.SortOrder, m.IsActive)).ToList());
}
```

- [ ] **Step 3: Add new DTOs to RestauranteDtos.cs**

```csharp
// ═══════════════════════════════════════════════════════════
// MODIFIERS
// ═══════════════════════════════════════════════════════════

public record CreateModifierGroupRequest(
    Guid ProductId, string Name,
    bool IsRequired = false, short MaxSelections = 1, short SortOrder = 0);

public record UpdateModifierGroupRequest(
    string Name, bool IsRequired, short MaxSelections, short SortOrder);

public record CreateModifierRequest(
    string Name, decimal PriceAdjustment = 0, short SortOrder = 0);

public record UpdateModifierRequest(
    string Name, decimal PriceAdjustment, short SortOrder);

public record ModifierDto(
    Guid Id, string Name, decimal PriceAdjustment, short SortOrder, bool IsActive);

public record ModifierGroupDto(
    Guid Id, Guid ProductId, string Name,
    bool IsRequired, short MaxSelections, short SortOrder, bool IsActive,
    IReadOnlyList<ModifierDto> Modifiers);
```

- [ ] **Step 4: Create ModifierGroupRepository**

```csharp
// ModifierGroupRepository.cs
using Microsoft.EntityFrameworkCore;
using Nexo.Application.Modules.Restaurante.Interfaces;
using Nexo.Domain.Modules.Restaurante;
using Nexo.Infrastructure.Persistence;

namespace Nexo.Infrastructure.Repositories.Modules.Restaurante;

public class ModifierGroupRepository : IModifierGroupRepository
{
    private readonly NexoDbContext _context;
    public ModifierGroupRepository(NexoDbContext context) => _context = context;

    public async Task<IReadOnlyList<ProductModifierGroup>> GetByProductIdAsync(Guid productId, CancellationToken ct = default)
        => await _context.ProductModifierGroups
            .Include(g => g.Modifiers)
            .Where(g => g.ProductId == productId && g.IsActive)
            .OrderBy(g => g.SortOrder)
            .ToListAsync(ct);

    public async Task<ProductModifierGroup?> GetByIdWithModifiersAsync(Guid id, CancellationToken ct = default)
        => await _context.ProductModifierGroups
            .Include(g => g.Modifiers)
            .FirstOrDefaultAsync(g => g.Id == id, ct);

    public async Task<ProductModifier?> GetModifierByIdAsync(Guid modifierId, CancellationToken ct = default)
        => await _context.ProductModifiers.FirstOrDefaultAsync(m => m.Id == modifierId, ct);

    public async Task AddGroupAsync(ProductModifierGroup group, CancellationToken ct = default)
        => await _context.ProductModifierGroups.AddAsync(group, ct);

    public void TrackModifier(ProductModifier modifier)
        => _context.Entry(modifier).State = EntityState.Added;

    public async Task SaveChangesAsync(CancellationToken ct = default)
        => await _context.SaveChangesAsync(ct);
}
```

- [ ] **Step 5: Register in DI**

`Application/DependencyInjection.cs` — add:
```csharp
services.AddScoped<ModifierGroupService>();
services.AddScoped<FoodServiceSettingsService>();
```

`Infrastructure/DependencyInjection.cs` — add:
```csharp
services.AddScoped<IModifierGroupRepository, ModifierGroupRepository>();
services.AddScoped<IFoodServiceSettingsRepository, FoodServiceSettingsRepository>();
```

- [ ] **Step 6: Build**
```bash
dotnet build src/Nexo.Api/Nexo.Api.csproj
```

- [ ] **Step 7: Commit**
```bash
git add nexo-backend/src/Nexo.Application/Modules/Restaurante/
git add nexo-backend/src/Nexo.Infrastructure/Repositories/Modules/Restaurante/
git commit -m "feat(restaurante): add ModifierGroupService and repository"
```

---

## Task B-08: ModifierGroupsController

**Files:**
- Create: `nexo-backend/src/Nexo.Api/Controllers/Modules/Restaurante/ModifierGroupsController.cs`

- [ ] **Step 1: Create controller**

```csharp
// ModifierGroupsController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nexo.Api.Filters;
using Nexo.Application.Modules.Restaurante;

namespace Nexo.Api.Controllers.Modules.Restaurante;

[ApiController]
[Authorize]
[RequireModule("restaurante")]
[Route("api/restaurante/modifier-groups")]
public class ModifierGroupsController : ControllerBase
{
    private readonly ModifierGroupService _service;
    public ModifierGroupsController(ModifierGroupService service) => _service = service;

    [HttpGet]
    public async Task<IActionResult> GetByProduct([FromQuery] Guid productId, CancellationToken ct)
        => Ok(await _service.GetByProductAsync(productId, ct));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateModifierGroupRequest req, CancellationToken ct)
        => Ok(await _service.CreateGroupAsync(req, ct));

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateModifierGroupRequest req, CancellationToken ct)
        => Ok(await _service.UpdateGroupAsync(id, req, ct));

    [HttpPost("{id:guid}/modifiers")]
    public async Task<IActionResult> AddModifier(Guid id, [FromBody] CreateModifierRequest req, CancellationToken ct)
        => Ok(await _service.AddModifierAsync(id, req, ct));

    [HttpPut("{id:guid}/modifiers/{modId:guid}")]
    public async Task<IActionResult> UpdateModifier(Guid id, Guid modId, [FromBody] UpdateModifierRequest req, CancellationToken ct)
        => Ok(await _service.UpdateModifierAsync(id, modId, req, ct));

    [HttpDelete("{id:guid}/modifiers/{modId:guid}")]
    public async Task<IActionResult> DeleteModifier(Guid id, Guid modId, CancellationToken ct)
    {
        await _service.DeleteModifierAsync(id, modId, ct);
        return NoContent();
    }
}
```

- [ ] **Step 2: Build and verify routes**
```bash
dotnet build src/Nexo.Api/Nexo.Api.csproj
```

- [ ] **Step 3: Commit**
```bash
git add nexo-backend/src/Nexo.Api/Controllers/Modules/Restaurante/ModifierGroupsController.cs
git commit -m "feat(restaurante): add ModifierGroupsController"
```

---

## Task B-09: FoodServiceSettingsService + Repository + Controller

**Files:**
- Create: `nexo-backend/src/Nexo.Application/Modules/Restaurante/Interfaces/IFoodServiceSettingsRepository.cs`
- Create: `nexo-backend/src/Nexo.Application/Modules/Restaurante/FoodServiceSettingsService.cs`
- Create: `nexo-backend/src/Nexo.Infrastructure/Repositories/Modules/Restaurante/FoodServiceSettingsRepository.cs`
- Create: `nexo-backend/src/Nexo.Api/Controllers/Modules/Restaurante/FoodServiceSettingsController.cs`
- Modify: `nexo-backend/src/Nexo.Application/Modules/Restaurante/RestauranteDtos.cs`

- [ ] **Step 1: Create IFoodServiceSettingsRepository**

```csharp
// IFoodServiceSettingsRepository.cs
using Nexo.Domain.Modules.Restaurante;

namespace Nexo.Application.Modules.Restaurante.Interfaces;

public interface IFoodServiceSettingsRepository
{
    Task<FoodServiceSettings?> GetCurrentStoreAsync(CancellationToken ct = default);
    Task AddAsync(FoodServiceSettings settings, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
```

- [ ] **Step 2: Add DTOs to RestauranteDtos.cs**

```csharp
// ═══════════════════════════════════════════════════════════
// FOOD SERVICE SETTINGS
// ═══════════════════════════════════════════════════════════

public record UpdateFoodServiceSettingsRequest(
    string  StoreType,
    bool    CouvertEnabled,
    decimal? CouvertPricePerPerson,
    bool    CouvertAutomatic,
    bool    ServiceFeeEnabled,
    decimal? ServiceFeePercent,
    string  OrderTypesEnabled);

public record FoodServiceSettingsDto(
    Guid    Id,
    string  StoreType,
    bool    CouvertEnabled,
    decimal? CouvertPricePerPerson,
    bool    CouvertAutomatic,
    bool    ServiceFeeEnabled,
    decimal? ServiceFeePercent,
    string  OrderTypesEnabled);
```

- [ ] **Step 3: Create FoodServiceSettingsService**

```csharp
// FoodServiceSettingsService.cs
using Nexo.Application.Common.Interfaces;
using Nexo.Application.Modules.Restaurante.Interfaces;
using Nexo.Domain.Modules.Restaurante;

namespace Nexo.Application.Modules.Restaurante;

public class FoodServiceSettingsService
{
    private readonly IFoodServiceSettingsRepository _repo;
    private readonly ICurrentTenant                 _currentTenant;

    public FoodServiceSettingsService(
        IFoodServiceSettingsRepository repo, ICurrentTenant currentTenant)
    {
        _repo          = repo;
        _currentTenant = currentTenant;
    }

    public async Task<FoodServiceSettingsDto> GetOrCreateAsync(CancellationToken ct = default)
    {
        var settings = await _repo.GetCurrentStoreAsync(ct);
        if (settings is null)
        {
            settings = FoodServiceSettings.CreateDefault(_currentTenant.Id);
            await _repo.AddAsync(settings, ct);
            await _repo.SaveChangesAsync(ct);
        }
        return Map(settings);
    }

    public async Task<FoodServiceSettingsDto> UpdateAsync(UpdateFoodServiceSettingsRequest req, CancellationToken ct = default)
    {
        var settings = await _repo.GetCurrentStoreAsync(ct);
        if (settings is null)
        {
            settings = FoodServiceSettings.CreateDefault(_currentTenant.Id);
            await _repo.AddAsync(settings, ct);
        }
        settings.Update(
            req.StoreType, req.CouvertEnabled, req.CouvertPricePerPerson, req.CouvertAutomatic,
            req.ServiceFeeEnabled, req.ServiceFeePercent, req.OrderTypesEnabled);
        await _repo.SaveChangesAsync(ct);
        return Map(settings);
    }

    private static FoodServiceSettingsDto Map(FoodServiceSettings s) => new(
        s.Id, s.StoreType,
        s.CouvertEnabled, s.CouvertPricePerPerson, s.CouvertAutomatic,
        s.ServiceFeeEnabled, s.ServiceFeePercent,
        s.OrderTypesEnabled);
}
```

- [ ] **Step 4: Create FoodServiceSettingsRepository**

```csharp
// FoodServiceSettingsRepository.cs
using Microsoft.EntityFrameworkCore;
using Nexo.Application.Modules.Restaurante.Interfaces;
using Nexo.Domain.Modules.Restaurante;
using Nexo.Infrastructure.Persistence;

namespace Nexo.Infrastructure.Repositories.Modules.Restaurante;

public class FoodServiceSettingsRepository : IFoodServiceSettingsRepository
{
    private readonly NexoDbContext _context;
    public FoodServiceSettingsRepository(NexoDbContext context) => _context = context;

    // Global query filter on FoodServiceSettings (StoreEntity) auto-scopes to current store
    public async Task<FoodServiceSettings?> GetCurrentStoreAsync(CancellationToken ct = default)
        => await _context.FoodServiceSettings.FirstOrDefaultAsync(ct);

    public async Task AddAsync(FoodServiceSettings settings, CancellationToken ct = default)
        => await _context.FoodServiceSettings.AddAsync(settings, ct);

    public async Task SaveChangesAsync(CancellationToken ct = default)
        => await _context.SaveChangesAsync(ct);
}
```

- [ ] **Step 5: Create FoodServiceSettingsController**

```csharp
// FoodServiceSettingsController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nexo.Api.Filters;
using Nexo.Application.Modules.Restaurante;

namespace Nexo.Api.Controllers.Modules.Restaurante;

[ApiController]
[Authorize]
[RequireModule("restaurante")]
[Route("api/restaurante/settings")]
public class FoodServiceSettingsController : ControllerBase
{
    private readonly FoodServiceSettingsService _service;
    public FoodServiceSettingsController(FoodServiceSettingsService service) => _service = service;

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
        => Ok(await _service.GetOrCreateAsync(ct));

    [HttpPut]
    public async Task<IActionResult> Update([FromBody] UpdateFoodServiceSettingsRequest req, CancellationToken ct)
        => Ok(await _service.UpdateAsync(req, ct));
}
```

- [ ] **Step 6: Build**
```bash
dotnet build src/Nexo.Api/Nexo.Api.csproj
```
Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 7: Commit**
```bash
git add nexo-backend/src/Nexo.Application/Modules/Restaurante/
git add nexo-backend/src/Nexo.Infrastructure/Repositories/Modules/Restaurante/FoodServiceSettingsRepository.cs
git add nexo-backend/src/Nexo.Api/Controllers/Modules/Restaurante/FoodServiceSettingsController.cs
git commit -m "feat(restaurante): add FoodServiceSettings service, repository, and controller"
```

---

## Task B-10: Update OrderService.OpenAsync — OrderType, PartySize, Auto-Couvert

**Files:**
- Modify: `nexo-backend/src/Nexo.Application/Modules/Restaurante/OrderService.cs`
- Modify: `nexo-backend/src/Nexo.Api/Controllers/Modules/Restaurante/OrdersController.cs`

> **Key logic:** Counter/Takeaway orders skip the table lock and table uniqueness check. DineIn orders require a table. Auto-couvert is applied when `CouvertEnabled=true AND CouvertAutomatic=true` and `PartySize` is provided.

- [ ] **Step 1: Add IFoodServiceSettingsRepository to OrderService constructor**

```csharp
// In OrderService constructor, add:
private readonly IFoodServiceSettingsRepository _foodSettings;

public OrderService(
    IOrderRepository orders, ITableRepository tables,
    IRecipeCardRepository recipes, IProductRepository products,
    IStockRepository stock, SaleService saleService,
    IFoodServiceSettingsRepository foodSettings,
    IUnitOfWork uow, ICurrentTenant currentTenant, ICurrentUser currentUser)
{
    // ... existing assignments ...
    _foodSettings = foodSettings;
}
```

Also register `IFoodServiceSettingsRepository` in `Infrastructure/DependencyInjection.cs` if not done in B-09.

- [ ] **Step 2: Replace OpenAsync**

```csharp
public async Task<OrderDto> OpenAsync(OpenOrderRequest request, CancellationToken ct = default)
{
    var orderType = Enum.Parse<RestOrderType>(request.OrderType, ignoreCase: true);
    var settings  = await _foodSettings.GetCurrentStoreAsync(ct);

    // Validate PartySize when CouvertAutomatic = true
    if (settings is { CouvertEnabled: true, CouvertAutomatic: true } && request.PartySize is null)
        throw new DomainException("PartySize is required when CouvertAutomatic is enabled.");

    // Counter/Takeaway: no table, no lock
    if (orderType != RestOrderType.DineIn)
    {
        if (request.TableId.HasValue)
            throw new DomainException($"{orderType} orders do not use a table.");

        var number = await _orders.GetNextNumberAsync(ct);
        decimal couvertAmount = 0;
        if (settings is { CouvertEnabled: true, CouvertAutomatic: true } && request.PartySize.HasValue)
            couvertAmount = (settings.CouvertPricePerPerson ?? 0) * request.PartySize.Value;

        var order = RestOrder.Create(
            _currentTenant.Id, number, orderType, null,
            request.PartySize, _currentUser.UserId,
            couvertAmount, request.CustomerId, request.Notes);

        await _orders.AddAsync(order, ct);
        await _orders.SaveChangesAsync(ct);
        return Map(order);
    }

    // DineIn: require table, use SELECT FOR UPDATE
    if (request.TableId is null)
        throw new DomainException("DineIn orders require a TableId.");

    await using var tx = await _uow.BeginTransactionAsync(ct);
    try
    {
        var table = await _tables.GetByIdForUpdateAsync(request.TableId.Value, ct)
            ?? throw new NotFoundException("Table", request.TableId.Value);

        if (!table.IsActive)
            throw new DomainException("Table is inactive.");

        var existing = await _orders.GetOpenOrderForTableAsync(request.TableId.Value, ct);
        if (existing is not null)
            throw new ConflictException($"Table '{table.Number}' already has an open order (#{existing.OrderNumber}).");

        var number = await _orders.GetNextNumberAsync(ct);
        decimal couvertAmount = 0;
        if (settings is { CouvertEnabled: true, CouvertAutomatic: true } && request.PartySize.HasValue)
            couvertAmount = (settings.CouvertPricePerPerson ?? 0) * request.PartySize.Value;

        var order = RestOrder.Create(
            _currentTenant.Id, number, orderType, request.TableId,
            request.PartySize, _currentUser.UserId,
            couvertAmount, request.CustomerId, request.Notes);

        table.SetOccupied();
        await _orders.AddAsync(order, ct);
        await _orders.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Map(order);
    }
    catch { await tx.RollbackAsync(ct); throw; }
}
```

- [ ] **Step 3: Update Map method to include new fields**

```csharp
private static OrderDto Map(RestOrder o) => new(
    Id:              o.Id,
    OrderNumber:     o.OrderNumber,
    Status:          o.Status.ToString(),
    OrderType:       o.OrderType.ToString(),
    TableId:         o.TableId,
    TableNumber:     o.Table?.Number,
    PartySize:       o.PartySize,
    WaiterId:        o.WaiterId,
    CustomerId:      o.CustomerId,
    SaleId:          o.SaleId,
    ItemsSubtotal:   o.ItemsSubtotal,
    CouvertAmount:   o.CouvertAmount,
    ServiceFeeAmount:o.ServiceFeeAmount,
    Total:           o.Total,
    Notes:           o.Notes,
    OpenedAt:        o.OpenedAt,
    ClosedAt:        o.ClosedAt,
    CancelledAt:     o.CancelledAt,
    Items:           o.Items.Select(MapItem).ToList());

private static OrderItemDto MapItem(RestOrderItem i) => new(
    Id:              i.Id,
    ProductId:       i.ProductId,
    ProductName:     i.Product?.Name ?? string.Empty,
    Quantity:        i.Quantity,
    UnitPrice:       i.UnitPrice,
    Total:           i.Total,
    Status:          i.Status.ToString(),
    Notes:           i.Notes,
    Modifiers:       i.Modifiers.Select(m => new OrderItemModifierDto(m.ModifierId, m.LabelSnapshot, m.PriceSnapshot)).ToList(),
    SentToKitchenAt: i.SentToKitchenAt,
    PreparedAt:      i.PreparedAt,
    DeliveredAt:     i.DeliveredAt,
    CancelledAt:     i.CancelledAt);
```

- [ ] **Step 4: Build**
```bash
dotnet build src/Nexo.Api/Nexo.Api.csproj
```

- [ ] **Step 5: Commit**
```bash
git add nexo-backend/src/Nexo.Application/Modules/Restaurante/OrderService.cs
git commit -m "feat(restaurante): update OrderService.OpenAsync — OrderType, PartySize, auto-couvert"
```

---

## Task B-11: Update OrderService.AddItemAsync — Modifiers + Validation

**Files:**
- Modify: `nexo-backend/src/Nexo.Application/Modules/Restaurante/OrderService.cs`

> **Key logic:** Validate that all required modifier groups have at least one selection. Snapshot modifier label and price. Update item total via `item.ApplyModifier()`.

- [ ] **Step 1: Add IModifierGroupRepository to OrderService and replace AddItemAsync**

Add to constructor:
```csharp
private readonly IModifierGroupRepository _modifierGroups;
// in constructor param list add: IModifierGroupRepository modifierGroups
// in body: _modifierGroups = modifierGroups;
```

Replace `AddItemAsync`:
```csharp
public async Task<OrderDto> AddItemAsync(
    Guid orderId, AddOrderItemRequest request, CancellationToken ct = default)
{
    var order = await _orders.GetByIdWithItemsAsync(orderId, ct)
        ?? throw new NotFoundException("Order", orderId);

    var product = await _products.GetByIdAsync(request.ProductId, ct)
        ?? throw new NotFoundException("Product", request.ProductId);

    if (!product.IsActive)
        throw new DomainException($"Product '{product.Name}' is inactive.");

    // Validate required modifier groups
    var groups = await _modifierGroups.GetByProductIdAsync(product.Id, ct);
    var requestedModifierIds = (request.Modifiers ?? []).Select(m => m.ModifierId).ToHashSet();

    foreach (var group in groups.Where(g => g.IsRequired))
    {
        var hasSelection = group.Modifiers.Any(m => requestedModifierIds.Contains(m.Id));
        if (!hasSelection)
            throw new DomainException(
                $"Modifier group '{group.Name}' is required. Select at least one option.");
    }

    var item = order.AddItem(
        _currentTenant.Id, product.Id, request.Quantity, product.SalePrice, request.Notes);
    _orders.TrackItem(item);

    // Apply modifier snapshots
    foreach (var modReq in request.Modifiers ?? [])
    {
        var modifier = await _modifierGroups.GetModifierByIdAsync(modReq.ModifierId, ct)
            ?? throw new NotFoundException("Modifier", modReq.ModifierId);

        if (!modifier.IsActive)
            throw new DomainException($"Modifier '{modifier.Name}' is not active.");

        var snap = item.ApplyModifier(
            _currentTenant.Id, modifier.Id, modifier.Name, modifier.PriceAdjustment);
        _orders.TrackModifier(snap);
    }

    await _orders.SaveChangesAsync(ct);
    return Map(order);
}
```

- [ ] **Step 2: Build**
```bash
dotnet build src/Nexo.Api/Nexo.Api.csproj
```
Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 3: Commit**
```bash
git add nexo-backend/src/Nexo.Application/Modules/Restaurante/OrderService.cs
git commit -m "feat(restaurante): update AddItemAsync — modifier validation and snapshot"
```

---

## Task B-12: Update OrderService.PayAsync — Service Fee + Full Breakdown

**Files:**
- Modify: `nexo-backend/src/Nexo.Application/Modules/Restaurante/OrderService.cs`

> **Official formula:**  
> `ItemsSubtotal = Σ(item.Total)` (already includes modifier adjustments)  
> `ServiceFee = ItemsSubtotal × (ServiceFeePercent / 100)` — applied to subtotal only, never to couvert  
> `Total = ItemsSubtotal + CouvertAmount + ServiceFeeAmount`

- [ ] **Step 1: Update CancelAsync to handle nullable TableId**

In `CancelAsync`, guard the table lookup:
```csharp
if (order.TableId.HasValue)
{
    var table = await _tables.GetByIdAsync(order.TableId.Value, ct);
    table?.SetAvailable();
}
```

- [ ] **Step 2: Replace PayAsync**

```csharp
public async Task<OrderDto> PayAsync(Guid orderId, PayOrderRequest request, CancellationToken ct = default)
{
    var order = await _orders.GetByIdWithItemsAsync(orderId, ct)
        ?? throw new NotFoundException("Order", orderId);

    if (order.Status == RestOrderStatus.Paid)
        throw new ConflictException("This order has already been paid.");

    if (order.Status != RestOrderStatus.Closed)
        throw new DomainException(
            order.Status == RestOrderStatus.Cancelled
                ? "Order is cancelled."
                : $"Order must be Closed before payment (current: {order.Status}). Call /close first.");

    if (order.SaleId is null)
        throw new DomainException("Order has no linked Sale. Inconsistent state — contact support.");

    var saleDto = await _saleService.GetByIdAsync(order.SaleId.Value, ct);
    if (saleDto.Status == "Paid")
        throw new ConflictException("This order has already been paid.");

    // Fetch settings for service fee calculation
    var settings = await _foodSettings.GetCurrentStoreAsync(ct);

    // If manual couvert (CouvertAutomatic=false) and partySize provided → recalculate couvert
    if (settings is { CouvertEnabled: true, CouvertAutomatic: false } && request.PartySize.HasValue)
    {
        order.SetPartySize(request.PartySize.Value);
        var couvert = (settings.CouvertPricePerPerson ?? 0) * request.PartySize.Value;
        order.SetCouvert(couvert);
    }

    // Calculate service fee on ItemsSubtotal only
    decimal serviceFeeAmount = 0;
    if (settings is { ServiceFeeEnabled: true } && settings.ServiceFeePercent.HasValue)
        serviceFeeAmount = Math.Round(order.ItemsSubtotal * (settings.ServiceFeePercent.Value / 100m), 2);

    order.SetServiceFee(serviceFeeAmount);

    decimal expectedTotal = order.ItemsSubtotal + order.CouvertAmount + serviceFeeAmount;
    decimal paymentsTotal = request.Payments.Sum(p => p.Amount);
    if (paymentsTotal < expectedTotal)
        throw new DomainException(
            $"Payment amount ({paymentsTotal:F2}) is less than order total ({expectedTotal:F2}).");

    await using var tx = await _uow.BeginTransactionAsync(ct);
    try
    {
        var recipeProductIds = new HashSet<Guid>();
        foreach (var item in order.ActiveItems)
        {
            var recipe = await _recipes.GetByProductIdAsync(item.ProductId, ct);
            if (recipe is not null && recipe.IsActive)
                recipeProductIds.Add(item.ProductId);
        }

        var payments = request.Payments
            .Select(p => new PaymentInput(p.Method, p.Type, p.Amount, p.DueDate))
            .ToList();

        decimal surcharges = order.CouvertAmount + serviceFeeAmount;
        await _saleService.ConfirmAsync(order.SaleId.Value,
            new ConfirmSaleRequest(
                payments,
                SurchargesAmount: surcharges > 0 ? surcharges : null,
                SkipStockProductIds: recipeProductIds.Count > 0 ? recipeProductIds : null),
            ct);

        // Ingredient deduction via recipe cards
        foreach (var item in order.ActiveItems)
        {
            var recipe = await _recipes.GetByProductIdWithIngredientsAsync(item.ProductId, ct);
            if (recipe is null || !recipe.IsActive) continue;

            foreach (var ingredient in recipe.Ingredients)
            {
                var ingProduct = await _products.GetByIdAsync(ingredient.IngredientProductId, ct);
                if (ingProduct is null || !ingProduct.TrackStock) continue;

                var stockItem = await _stock.GetByProductIdAsync(ingredient.IngredientProductId, ct);
                if (stockItem is null) continue;

                var consumption = (item.Quantity / recipe.Yield) * ingredient.Quantity;
                var qtyBefore   = stockItem.CurrentQuantity;
                stockItem.ApplyMovement(-consumption);

                var movement = StockMovement.Create(
                    tenantId:          _currentTenant.Id,
                    productId:         ingredient.IngredientProductId,
                    movementType:      StockMovementType.RecipeOutput,
                    quantity:          consumption,
                    quantityBefore:    qtyBefore,
                    quantityAfter:     stockItem.CurrentQuantity,
                    createdByUserId:   _currentUser.UserId,
                    referenceType:     "Order",
                    referenceId:       order.Id,
                    notes:             $"Ficha técnica — Comanda #{order.OrderNumber} — {ingProduct.Name}",
                    costPriceSnapshot: ingProduct.CostPrice);

                await _stock.AddMovementAsync(movement, ct);
            }
        }

        // Release table (only DineIn has a table)
        if (order.TableId.HasValue)
        {
            var table = await _tables.GetByIdAsync(order.TableId.Value, ct);
            table?.SetAvailable();
        }

        order.MarkPaid();
        await _orders.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Map(order);
    }
    catch { await tx.RollbackAsync(ct); throw; }
}
```

- [ ] **Step 3: Build**
```bash
dotnet build src/Nexo.Api/Nexo.Api.csproj
```
Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 4: Commit**
```bash
git add nexo-backend/src/Nexo.Application/Modules/Restaurante/OrderService.cs
git commit -m "feat(restaurante): update PayAsync — service fee, couvert breakdown, nullable table"
```

---

## Task B-13: TablesController — GET /tables/{id}/orders

**Files:**
- Modify: `nexo-backend/src/Nexo.Application/Modules/Restaurante/Interfaces/IOrderRepository.cs`
- Modify: `nexo-backend/src/Nexo.Infrastructure/Repositories/Modules/Restaurante/OrderRepository.cs`
- Modify: `nexo-backend/src/Nexo.Api/Controllers/Modules/Restaurante/TablesController.cs`

- [ ] **Step 1: Add GetOrdersByTableIdAsync to IOrderRepository**

```csharp
Task<IReadOnlyList<RestOrder>> GetOrdersByTableIdAsync(Guid tableId, CancellationToken ct = default);
```

- [ ] **Step 2: Implement in OrderRepository**

```csharp
public async Task<IReadOnlyList<RestOrder>> GetOrdersByTableIdAsync(
    Guid tableId, CancellationToken ct = default)
    => await _context.RestOrders
        .Include(x => x.Table)
        .Include(x => x.Items).ThenInclude(i => i.Product)
        .Include(x => x.Items).ThenInclude(i => i.Modifiers)
        .Where(x => x.TableId == tableId)
        .OrderByDescending(x => x.OpenedAt)
        .ToListAsync(ct);
```

- [ ] **Step 3: Add endpoint to TablesController**

```csharp
[HttpGet("{id:guid}/orders")]
public async Task<IActionResult> GetOrders(Guid id, CancellationToken ct)
    => Ok(await _orderService.GetByTableIdAsync(id, ct));
```

Add `GetByTableIdAsync` to `OrderService`:
```csharp
public async Task<IReadOnlyList<OrderDto>> GetByTableIdAsync(Guid tableId, CancellationToken ct = default)
{
    var orders = await _orders.GetOrdersByTableIdAsync(tableId, ct);
    return orders.Select(Map).ToList();
}
```

- [ ] **Step 4: Build and commit**
```bash
dotnet build src/Nexo.Api/Nexo.Api.csproj
git add nexo-backend/src/Nexo.Application/Modules/Restaurante/
git add nexo-backend/src/Nexo.Infrastructure/Repositories/Modules/Restaurante/OrderRepository.cs
git add nexo-backend/src/Nexo.Api/Controllers/Modules/Restaurante/TablesController.cs
git commit -m "feat(restaurante): add GET /tables/{id}/orders for historical order view"
```

---

## Task B-14: SignalR Hub — CHECKPOINT ✓

**Files:**
- Create: `nexo-backend/src/Nexo.Application/Common/Interfaces/IRestaurantNotificationService.cs`
- Create: `nexo-backend/src/Nexo.Infrastructure/Hubs/RestaurantHub.cs`
- Create: `nexo-backend/src/Nexo.Infrastructure/Hubs/RestaurantNotificationService.cs`
- Modify: `nexo-backend/src/Nexo.Infrastructure/DependencyInjection.cs`
- Modify: `nexo-backend/src/Nexo.Api/Program.cs`
- Modify: `nexo-backend/src/Nexo.Application/Modules/Restaurante/OrderService.cs`

> **Checkpoint:** After this task, test the hub independently using Postman or a WebSocket client before frontend work begins.

- [ ] **Step 1: Create IRestaurantNotificationService**

```csharp
// IRestaurantNotificationService.cs
namespace Nexo.Application.Common.Interfaces;

public interface IRestaurantNotificationService
{
    Task OrderItemStatusChangedAsync(Guid orderId, Guid itemId, string newStatus, CancellationToken ct = default);
    Task NewItemAddedAsync(Guid orderId, object itemDto, CancellationToken ct = default);
    Task OrderStatusChangedAsync(Guid orderId, string newStatus, CancellationToken ct = default);
    Task TableStatusChangedAsync(Guid tableId, string newStatus, CancellationToken ct = default);
}
```

- [ ] **Step 2: Create RestaurantHub**

```csharp
// nexo-backend/src/Nexo.Infrastructure/Hubs/RestaurantHub.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Nexo.Infrastructure.Hubs;

[Authorize]
public class RestaurantHub : Hub
{
    /// <summary>Client calls this after connecting to subscribe to a store's events.</summary>
    public async Task JoinStore(string storeId)
        => await Groups.AddToGroupAsync(Context.ConnectionId, $"store:{storeId}");

    public async Task LeaveStore(string storeId)
        => await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"store:{storeId}");
}
```

- [ ] **Step 3: Create RestaurantNotificationService**

```csharp
// nexo-backend/src/Nexo.Infrastructure/Hubs/RestaurantNotificationService.cs
using Microsoft.AspNetCore.SignalR;
using Nexo.Application.Common.Interfaces;

namespace Nexo.Infrastructure.Hubs;

/// <summary>
/// Wraps IHubContext to emit restaurant events.
/// Group name: "store:{tenantId}:{storeId}" — receives all events for that store.
/// All methods fire-and-forget after DB commit (non-blocking).
/// </summary>
public class RestaurantNotificationService : IRestaurantNotificationService
{
    private readonly IHubContext<RestaurantHub> _hub;
    private readonly ICurrentTenant             _currentTenant;
    private readonly ICurrentStore              _currentStore;

    public RestaurantNotificationService(
        IHubContext<RestaurantHub> hub,
        ICurrentTenant currentTenant,
        ICurrentStore currentStore)
    {
        _hub           = hub;
        _currentTenant = currentTenant;
        _currentStore  = currentStore;
    }

    private string GroupName =>
        $"store:{_currentTenant.Id}:{_currentStore.Id}";

    public Task OrderItemStatusChangedAsync(Guid orderId, Guid itemId, string newStatus, CancellationToken ct = default)
        => _hub.Clients.Group(GroupName)
            .SendAsync("OrderItemStatusChanged",
                orderId.ToString(), itemId.ToString(), newStatus, ct);

    public Task NewItemAddedAsync(Guid orderId, object itemDto, CancellationToken ct = default)
        => _hub.Clients.Group(GroupName)
            .SendAsync("NewItemAdded", orderId.ToString(), itemDto, ct);

    public Task OrderStatusChangedAsync(Guid orderId, string newStatus, CancellationToken ct = default)
        => _hub.Clients.Group(GroupName)
            .SendAsync("OrderStatusChanged", orderId.ToString(), newStatus, ct);

    public Task TableStatusChangedAsync(Guid tableId, string newStatus, CancellationToken ct = default)
        => _hub.Clients.Group(GroupName)
            .SendAsync("TableStatusChanged", tableId.ToString(), newStatus, ct);
}
```

- [ ] **Step 4: Register SignalR in Infrastructure/DependencyInjection.cs**

Add at the end of `AddInfrastructure`:
```csharp
services.AddSignalR();
services.AddScoped<IRestaurantNotificationService, RestaurantNotificationService>();
```

- [ ] **Step 5: Configure JWT for SignalR WebSocket connections in Program.cs**

In the JWT `AddJwtBearer` options block, add `Events`:
```csharp
opts.Events = new JwtBearerEvents
{
    OnMessageReceived = ctx =>
    {
        // SignalR passes the token in the query string for WebSocket connections
        var accessToken = ctx.Request.Query["access_token"];
        var path = ctx.HttpContext.Request.Path;
        if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
            ctx.Token = accessToken;
        return Task.CompletedTask;
    }
};
```

Then map the hub after `app.UseAuthorization()`:
```csharp
app.MapHub<RestaurantHub>("/hubs/restaurant");
```

Add the `using Nexo.Infrastructure.Hubs;` at the top of Program.cs.

- [ ] **Step 6: Inject IRestaurantNotificationService into OrderService and emit events**

Add to OrderService constructor:
```csharp
private readonly IRestaurantNotificationService _notifications;
// constructor param: IRestaurantNotificationService notifications
// body: _notifications = notifications;
```

After `await _orders.SaveChangesAsync(ct)` in `AddItemAsync`, add:
```csharp
_ = _notifications.NewItemAddedAsync(order.Id, MapItem(item));
```

After `await _orders.SaveChangesAsync(ct)` in `UpdateItemStatusAsync`, add:
```csharp
_ = _notifications.OrderItemStatusChangedAsync(order.Id, itemId, item.Status.ToString());
_ = _notifications.OrderStatusChangedAsync(order.Id, order.Status.ToString());
```

After `await tx.CommitAsync(ct)` in `PayAsync`, add:
```csharp
_ = _notifications.TableStatusChangedAsync(order.TableId ?? Guid.Empty, "Available");
_ = _notifications.OrderStatusChangedAsync(order.Id, "Paid");
```

After `await tx.CommitAsync(ct)` in `OpenAsync` (DineIn branch), add:
```csharp
_ = _notifications.TableStatusChangedAsync(request.TableId!.Value, "Occupied");
```

- [ ] **Step 7: Build**
```bash
dotnet build src/Nexo.Api/Nexo.Api.csproj
```
Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 8: Manual checkpoint — test hub with Postman**

Start the API:
```bash
dotnet run --project src/Nexo.Api
```

In Postman:
1. POST `/api/auth/login` → get `accessToken`
2. Open WebSocket connection to `ws://localhost:5000/hubs/restaurant?access_token=<token>`
3. Send: `{ "protocol": "json", "version": 1 }` (SignalR handshake)
4. Invoke: `JoinStore` with your `storeId`
5. In a separate request, open a comanda via `POST /api/restaurante/orders`
6. Verify the WebSocket connection receives `OrderStatusChanged` or `TableStatusChanged`

Expected: hub emits events in real time.

- [ ] **Step 9: Commit**
```bash
git add nexo-backend/src/Nexo.Application/Common/Interfaces/IRestaurantNotificationService.cs
git add nexo-backend/src/Nexo.Infrastructure/Hubs/
git add nexo-backend/src/Nexo.Infrastructure/DependencyInjection.cs
git add nexo-backend/src/Nexo.Api/Program.cs
git add nexo-backend/src/Nexo.Application/Modules/Restaurante/OrderService.cs
git commit -m "feat(restaurante): add SignalR RestaurantHub with real-time order/table events"
```

---

## Task B-15: Update Integration Tests

**Files:**
- Modify: `nexo-backend/tests/Nexo.IntegrationTests/Restaurante/RestauranteFlowTests.cs`

- [ ] **Step 1: Update existing OpenOrderRequest calls**

Find all `new OpenOrderRequest(tableId)` calls and replace with:
```csharp
new OpenOrderRequest(OrderType: "DineIn", TableId: tableId)
```

Find all `new PayOrderRequest(payments)` and replace with:
```csharp
new PayOrderRequest(Payments: payments)
```

- [ ] **Step 2: Add modifier validation test**

```csharp
[Fact]
public async Task AddItem_WithRequiredModifierGroupMissing_Returns422()
{
    // Arrange: create product + required modifier group
    var productCode = $"BURGER-{Interlocked.Increment(ref _tableSeq)}";
    var productId   = await CreateProductAsync(productCode, salePrice: 30m);

    // Create modifier group with IsRequired=true
    var groupResp = await _client.PostAsJsonAsync("/api/restaurante/modifier-groups",
        new { ProductId = productId, Name = "Ponto da carne", IsRequired = true, MaxSelections = 1, SortOrder = 0 });
    groupResp.StatusCode.Should().Be(HttpStatusCode.OK);

    // Create table + order
    var tableNum = $"T{Interlocked.Increment(ref _tableSeq)}";
    var tableId  = await CreateTableAsync(tableNum);
    var orderResp = await _client.PostAsJsonAsync("/api/restaurante/orders",
        new { OrderType = "DineIn", TableId = tableId });
    var order = await orderResp.Content.ReadFromJsonAsync<OrderDto>();

    // Act: add item WITHOUT selecting the required group
    var addResp = await _client.PostAsJsonAsync(
        $"/api/restaurante/orders/{order!.Id}/items",
        new { ProductId = productId, Quantity = 1 });

    // Assert
    addResp.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
}
```

- [ ] **Step 3: Add couvert auto-apply test**

```csharp
[Fact]
public async Task OpenOrder_WithCouvertAutomatic_AppliesCouvertAmount()
{
    // Arrange: enable couvert in settings
    await _client.PutAsJsonAsync("/api/restaurante/settings", new
    {
        StoreType = "restaurant",
        CouvertEnabled = true, CouvertPricePerPerson = 8.00m, CouvertAutomatic = true,
        ServiceFeeEnabled = false, ServiceFeePercent = (decimal?)null,
        OrderTypesEnabled = "DineIn,Counter,Takeaway"
    });

    var tableNum = $"T{Interlocked.Increment(ref _tableSeq)}";
    var tableId  = await CreateTableAsync(tableNum);

    // Act: open DineIn order for 4 people
    var resp = await _client.PostAsJsonAsync("/api/restaurante/orders",
        new { OrderType = "DineIn", TableId = tableId, PartySize = 4 });
    resp.StatusCode.Should().Be(HttpStatusCode.OK);
    var order = await resp.Content.ReadFromJsonAsync<OrderDto>();

    // Assert: couvert = 8.00 × 4 = 32.00
    order!.CouvertAmount.Should().Be(32.00m);
    order.PartySize.Should().Be(4);

    // Cleanup: disable couvert
    await _client.PutAsJsonAsync("/api/restaurante/settings", new
    {
        StoreType = "restaurant",
        CouvertEnabled = false, CouvertPricePerPerson = (decimal?)null, CouvertAutomatic = false,
        ServiceFeeEnabled = false, ServiceFeePercent = (decimal?)null,
        OrderTypesEnabled = "DineIn,Counter,Takeaway"
    });
}
```

- [ ] **Step 4: Add full payment breakdown test**

```csharp
[Fact]
public async Task PayOrder_WithServiceFee_CalculatesCorrectTotal()
{
    // Arrange: enable 10% service fee
    await _client.PutAsJsonAsync("/api/restaurante/settings", new
    {
        StoreType = "restaurant",
        CouvertEnabled = true, CouvertPricePerPerson = 8.00m, CouvertAutomatic = true,
        ServiceFeeEnabled = true, ServiceFeePercent = 10.00m,
        OrderTypesEnabled = "DineIn,Counter,Takeaway"
    });

    var productCode = $"DISH-{Interlocked.Increment(ref _tableSeq)}";
    var productId   = await CreateProductAsync(productCode, salePrice: 60m);
    var tableNum    = $"T{Interlocked.Increment(ref _tableSeq)}";
    var tableId     = await CreateTableAsync(tableNum);

    var openResp = await _client.PostAsJsonAsync("/api/restaurante/orders",
        new { OrderType = "DineIn", TableId = tableId, PartySize = 2 });
    var order = await openResp.Content.ReadFromJsonAsync<OrderDto>();

    await _client.PostAsJsonAsync($"/api/restaurante/orders/{order!.Id}/items",
        new { ProductId = productId, Quantity = 2 });

    // close
    await _client.PostAsJsonAsync($"/api/restaurante/orders/{order.Id}/close", new { });

    // Act: pay
    // ItemsSubtotal = 2 × 60 = 120. Couvert = 8 × 2 = 16. ServiceFee = 120 × 10% = 12. Total = 148.
    var payResp = await _client.PostAsJsonAsync($"/api/restaurante/orders/{order.Id}/pay", new
    {
        Payments = new[] { new { Method = "Cash", Type = "Cash", Amount = 148.00m } }
    });
    payResp.StatusCode.Should().Be(HttpStatusCode.OK);
    var paid = await payResp.Content.ReadFromJsonAsync<OrderDto>();

    paid!.ItemsSubtotal.Should().Be(120.00m);
    paid.CouvertAmount.Should().Be(16.00m);
    paid.ServiceFeeAmount.Should().Be(12.00m);
    paid.Total.Should().Be(148.00m);
    paid.Status.Should().Be("Paid");

    // Cleanup settings
    await _client.PutAsJsonAsync("/api/restaurante/settings", new
    {
        StoreType = "restaurant", CouvertEnabled = false, CouvertPricePerPerson = (decimal?)null,
        CouvertAutomatic = false, ServiceFeeEnabled = false, ServiceFeePercent = (decimal?)null,
        OrderTypesEnabled = "DineIn,Counter,Takeaway"
    });
}
```

- [ ] **Step 5: Run integration tests**
```bash
cd nexo-backend
dotnet test tests/Nexo.IntegrationTests/ --logger "console;verbosity=normal"
```
Expected: all tests pass (green).

- [ ] **Step 6: Commit**
```bash
git add nexo-backend/tests/Nexo.IntegrationTests/Restaurante/RestauranteFlowTests.cs
git commit -m "test(restaurante): cover modifiers, couvert, service fee, store isolation"
```

---

**Phase 1 complete.** Proceed to `2026-04-13-food-service-phase2-frontend.md`.
