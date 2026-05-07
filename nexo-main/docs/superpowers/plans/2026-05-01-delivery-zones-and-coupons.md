# Delivery Zones & Coupons Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Allow restaurant owners to configure delivery zones (neighborhoods + fees) and coupons; customers on the public portal pick their zone via CEP lookup and optionally apply a coupon code.

**Architecture:** Three new domain entities (`DeliveryZone`, `Coupon`, `CouponUsage`) extend `StoreEntity`. Portal order creation resolves the zone fee and validates/applies the coupon server-side. Frontend adds CEP lookup + coupon field to `CartSheet` and two new management tabs to `PortalSetupPage`.

**Tech Stack:** C# / .NET 8, EF Core + PostgreSQL (jsonb), React + TypeScript, TanStack Query, ViaCEP public API.

---

## File Map

### New backend files
- `Nexo.Domain/Modules/Restaurante/DeliveryZone.cs`
- `Nexo.Domain/Modules/Restaurante/Coupon.cs` (includes `CouponDiscountType` enum)
- `Nexo.Domain/Modules/Restaurante/CouponUsage.cs`
- `Nexo.Infrastructure/Persistence/Configurations/Modules/Restaurante/DeliveryZoneConfiguration.cs`
- `Nexo.Infrastructure/Persistence/Configurations/Modules/Restaurante/CouponConfiguration.cs`
- `Nexo.Infrastructure/Persistence/Configurations/Modules/Restaurante/CouponUsageConfiguration.cs`
- `Nexo.Application/Modules/Restaurante/Interfaces/IDeliveryZoneRepository.cs`
- `Nexo.Application/Modules/Restaurante/Interfaces/ICouponRepository.cs`
- `Nexo.Infrastructure/Repositories/Modules/Restaurante/DeliveryZoneRepository.cs`
- `Nexo.Infrastructure/Repositories/Modules/Restaurante/CouponRepository.cs`
- `Nexo.Application/Modules/Restaurante/DeliveryZoneService.cs`
- `Nexo.Application/Modules/Restaurante/CouponService.cs`
- `Nexo.Application/Modules/Restaurante/DeliveryZoneDtos.cs`
- `Nexo.Application/Modules/Restaurante/CouponDtos.cs`
- `Nexo.Api/Controllers/Modules/Restaurante/DeliveryZonesController.cs`
- `Nexo.Api/Controllers/Modules/Restaurante/CouponsController.cs`
- `Nexo.Api/Controllers/PublicPortalController.cs` (or extend existing public controller)

### Modified backend files
- `Nexo.Domain/Modules/Restaurante/RestDeliveryOrder.cs` — add `CouponCode`, `DiscountAmount`; update `Total`
- `Nexo.Application/Modules/Restaurante/RestDeliveryOrderDtos.cs` — add `DeliveryZoneId`, `CouponCode` to `CreatePortalOrderRequest`; add `DiscountAmount` + `CouponCode` to `DeliveryOrderDto`
- `Nexo.Application/Modules/Restaurante/DeliveryOrderService.cs` — update `CreateFromPortalAsync` to resolve zone fee + apply coupon
- `Nexo.Infrastructure/Persistence/Configurations/Modules/Restaurante/RestDeliveryOrderConfiguration.cs` — add new columns
- `Nexo.Infrastructure/Persistence/NexoDbContext.cs` — add `DeliveryZones`, `Coupons`, `CouponUsages` DbSets

### New frontend files
- `src/modules/portal/types/deliveryZone.ts`
- `src/modules/portal/types/coupon.ts`
- `src/modules/restaurante/types/deliveryZone.ts`
- `src/modules/restaurante/types/coupon.ts`

### Modified frontend files
- `src/modules/portal/api/portal.api.ts` — add `getDeliveryZones`, `validateCoupon`
- `src/modules/restaurante/api/restaurante.api.ts` — add zone + coupon CRUD
- `src/modules/portal/components/CartSheet.tsx` — CEP lookup, zone selection, coupon, breakdown
- `src/modules/restaurante/pages/PortalSetupPage.tsx` — add Zonas and Cupons tabs

---

## Task 1 — Domain: DeliveryZone entity

**Files:**
- Create: `nexo-backend/src/Nexo.Domain/Modules/Restaurante/DeliveryZone.cs`

- [ ] **Step 1: Create the entity**

```csharp
// nexo-backend/src/Nexo.Domain/Modules/Restaurante/DeliveryZone.cs
using Nexo.Domain.Common;

namespace Nexo.Domain.Modules.Restaurante;

/// <summary>
/// Represents a delivery neighborhood configured by the restaurant owner.
/// Existence = active (no IsActive flag — owners delete to deactivate).
/// </summary>
public class DeliveryZone : StoreEntity
{
    private DeliveryZone() { }
    private DeliveryZone(Guid tenantId) : base(tenantId) { }

    public string  Neighborhood { get; private set; } = string.Empty;
    public decimal Fee          { get; private set; }

    public static DeliveryZone Create(Guid tenantId, string neighborhood, decimal fee)
    {
        if (string.IsNullOrWhiteSpace(neighborhood))
            throw new ArgumentException("Neighborhood is required.", nameof(neighborhood));
        if (fee < 0)
            throw new ArgumentException("Fee cannot be negative.", nameof(fee));

        return new DeliveryZone(tenantId)
        {
            Neighborhood = neighborhood.Trim(),
            Fee          = fee,
        };
    }

    public void Update(string neighborhood, decimal fee)
    {
        if (string.IsNullOrWhiteSpace(neighborhood))
            throw new ArgumentException("Neighborhood is required.", nameof(neighborhood));
        if (fee < 0)
            throw new ArgumentException("Fee cannot be negative.", nameof(fee));

        Neighborhood = neighborhood.Trim();
        Fee          = fee;
        SetUpdatedAt();
    }
}
```

- [ ] **Step 2: Commit**

```bash
cd nexo-backend
git add src/Nexo.Domain/Modules/Restaurante/DeliveryZone.cs
git commit -m "feat(restaurante): add DeliveryZone domain entity"
```

---

## Task 2 — Domain: Coupon + CouponUsage entities

**Files:**
- Create: `nexo-backend/src/Nexo.Domain/Modules/Restaurante/Coupon.cs`
- Create: `nexo-backend/src/Nexo.Domain/Modules/Restaurante/CouponUsage.cs`

- [ ] **Step 1: Create Coupon entity**

```csharp
// nexo-backend/src/Nexo.Domain/Modules/Restaurante/Coupon.cs
using Nexo.Domain.Common;
using Nexo.Domain.Exceptions;

namespace Nexo.Domain.Modules.Restaurante;

public enum CouponDiscountType { Percentage, FixedAmount, DeliveryFee }

public class Coupon : StoreEntity
{
    private Coupon() { }
    private Coupon(Guid tenantId) : base(tenantId) { }

    public string             Code           { get; private set; } = string.Empty;
    public string?            Description    { get; private set; }
    public CouponDiscountType DiscountType   { get; private set; }
    public decimal            DiscountValue  { get; private set; }
    public bool               IsActive       { get; private set; } = true;

    // Conditions (all optional)
    public decimal? MinOrderAmount        { get; private set; }  // min items subtotal
    public decimal? MinDeliveryFee        { get; private set; }
    public string?  RestrictToNeighborhoods { get; private set; } // jsonb: string[]
    public string?  RestrictToProductIds    { get; private set; } // jsonb: Guid[]
    public bool     IsFirstOrderOnly       { get; private set; }
    public string?  RestrictToCustomerPhone { get; private set; }
    public int?     MaxUses                { get; private set; }
    public int      UsedCount              { get; private set; }
    public DateTime? ValidFrom             { get; private set; }
    public DateTime? ValidUntil            { get; private set; }

    public static Coupon Create(
        Guid              tenantId,
        string            code,
        CouponDiscountType discountType,
        decimal           discountValue,
        string?           description             = null,
        decimal?          minOrderAmount          = null,
        decimal?          minDeliveryFee          = null,
        string?           restrictToNeighborhoods = null,
        string?           restrictToProductIds    = null,
        bool              isFirstOrderOnly        = false,
        string?           restrictToCustomerPhone = null,
        int?              maxUses                 = null,
        DateTime?         validFrom               = null,
        DateTime?         validUntil              = null)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new DomainException("Coupon code is required.");
        if (discountValue <= 0)
            throw new DomainException("Discount value must be positive.");
        if (discountType == CouponDiscountType.Percentage && discountValue > 100)
            throw new DomainException("Percentage discount cannot exceed 100.");

        return new Coupon(tenantId)
        {
            Code                    = code.Trim().ToUpperInvariant(),
            Description             = description?.Trim(),
            DiscountType            = discountType,
            DiscountValue           = discountValue,
            MinOrderAmount          = minOrderAmount,
            MinDeliveryFee          = minDeliveryFee,
            RestrictToNeighborhoods = restrictToNeighborhoods,
            RestrictToProductIds    = restrictToProductIds,
            IsFirstOrderOnly        = isFirstOrderOnly,
            RestrictToCustomerPhone = restrictToCustomerPhone?.Trim(),
            MaxUses                 = maxUses,
            ValidFrom               = validFrom,
            ValidUntil              = validUntil,
        };
    }

    public void Update(
        CouponDiscountType discountType,
        decimal            discountValue,
        string?            description             = null,
        decimal?           minOrderAmount          = null,
        decimal?           minDeliveryFee          = null,
        string?            restrictToNeighborhoods = null,
        string?            restrictToProductIds    = null,
        bool               isFirstOrderOnly        = false,
        string?            restrictToCustomerPhone = null,
        int?               maxUses                 = null,
        DateTime?          validFrom               = null,
        DateTime?          validUntil              = null)
    {
        if (discountValue <= 0)
            throw new DomainException("Discount value must be positive.");
        if (discountType == CouponDiscountType.Percentage && discountValue > 100)
            throw new DomainException("Percentage discount cannot exceed 100.");

        DiscountType            = discountType;
        DiscountValue           = discountValue;
        Description             = description?.Trim();
        MinOrderAmount          = minOrderAmount;
        MinDeliveryFee          = minDeliveryFee;
        RestrictToNeighborhoods = restrictToNeighborhoods;
        RestrictToProductIds    = restrictToProductIds;
        IsFirstOrderOnly        = isFirstOrderOnly;
        RestrictToCustomerPhone = restrictToCustomerPhone?.Trim();
        MaxUses                 = maxUses;
        ValidFrom               = validFrom;
        ValidUntil              = validUntil;
        SetUpdatedAt();
    }

    public void Revoke()
    {
        IsActive = false;
        SetUpdatedAt();
    }

    public void IncrementUsedCount()
    {
        UsedCount++;
        SetUpdatedAt();
    }

    /// <summary>
    /// Validates conditions and returns the discount amount to deduct.
    /// Throws DomainException with user-facing message on any violation.
    /// </summary>
    public decimal CalculateDiscount(
        decimal itemsSubtotal,
        decimal deliveryFee,
        string  customerPhone,
        string? neighborhood,
        bool    isFirstOrder)
    {
        var now = DateTime.UtcNow;

        if (!IsActive)
            throw new DomainException("Cupom inválido ou inativo.");
        if (ValidFrom.HasValue && now < ValidFrom.Value)
            throw new DomainException("Este cupom ainda não está válido.");
        if (ValidUntil.HasValue && now > ValidUntil.Value)
            throw new DomainException("Este cupom expirou.");
        if (MaxUses.HasValue && UsedCount >= MaxUses.Value)
            throw new DomainException("Este cupom atingiu o limite de usos.");
        if (MinOrderAmount.HasValue && itemsSubtotal < MinOrderAmount.Value)
            throw new DomainException($"Pedido mínimo de R$ {MinOrderAmount.Value:F2} para este cupom.");
        if (MinDeliveryFee.HasValue && deliveryFee < MinDeliveryFee.Value)
            throw new DomainException($"Taxa de entrega mínima de R$ {MinDeliveryFee.Value:F2} para este cupom.");
        if (IsFirstOrderOnly && !isFirstOrder)
            throw new DomainException("Este cupom é válido apenas para o primeiro pedido.");
        if (RestrictToCustomerPhone is not null &&
            RestrictNormalizedPhone(RestrictToCustomerPhone) != NormalizePhone(customerPhone))
            throw new DomainException("Este cupom não é válido para este cliente.");

        if (RestrictToNeighborhoods is not null)
        {
            var allowed = System.Text.Json.JsonSerializer
                .Deserialize<string[]>(RestrictToNeighborhoods) ?? [];
            if (neighborhood is null || !allowed.Contains(neighborhood, StringComparer.OrdinalIgnoreCase))
                throw new DomainException("Este cupom não é válido para o seu bairro.");
        }

        return DiscountType switch
        {
            CouponDiscountType.Percentage  => Math.Round(itemsSubtotal * DiscountValue / 100m, 2),
            CouponDiscountType.FixedAmount => Math.Min(DiscountValue, itemsSubtotal),
            CouponDiscountType.DeliveryFee => Math.Min(DiscountValue, deliveryFee),
            _ => 0m,
        };
    }

    private static string NormalizePhone(string phone)
        => new string(phone.Where(char.IsDigit).ToArray());

    private static string RestrictNormalizedPhone(string phone)
        => new string(phone.Where(char.IsDigit).ToArray());
}
```

- [ ] **Step 2: Create CouponUsage entity**

```csharp
// nexo-backend/src/Nexo.Domain/Modules/Restaurante/CouponUsage.cs
using Nexo.Domain.Common;

namespace Nexo.Domain.Modules.Restaurante;

public class CouponUsage : StoreEntity
{
    private CouponUsage() { }
    private CouponUsage(Guid tenantId) : base(tenantId) { }

    public Guid      CouponId        { get; private set; }
    public string    CustomerPhone   { get; private set; } = string.Empty;
    public Guid      DeliveryOrderId { get; private set; }
    public DateTime  UsedAt          { get; private set; }

    public static CouponUsage Create(
        Guid tenantId,
        Guid couponId,
        string customerPhone,
        Guid deliveryOrderId)
    {
        return new CouponUsage(tenantId)
        {
            CouponId        = couponId,
            CustomerPhone   = customerPhone,
            DeliveryOrderId = deliveryOrderId,
            UsedAt          = DateTime.UtcNow,
        };
    }
}
```

- [ ] **Step 3: Commit**

```bash
git add src/Nexo.Domain/Modules/Restaurante/Coupon.cs \
        src/Nexo.Domain/Modules/Restaurante/CouponUsage.cs
git commit -m "feat(restaurante): add Coupon and CouponUsage domain entities"
```

---

## Task 3 — Domain: Update RestDeliveryOrder for coupon support

**Files:**
- Modify: `nexo-backend/src/Nexo.Domain/Modules/Restaurante/RestDeliveryOrder.cs`

- [ ] **Step 1: Add fields and update Total**

In `RestDeliveryOrder.cs`, after the `DeliveryFee` property (line ~42), add:

```csharp
    public string?  CouponCode      { get; private set; }
    public decimal  DiscountAmount  { get; private set; }
```

Replace the existing `Total` computed property:
```csharp
    // OLD:
    public decimal Total => ItemsSubtotal + DeliveryFee;

    // NEW:
    public decimal Total => ItemsSubtotal + DeliveryFee - DiscountAmount;
```

Add a method to apply coupon (after `SetCustomer`):
```csharp
    public void ApplyCoupon(string code, decimal discountAmount)
    {
        CouponCode     = code;
        DiscountAmount = discountAmount >= 0 ? discountAmount : 0;
        SetUpdatedAt();
    }
```

- [ ] **Step 2: Commit**

```bash
git add src/Nexo.Domain/Modules/Restaurante/RestDeliveryOrder.cs
git commit -m "feat(restaurante): add CouponCode/DiscountAmount to RestDeliveryOrder"
```

---

## Task 4 — EF Configurations + NexoDbContext

**Files:**
- Create: `nexo-backend/src/Nexo.Infrastructure/Persistence/Configurations/Modules/Restaurante/DeliveryZoneConfiguration.cs`
- Create: `nexo-backend/src/Nexo.Infrastructure/Persistence/Configurations/Modules/Restaurante/CouponConfiguration.cs`
- Create: `nexo-backend/src/Nexo.Infrastructure/Persistence/Configurations/Modules/Restaurante/CouponUsageConfiguration.cs`
- Modify: `nexo-backend/src/Nexo.Infrastructure/Persistence/Configurations/Modules/Restaurante/RestDeliveryOrderConfiguration.cs`
- Modify: `nexo-backend/src/Nexo.Infrastructure/Persistence/NexoDbContext.cs`

- [ ] **Step 1: DeliveryZone EF config**

```csharp
// nexo-backend/src/Nexo.Infrastructure/Persistence/Configurations/Modules/Restaurante/DeliveryZoneConfiguration.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nexo.Domain.Modules.Restaurante;

namespace Nexo.Infrastructure.Persistence.Configurations.Modules.Restaurante;

public class DeliveryZoneConfiguration : IEntityTypeConfiguration<DeliveryZone>
{
    public void Configure(EntityTypeBuilder<DeliveryZone> builder)
    {
        builder.ToTable("rest_delivery_zones", "nexo");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.StoreId).HasColumnName("store_id").IsRequired();
        builder.Property(x => x.Neighborhood).HasColumnName("neighborhood").HasMaxLength(100).IsRequired();
        builder.Property(x => x.Fee).HasColumnName("fee").HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamptz").IsRequired();

        builder.HasIndex(x => new { x.TenantId, x.StoreId })
            .HasDatabaseName("ix_rest_delivery_zones_store");
        builder.HasIndex(x => new { x.StoreId, x.Neighborhood })
            .IsUnique()
            .HasDatabaseName("ix_rest_delivery_zones_store_neighborhood");

        builder.HasOne<Nexo.Domain.Entities.Tenant>()
            .WithMany().HasForeignKey(x => x.TenantId)
            .HasConstraintName("fk_rest_delivery_zones_tenants").OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Nexo.Domain.Entities.Store>()
            .WithMany().HasForeignKey(x => x.StoreId)
            .HasConstraintName("fk_rest_delivery_zones_stores").OnDelete(DeleteBehavior.Cascade);
    }
}
```

- [ ] **Step 2: Coupon EF config**

```csharp
// nexo-backend/src/Nexo.Infrastructure/Persistence/Configurations/Modules/Restaurante/CouponConfiguration.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nexo.Domain.Modules.Restaurante;

namespace Nexo.Infrastructure.Persistence.Configurations.Modules.Restaurante;

public class CouponConfiguration : IEntityTypeConfiguration<Coupon>
{
    public void Configure(EntityTypeBuilder<Coupon> builder)
    {
        builder.ToTable("rest_coupons", "nexo");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.StoreId).HasColumnName("store_id").IsRequired();
        builder.Property(x => x.Code).HasColumnName("code").HasMaxLength(50).IsRequired();
        builder.Property(x => x.Description).HasColumnName("description").HasMaxLength(200);
        builder.Property(x => x.DiscountType)
            .HasColumnName("discount_type").HasMaxLength(20).HasConversion<string>().IsRequired();
        builder.Property(x => x.DiscountValue).HasColumnName("discount_value")
            .HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(x => x.IsActive).HasColumnName("is_active").IsRequired();
        builder.Property(x => x.MinOrderAmount).HasColumnName("min_order_amount")
            .HasColumnType("numeric(18,2)");
        builder.Property(x => x.MinDeliveryFee).HasColumnName("min_delivery_fee")
            .HasColumnType("numeric(18,2)");
        builder.Property(x => x.RestrictToNeighborhoods).HasColumnName("restrict_to_neighborhoods")
            .HasColumnType("jsonb");
        builder.Property(x => x.RestrictToProductIds).HasColumnName("restrict_to_product_ids")
            .HasColumnType("jsonb");
        builder.Property(x => x.IsFirstOrderOnly).HasColumnName("is_first_order_only").IsRequired();
        builder.Property(x => x.RestrictToCustomerPhone).HasColumnName("restrict_to_customer_phone")
            .HasMaxLength(20);
        builder.Property(x => x.MaxUses).HasColumnName("max_uses");
        builder.Property(x => x.UsedCount).HasColumnName("used_count").IsRequired();
        builder.Property(x => x.ValidFrom).HasColumnName("valid_from").HasColumnType("timestamptz");
        builder.Property(x => x.ValidUntil).HasColumnName("valid_until").HasColumnType("timestamptz");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamptz").IsRequired();

        builder.HasIndex(x => new { x.StoreId, x.Code })
            .IsUnique().HasDatabaseName("ix_rest_coupons_store_code");
        builder.HasIndex(x => new { x.TenantId, x.StoreId })
            .HasDatabaseName("ix_rest_coupons_store");

        builder.HasOne<Nexo.Domain.Entities.Tenant>()
            .WithMany().HasForeignKey(x => x.TenantId)
            .HasConstraintName("fk_rest_coupons_tenants").OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Nexo.Domain.Entities.Store>()
            .WithMany().HasForeignKey(x => x.StoreId)
            .HasConstraintName("fk_rest_coupons_stores").OnDelete(DeleteBehavior.Cascade);
    }
}
```

- [ ] **Step 3: CouponUsage EF config**

```csharp
// nexo-backend/src/Nexo.Infrastructure/Persistence/Configurations/Modules/Restaurante/CouponUsageConfiguration.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nexo.Domain.Modules.Restaurante;

namespace Nexo.Infrastructure.Persistence.Configurations.Modules.Restaurante;

public class CouponUsageConfiguration : IEntityTypeConfiguration<CouponUsage>
{
    public void Configure(EntityTypeBuilder<CouponUsage> builder)
    {
        builder.ToTable("rest_coupon_usages", "nexo");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.StoreId).HasColumnName("store_id").IsRequired();
        builder.Property(x => x.CouponId).HasColumnName("coupon_id").IsRequired();
        builder.Property(x => x.CustomerPhone).HasColumnName("customer_phone").HasMaxLength(20).IsRequired();
        builder.Property(x => x.DeliveryOrderId).HasColumnName("delivery_order_id").IsRequired();
        builder.Property(x => x.UsedAt).HasColumnName("used_at").HasColumnType("timestamptz").IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamptz").IsRequired();

        builder.HasIndex(x => x.CouponId).HasDatabaseName("ix_rest_coupon_usages_coupon");
        builder.HasIndex(x => new { x.CouponId, x.CustomerPhone })
            .HasDatabaseName("ix_rest_coupon_usages_coupon_phone");

        builder.HasOne<Nexo.Domain.Entities.Tenant>()
            .WithMany().HasForeignKey(x => x.TenantId)
            .HasConstraintName("fk_rest_coupon_usages_tenants").OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Nexo.Domain.Entities.Store>()
            .WithMany().HasForeignKey(x => x.StoreId)
            .HasConstraintName("fk_rest_coupon_usages_stores").OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Coupon>()
            .WithMany().HasForeignKey(x => x.CouponId)
            .HasConstraintName("fk_rest_coupon_usages_coupons").OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<RestDeliveryOrder>()
            .WithMany().HasForeignKey(x => x.DeliveryOrderId)
            .HasConstraintName("fk_rest_coupon_usages_delivery_orders").OnDelete(DeleteBehavior.Cascade);
    }
}
```

- [ ] **Step 4: Update RestDeliveryOrder EF config**

Find `RestDeliveryOrderConfiguration.cs`. After the `delivery_address_json` property mapping, add:

```csharp
        builder.Property(x => x.CouponCode).HasColumnName("coupon_code").HasMaxLength(50);
        builder.Property(x => x.DiscountAmount).HasColumnName("discount_amount")
            .HasColumnType("numeric(18,2)").HasDefaultValue(0m).IsRequired();
```

Also ensure `Total` and `ItemsSubtotal` are in the ignored list (they already should be as computed):
```csharp
        builder.Ignore(x => x.Total);
        builder.Ignore(x => x.ItemsSubtotal);
```

- [ ] **Step 5: Add DbSets to NexoDbContext**

In `NexoDbContext.cs`, find the `// ── Restaurante` section and add:

```csharp
    public DbSet<DeliveryZone>  DeliveryZones  { get; set; } = null!;
    public DbSet<Coupon>        Coupons        { get; set; } = null!;
    public DbSet<CouponUsage>   CouponUsages   { get; set; } = null!;
```

- [ ] **Step 6: Add global query filters for new entities**

In `NexoDbContext.OnModelCreating`, find where other StoreEntity filters are applied and add (follow the same pattern as `RestDeliveryOrder`):

```csharp
        modelBuilder.Entity<DeliveryZone>()
            .HasQueryFilter(x => x.TenantId == _currentTenant.Id && x.StoreId == _currentStore.Id);
        modelBuilder.Entity<Coupon>()
            .HasQueryFilter(x => x.TenantId == _currentTenant.Id && x.StoreId == _currentStore.Id);
        modelBuilder.Entity<CouponUsage>()
            .HasQueryFilter(x => x.TenantId == _currentTenant.Id && x.StoreId == _currentStore.Id);
```

- [ ] **Step 7: Create and apply migration**

```bash
cd nexo-backend
dotnet ef migrations add AddDeliveryZonesAndCoupons \
  --project src/Nexo.Infrastructure \
  --startup-project src/Nexo.Api
dotnet ef database update \
  --project src/Nexo.Infrastructure \
  --startup-project src/Nexo.Api
```

Expected: migration file created, `rest_delivery_zones`, `rest_coupons`, `rest_coupon_usages` tables created, `coupon_code` + `discount_amount` columns added to `rest_delivery_orders`.

- [ ] **Step 8: Commit**

```bash
git add src/Nexo.Infrastructure/Persistence/Configurations/Modules/Restaurante/DeliveryZoneConfiguration.cs \
        src/Nexo.Infrastructure/Persistence/Configurations/Modules/Restaurante/CouponConfiguration.cs \
        src/Nexo.Infrastructure/Persistence/Configurations/Modules/Restaurante/CouponUsageConfiguration.cs \
        src/Nexo.Infrastructure/Persistence/Configurations/Modules/Restaurante/RestDeliveryOrderConfiguration.cs \
        src/Nexo.Infrastructure/Persistence/NexoDbContext.cs \
        src/Nexo.Infrastructure/Persistence/Migrations/
git commit -m "feat(restaurante): EF configs + migration for delivery zones and coupons"
```

---

## Task 5 — Repository interfaces + implementations

**Files:**
- Create: `nexo-backend/src/Nexo.Application/Modules/Restaurante/Interfaces/IDeliveryZoneRepository.cs`
- Create: `nexo-backend/src/Nexo.Application/Modules/Restaurante/Interfaces/ICouponRepository.cs`
- Create: `nexo-backend/src/Nexo.Infrastructure/Repositories/Modules/Restaurante/DeliveryZoneRepository.cs`
- Create: `nexo-backend/src/Nexo.Infrastructure/Repositories/Modules/Restaurante/CouponRepository.cs`

- [ ] **Step 1: IDeliveryZoneRepository**

```csharp
// nexo-backend/src/Nexo.Application/Modules/Restaurante/Interfaces/IDeliveryZoneRepository.cs
using Nexo.Domain.Modules.Restaurante;

namespace Nexo.Application.Modules.Restaurante.Interfaces;

public interface IDeliveryZoneRepository
{
    Task<IReadOnlyList<DeliveryZone>> GetAllAsync(CancellationToken ct = default);

    /// <summary>Public: bypasses query filters. Resolves by store slug.</summary>
    Task<IReadOnlyList<DeliveryZone>> GetAllByStoreIdPublicAsync(
        Guid storeId, Guid tenantId, CancellationToken ct = default);

    Task<DeliveryZone?> GetByIdAsync(Guid id, CancellationToken ct = default);
    void Add(DeliveryZone zone);
    void Remove(DeliveryZone zone);
    Task SaveChangesAsync(CancellationToken ct = default);
}
```

- [ ] **Step 2: ICouponRepository**

```csharp
// nexo-backend/src/Nexo.Application/Modules/Restaurante/Interfaces/ICouponRepository.cs
using Nexo.Domain.Modules.Restaurante;

namespace Nexo.Application.Modules.Restaurante.Interfaces;

public interface ICouponRepository
{
    Task<IReadOnlyList<Coupon>> GetAllAsync(CancellationToken ct = default);
    Task<Coupon?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>Public: bypasses query filters. For portal order validation.</summary>
    Task<Coupon?> GetByCodePublicAsync(
        string code, Guid storeId, Guid tenantId, CancellationToken ct = default);

    /// <summary>
    /// Counts existing orders for this phone on this store — for IsFirstOrderOnly check.
    /// Bypasses query filters.
    /// </summary>
    Task<int> CountOrdersByPhonePublicAsync(
        string normalizedPhone, Guid storeId, Guid tenantId, CancellationToken ct = default);

    void Add(Coupon coupon);
    void AddUsage(CouponUsage usage);
    Task SaveChangesAsync(CancellationToken ct = default);
}
```

- [ ] **Step 3: DeliveryZoneRepository**

```csharp
// nexo-backend/src/Nexo.Infrastructure/Repositories/Modules/Restaurante/DeliveryZoneRepository.cs
using Microsoft.EntityFrameworkCore;
using Nexo.Application.Modules.Restaurante.Interfaces;
using Nexo.Domain.Modules.Restaurante;
using Nexo.Infrastructure.Persistence;

namespace Nexo.Infrastructure.Repositories.Modules.Restaurante;

public class DeliveryZoneRepository : IDeliveryZoneRepository
{
    private readonly NexoDbContext _db;
    public DeliveryZoneRepository(NexoDbContext db) => _db = db;

    public Task<IReadOnlyList<DeliveryZone>> GetAllAsync(CancellationToken ct = default)
        => _db.DeliveryZones.AsNoTracking()
            .OrderBy(x => x.Neighborhood)
            .ToListAsync(ct)
            .ContinueWith(t => (IReadOnlyList<DeliveryZone>)t.Result, ct);

    public Task<IReadOnlyList<DeliveryZone>> GetAllByStoreIdPublicAsync(
        Guid storeId, Guid tenantId, CancellationToken ct = default)
        => _db.DeliveryZones.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.StoreId == storeId && x.TenantId == tenantId)
            .OrderBy(x => x.Neighborhood)
            .ToListAsync(ct)
            .ContinueWith(t => (IReadOnlyList<DeliveryZone>)t.Result, ct);

    public Task<DeliveryZone?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => _db.DeliveryZones.FirstOrDefaultAsync(x => x.Id == id, ct);

    public void Add(DeliveryZone zone) => _db.DeliveryZones.Add(zone);
    public void Remove(DeliveryZone zone) => _db.DeliveryZones.Remove(zone);
    public Task SaveChangesAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);
}
```

- [ ] **Step 4: CouponRepository**

```csharp
// nexo-backend/src/Nexo.Infrastructure/Repositories/Modules/Restaurante/CouponRepository.cs
using Microsoft.EntityFrameworkCore;
using Nexo.Application.Modules.Restaurante.Interfaces;
using Nexo.Domain.Modules.Restaurante;
using Nexo.Infrastructure.Persistence;

namespace Nexo.Infrastructure.Repositories.Modules.Restaurante;

public class CouponRepository : ICouponRepository
{
    private readonly NexoDbContext _db;
    public CouponRepository(NexoDbContext db) => _db = db;

    public Task<IReadOnlyList<Coupon>> GetAllAsync(CancellationToken ct = default)
        => _db.Coupons.AsNoTracking()
            .OrderBy(x => x.Code)
            .ToListAsync(ct)
            .ContinueWith(t => (IReadOnlyList<Coupon>)t.Result, ct);

    public Task<Coupon?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => _db.Coupons.FirstOrDefaultAsync(x => x.Id == id, ct);

    public Task<Coupon?> GetByCodePublicAsync(
        string code, Guid storeId, Guid tenantId, CancellationToken ct = default)
        => _db.Coupons.IgnoreQueryFilters()
            .FirstOrDefaultAsync(x =>
                x.StoreId  == storeId  &&
                x.TenantId == tenantId &&
                x.Code     == code.Trim().ToUpperInvariant() &&
                x.IsActive, ct);

    public Task<int> CountOrdersByPhonePublicAsync(
        string normalizedPhone, Guid storeId, Guid tenantId, CancellationToken ct = default)
        => _db.RestDeliveryOrders.IgnoreQueryFilters()
            .CountAsync(x =>
                x.StoreId       == storeId  &&
                x.TenantId      == tenantId &&
                x.CustomerPhone == normalizedPhone &&
                x.Status        != DeliveryOrderStatus.Rejected &&
                x.Status        != DeliveryOrderStatus.Cancelled, ct);

    public void Add(Coupon coupon) => _db.Coupons.Add(coupon);
    public void AddUsage(CouponUsage usage) => _db.CouponUsages.Add(usage);
    public Task SaveChangesAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);
}
```

- [ ] **Step 5: Register repositories in DI**

In `Nexo.Infrastructure/DependencyInjection.cs` (or wherever other repos are registered), add:

```csharp
services.AddScoped<IDeliveryZoneRepository, DeliveryZoneRepository>();
services.AddScoped<ICouponRepository, CouponRepository>();
```

- [ ] **Step 6: Commit**

```bash
git add src/Nexo.Application/Modules/Restaurante/Interfaces/IDeliveryZoneRepository.cs \
        src/Nexo.Application/Modules/Restaurante/Interfaces/ICouponRepository.cs \
        src/Nexo.Infrastructure/Repositories/Modules/Restaurante/DeliveryZoneRepository.cs \
        src/Nexo.Infrastructure/Repositories/Modules/Restaurante/CouponRepository.cs \
        src/Nexo.Infrastructure/DependencyInjection.cs
git commit -m "feat(restaurante): add DeliveryZone and Coupon repositories"
```

---

## Task 6 — DTOs + Application Services

**Files:**
- Create: `nexo-backend/src/Nexo.Application/Modules/Restaurante/DeliveryZoneDtos.cs`
- Create: `nexo-backend/src/Nexo.Application/Modules/Restaurante/CouponDtos.cs`
- Create: `nexo-backend/src/Nexo.Application/Modules/Restaurante/DeliveryZoneService.cs`
- Create: `nexo-backend/src/Nexo.Application/Modules/Restaurante/CouponService.cs`

- [ ] **Step 1: DeliveryZoneDtos.cs**

```csharp
// nexo-backend/src/Nexo.Application/Modules/Restaurante/DeliveryZoneDtos.cs
namespace Nexo.Application.Modules.Restaurante;

public record DeliveryZoneDto(Guid Id, string Neighborhood, decimal Fee);

public record UpsertDeliveryZonesRequest(
    List<UpsertDeliveryZoneItem> Zones);

public record UpsertDeliveryZoneItem(
    string  Neighborhood,
    decimal Fee);
```

- [ ] **Step 2: CouponDtos.cs**

```csharp
// nexo-backend/src/Nexo.Application/Modules/Restaurante/CouponDtos.cs
namespace Nexo.Application.Modules.Restaurante;

public record CouponDto(
    Guid      Id,
    string    Code,
    string?   Description,
    string    DiscountType,
    decimal   DiscountValue,
    bool      IsActive,
    decimal?  MinOrderAmount,
    decimal?  MinDeliveryFee,
    string[]? RestrictToNeighborhoods,
    Guid[]?   RestrictToProductIds,
    bool      IsFirstOrderOnly,
    string?   RestrictToCustomerPhone,
    int?      MaxUses,
    int       UsedCount,
    DateTime? ValidFrom,
    DateTime? ValidUntil);

public record CreateCouponRequest(
    string    Code,
    string    DiscountType,       // "Percentage" | "FixedAmount" | "DeliveryFee"
    decimal   DiscountValue,
    string?   Description             = null,
    decimal?  MinOrderAmount          = null,
    decimal?  MinDeliveryFee          = null,
    string[]? RestrictToNeighborhoods = null,
    Guid[]?   RestrictToProductIds    = null,
    bool      IsFirstOrderOnly        = false,
    string?   RestrictToCustomerPhone = null,
    int?      MaxUses                 = null,
    DateTime? ValidFrom               = null,
    DateTime? ValidUntil              = null);

public record UpdateCouponRequest(
    string    DiscountType,
    decimal   DiscountValue,
    string?   Description             = null,
    decimal?  MinOrderAmount          = null,
    decimal?  MinDeliveryFee          = null,
    string[]? RestrictToNeighborhoods = null,
    Guid[]?   RestrictToProductIds    = null,
    bool      IsFirstOrderOnly        = false,
    string?   RestrictToCustomerPhone = null,
    int?      MaxUses                 = null,
    DateTime? ValidFrom               = null,
    DateTime? ValidUntil              = null);

// Public portal: stateless preview (no DB write)
public record ValidateCouponRequest(
    string  PublicSlug,
    string  CouponCode,
    string  CustomerPhone,
    decimal ItemsSubtotal,
    decimal DeliveryFee,
    string? Neighborhood = null);

public record ValidateCouponResponse(
    bool    Valid,
    string? Error,
    decimal DiscountAmount,
    string  DiscountType,
    decimal DiscountValue);
```

- [ ] **Step 3: DeliveryZoneService.cs**

```csharp
// nexo-backend/src/Nexo.Application/Modules/Restaurante/DeliveryZoneService.cs
using Nexo.Application.Common.Interfaces;
using Nexo.Application.Modules.Restaurante.Interfaces;
using Nexo.Domain.Modules.Restaurante;

namespace Nexo.Application.Modules.Restaurante;

public class DeliveryZoneService
{
    private readonly IDeliveryZoneRepository _repo;
    private readonly ICurrentTenant          _currentTenant;
    private readonly IStoreRepository        _stores;

    public DeliveryZoneService(
        IDeliveryZoneRepository repo,
        ICurrentTenant currentTenant,
        IStoreRepository stores)
    {
        _repo          = repo;
        _currentTenant = currentTenant;
        _stores        = stores;
    }

    public async Task<IReadOnlyList<DeliveryZoneDto>> GetAllAsync(CancellationToken ct = default)
    {
        var zones = await _repo.GetAllAsync(ct);
        return zones.Select(Map).ToList();
    }

    public async Task<IReadOnlyList<DeliveryZoneDto>> GetAllBySlugPublicAsync(
        string slug, CancellationToken ct = default)
    {
        var store = await _stores.GetByPublicSlugAsync(slug, ct)
            ?? throw new Nexo.Domain.Exceptions.NotFoundException("Store", slug);
        var zones = await _repo.GetAllByStoreIdPublicAsync(store.Id, store.TenantId, ct);
        return zones.Select(Map).ToList();
    }

    /// <summary>
    /// Bulk replace: replaces all zones for the current store.
    /// Simple approach: delete all, insert new. Idempotent.
    /// </summary>
    public async Task<IReadOnlyList<DeliveryZoneDto>> UpsertAsync(
        UpsertDeliveryZonesRequest request, CancellationToken ct = default)
    {
        var existing = await _repo.GetAllAsync(ct);
        foreach (var z in existing)
            _repo.Remove(z);

        var created = new List<DeliveryZone>();
        foreach (var item in request.Zones)
        {
            var zone = DeliveryZone.Create(_currentTenant.Id, item.Neighborhood, item.Fee);
            _repo.Add(zone);
            created.Add(zone);
        }

        await _repo.SaveChangesAsync(ct);
        return created.Select(Map).ToList();
    }

    private static DeliveryZoneDto Map(DeliveryZone z) => new(z.Id, z.Neighborhood, z.Fee);
}
```

- [ ] **Step 4: CouponService.cs**

```csharp
// nexo-backend/src/Nexo.Application/Modules/Restaurante/CouponService.cs
using System.Text.Json;
using Nexo.Application.Common.Interfaces;
using Nexo.Application.Modules.Restaurante.Interfaces;
using Nexo.Domain.Exceptions;
using Nexo.Domain.Modules.Restaurante;

namespace Nexo.Application.Modules.Restaurante;

public class CouponService
{
    private readonly ICouponRepository _repo;
    private readonly ICurrentTenant    _currentTenant;
    private readonly IStoreRepository  _stores;

    public CouponService(
        ICouponRepository repo,
        ICurrentTenant    currentTenant,
        IStoreRepository  stores)
    {
        _repo          = repo;
        _currentTenant = currentTenant;
        _stores        = stores;
    }

    public async Task<IReadOnlyList<CouponDto>> GetAllAsync(CancellationToken ct = default)
    {
        var coupons = await _repo.GetAllAsync(ct);
        return coupons.Select(Map).ToList();
    }

    public async Task<CouponDto> CreateAsync(
        CreateCouponRequest req, CancellationToken ct = default)
    {
        var discountType = Enum.Parse<CouponDiscountType>(req.DiscountType, ignoreCase: true);
        var coupon = Coupon.Create(
            _currentTenant.Id,
            req.Code,
            discountType,
            req.DiscountValue,
            req.Description,
            req.MinOrderAmount,
            req.MinDeliveryFee,
            req.RestrictToNeighborhoods is not null
                ? JsonSerializer.Serialize(req.RestrictToNeighborhoods) : null,
            req.RestrictToProductIds is not null
                ? JsonSerializer.Serialize(req.RestrictToProductIds) : null,
            req.IsFirstOrderOnly,
            req.RestrictToCustomerPhone,
            req.MaxUses,
            req.ValidFrom,
            req.ValidUntil);
        _repo.Add(coupon);
        await _repo.SaveChangesAsync(ct);
        return Map(coupon);
    }

    public async Task<CouponDto> UpdateAsync(
        Guid id, UpdateCouponRequest req, CancellationToken ct = default)
    {
        var coupon = await _repo.GetByIdAsync(id, ct)
            ?? throw new NotFoundException("Coupon", id);
        var discountType = Enum.Parse<CouponDiscountType>(req.DiscountType, ignoreCase: true);
        coupon.Update(
            discountType, req.DiscountValue, req.Description,
            req.MinOrderAmount, req.MinDeliveryFee,
            req.RestrictToNeighborhoods is not null
                ? JsonSerializer.Serialize(req.RestrictToNeighborhoods) : null,
            req.RestrictToProductIds is not null
                ? JsonSerializer.Serialize(req.RestrictToProductIds) : null,
            req.IsFirstOrderOnly, req.RestrictToCustomerPhone,
            req.MaxUses, req.ValidFrom, req.ValidUntil);
        await _repo.SaveChangesAsync(ct);
        return Map(coupon);
    }

    public async Task RevokeAsync(Guid id, CancellationToken ct = default)
    {
        var coupon = await _repo.GetByIdAsync(id, ct)
            ?? throw new NotFoundException("Coupon", id);
        coupon.Revoke();
        await _repo.SaveChangesAsync(ct);
    }

    public async Task<ValidateCouponResponse> ValidatePublicAsync(
        ValidateCouponRequest req, CancellationToken ct = default)
    {
        var store = await _stores.GetByPublicSlugAsync(req.PublicSlug, ct)
            ?? throw new NotFoundException("Store", req.PublicSlug);

        var coupon = await _repo.GetByCodePublicAsync(
            req.CouponCode, store.Id, store.TenantId, ct);

        if (coupon is null)
            return new ValidateCouponResponse(false, "Cupom não encontrado.", 0, "", 0);

        var normalizedPhone = new string(req.CustomerPhone.Where(char.IsDigit).ToArray());
        var orderCount = await _repo.CountOrdersByPhonePublicAsync(
            normalizedPhone, store.Id, store.TenantId, ct);
        var isFirstOrder = orderCount == 0;

        try
        {
            var discount = coupon.CalculateDiscount(
                req.ItemsSubtotal, req.DeliveryFee,
                req.CustomerPhone, req.Neighborhood, isFirstOrder);

            return new ValidateCouponResponse(
                true, null, discount,
                coupon.DiscountType.ToString(), coupon.DiscountValue);
        }
        catch (DomainException ex)
        {
            return new ValidateCouponResponse(false, ex.Message, 0, "", 0);
        }
    }

    private static CouponDto Map(Coupon c) => new(
        c.Id, c.Code, c.Description, c.DiscountType.ToString(), c.DiscountValue,
        c.IsActive, c.MinOrderAmount, c.MinDeliveryFee,
        c.RestrictToNeighborhoods is not null
            ? JsonSerializer.Deserialize<string[]>(c.RestrictToNeighborhoods) : null,
        c.RestrictToProductIds is not null
            ? JsonSerializer.Deserialize<Guid[]>(c.RestrictToProductIds) : null,
        c.IsFirstOrderOnly, c.RestrictToCustomerPhone,
        c.MaxUses, c.UsedCount, c.ValidFrom, c.ValidUntil);
}
```

- [ ] **Step 5: Register services in DI**

```csharp
services.AddScoped<DeliveryZoneService>();
services.AddScoped<CouponService>();
```

- [ ] **Step 6: Commit**

```bash
git add src/Nexo.Application/Modules/Restaurante/DeliveryZoneDtos.cs \
        src/Nexo.Application/Modules/Restaurante/CouponDtos.cs \
        src/Nexo.Application/Modules/Restaurante/DeliveryZoneService.cs \
        src/Nexo.Application/Modules/Restaurante/CouponService.cs \
        src/Nexo.Infrastructure/DependencyInjection.cs
git commit -m "feat(restaurante): delivery zone and coupon application services + DTOs"
```

---

## Task 7 — Update CreatePortalOrderRequest + CreateFromPortalAsync

**Files:**
- Modify: `nexo-backend/src/Nexo.Application/Modules/Restaurante/RestDeliveryOrderDtos.cs`
- Modify: `nexo-backend/src/Nexo.Application/Modules/Restaurante/DeliveryOrderService.cs`

- [ ] **Step 1: Add fields to CreatePortalOrderRequest**

In `RestDeliveryOrderDtos.cs`, update `CreatePortalOrderRequest`:

```csharp
public record CreatePortalOrderRequest(
    string  PublicSlug,
    string  OrderType,
    string  CustomerName,
    string  CustomerPhone,
    string? CustomerEmail        = null,
    string? DeliveryAddressJson  = null,
    int?    EstimatedMinutes     = null,
    string? Notes                = null,
    Guid?   DeliveryZoneId       = null,   // NEW — null for Takeaway
    string? CouponCode           = null,   // NEW — optional
    List<CreatePortalOrderItemRequest>? Items = null);
```

Note: `DeliveryFee` is removed from the DTO — it is now resolved server-side from `DeliveryZoneId`.

Also update `DeliveryOrderDto` to include new financial fields:
```csharp
public record DeliveryOrderDto(
    // ... existing fields ...
    decimal  DeliveryFee,
    decimal  ItemsSubtotal,
    decimal  DiscountAmount,   // NEW
    decimal  Total,
    string?  CouponCode,       // NEW
    // ... rest of existing fields ...
```

- [ ] **Step 2: Update Map() in DeliveryOrderService**

In `DeliveryOrderService.cs`, find the `Map(RestDeliveryOrder order)` private method and add the new fields:

```csharp
    private static DeliveryOrderDto Map(RestDeliveryOrder order) => new(
        order.Id,
        order.OrderNumber,
        order.TrackingToken,
        order.Channel.ToString(),
        order.OrderType.ToString(),
        order.Status.ToString(),
        order.RejectionReason,
        order.CustomerName,
        order.CustomerPhone,
        order.CustomerEmail,
        order.CustomerId,
        order.DeliveryAddressJson,
        order.DeliveryFee,
        order.ItemsSubtotal,
        order.DiscountAmount,      // NEW
        order.Total,
        order.CouponCode,          // NEW
        order.EstimatedMinutes,
        order.RiderName,
        order.RiderPhone,
        order.RestOrderId,
        order.ExternalOrderId,
        order.Notes,
        order.ReceivedAt,
        order.AcceptedAt,
        order.ReadyAt,
        order.DispatchedAt,
        order.DeliveredAt,
        order.CancelledAt,
        order.Items.Select(MapItem).ToList());
```

- [ ] **Step 3: Inject IDeliveryZoneRepository + ICouponRepository into DeliveryOrderService**

Add constructor parameters:
```csharp
    private readonly IDeliveryZoneRepository _deliveryZones;
    private readonly ICouponRepository       _coupons;
```

Update constructor to include them.

- [ ] **Step 4: Update CreateFromPortalAsync**

Replace the `deliveryFee` block (currently logs a warning and uses 0) with zone lookup + coupon logic:

```csharp
        // Resolve delivery fee from zone (never trusted from client)
        var deliveryFee = 0m;
        DeliveryZone? resolvedZone = null;
        if (orderType == DeliveryOrderType.Delivery)
        {
            if (!request.DeliveryZoneId.HasValue)
                throw new DomainException("Informe o bairro de entrega.");

            var zones = await _deliveryZones.GetAllByStoreIdPublicAsync(store.Id, store.TenantId, ct);
            resolvedZone = zones.FirstOrDefault(z => z.Id == request.DeliveryZoneId.Value)
                ?? throw new DomainException("Bairro de entrega não disponível.");
            deliveryFee = resolvedZone.Fee;
        }
```

After items are added and before `_repo.SaveChangesAsync`, add coupon logic:

```csharp
        // Apply coupon if provided
        if (!string.IsNullOrWhiteSpace(request.CouponCode))
        {
            var coupon = await _coupons.GetByCodePublicAsync(
                request.CouponCode, store.Id, store.TenantId, ct)
                ?? throw new DomainException("Cupom inválido.");

            var normalizedPhone = RestDeliveryOrder.NormalizePhone(request.CustomerPhone);
            var orderCount = await _coupons.CountOrdersByPhonePublicAsync(
                normalizedPhone, store.Id, store.TenantId, ct);
            var isFirstOrder = orderCount == 0;

            var discountAmount = coupon.CalculateDiscount(
                order.ItemsSubtotal,
                deliveryFee,
                request.CustomerPhone,
                resolvedZone?.Neighborhood,
                isFirstOrder);

            order.ApplyCoupon(coupon.Code, discountAmount);
            coupon.IncrementUsedCount();

            var usage = CouponUsage.Create(
                store.TenantId, coupon.Id, normalizedPhone, order.Id);
            usage.SetStoreId(store.Id);   // internal — accessible within domain assembly
            _coupons.AddUsage(usage);
        }

        await _repo.SaveChangesAsync(ct);
```

**Note on `usage.SetStoreId`:** `SetStoreId` is `internal` in `Nexo.Domain`. `CouponService` is in `Nexo.Application` — no direct access. Two clean options:
- Option A: Add a `CouponUsage.CreateForStore(tenantId, storeId, ...)` factory overload that takes storeId explicitly (parallel to how `RestDeliveryOrder.Create` handles `storeId`).
- **Use Option A** — add this to `CouponUsage.cs`:

```csharp
    public static CouponUsage Create(
        Guid tenantId, Guid storeId,
        Guid couponId, string customerPhone, Guid deliveryOrderId)
    {
        var usage = new CouponUsage(tenantId)
        {
            CouponId        = couponId,
            CustomerPhone   = customerPhone,
            DeliveryOrderId = deliveryOrderId,
            UsedAt          = DateTime.UtcNow,
        };
        usage.SetStoreId(storeId);  // internal — same assembly (Nexo.Domain)
        return usage;
    }
```

Update `CreateFromPortalAsync` accordingly:
```csharp
            var usage = CouponUsage.Create(
                store.TenantId, store.Id, coupon.Id, normalizedPhone, order.Id);
            _coupons.AddUsage(usage);
```

- [ ] **Step 5: Build to verify**

```bash
cd nexo-backend
dotnet build src/Nexo.Api
```

Expected: 0 errors.

- [ ] **Step 6: Commit**

```bash
git add src/Nexo.Application/Modules/Restaurante/RestDeliveryOrderDtos.cs \
        src/Nexo.Application/Modules/Restaurante/DeliveryOrderService.cs \
        src/Nexo.Domain/Modules/Restaurante/CouponUsage.cs
git commit -m "feat(restaurante): portal order resolves delivery fee from zone and applies coupon"
```

---

## Task 8 — API Controllers

**Files:**
- Create: `nexo-backend/src/Nexo.Api/Controllers/Modules/Restaurante/DeliveryZonesController.cs`
- Create: `nexo-backend/src/Nexo.Api/Controllers/Modules/Restaurante/CouponsController.cs`
- Modify: existing public portal controller (find via `GetByPublicSlugAsync` or `PublicMenu`)

- [ ] **Step 1: Identify public controller path**

```bash
grep -r "public/menu" nexo-backend/src/Nexo.Api --include="*.cs" -l
```

Note the file. Typically `PublicPortalController.cs` or `PublicMenuController.cs`. Add new public endpoints there.

- [ ] **Step 2: DeliveryZonesController (authenticated management)**

```csharp
// nexo-backend/src/Nexo.Api/Controllers/Modules/Restaurante/DeliveryZonesController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nexo.Application.Modules.Restaurante;

namespace Nexo.Api.Controllers.Modules.Restaurante;

[ApiController]
[Route("api/restaurante/delivery-zones")]
[Authorize]
public class DeliveryZonesController : ControllerBase
{
    private readonly DeliveryZoneService _svc;
    public DeliveryZonesController(DeliveryZoneService svc) => _svc = svc;

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct)
        => Ok(await _svc.GetAllAsync(ct));

    [HttpPut]
    public async Task<IActionResult> Upsert(
        [FromBody] UpsertDeliveryZonesRequest req, CancellationToken ct)
        => Ok(await _svc.UpsertAsync(req, ct));
}
```

- [ ] **Step 3: CouponsController (authenticated management)**

```csharp
// nexo-backend/src/Nexo.Api/Controllers/Modules/Restaurante/CouponsController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nexo.Application.Modules.Restaurante;

namespace Nexo.Api.Controllers.Modules.Restaurante;

[ApiController]
[Route("api/restaurante/coupons")]
[Authorize]
public class CouponsController : ControllerBase
{
    private readonly CouponService _svc;
    public CouponsController(CouponService svc) => _svc = svc;

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct)
        => Ok(await _svc.GetAllAsync(ct));

    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] CreateCouponRequest req, CancellationToken ct)
        => Ok(await _svc.CreateAsync(req, ct));

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(
        Guid id, [FromBody] UpdateCouponRequest req, CancellationToken ct)
        => Ok(await _svc.UpdateAsync(id, req, ct));

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Revoke(Guid id, CancellationToken ct)
    {
        await _svc.RevokeAsync(id, ct);
        return NoContent();
    }
}
```

- [ ] **Step 4: Add public endpoints to existing public controller**

Find the public controller file (from step 1). Add:

```csharp
    // Inject via constructor:
    private readonly DeliveryZoneService _zones;
    private readonly CouponService       _couponSvc;

    [HttpGet("delivery-zones/{slug}")]
    [AllowAnonymous]
    public async Task<IActionResult> GetDeliveryZones(string slug, CancellationToken ct)
        => Ok(await _zones.GetAllBySlugPublicAsync(slug, ct));

    [HttpPost("coupons/validate")]
    [AllowAnonymous]
    public async Task<IActionResult> ValidateCoupon(
        [FromBody] ValidateCouponRequest req, CancellationToken ct)
        => Ok(await _couponSvc.ValidatePublicAsync(req, ct));
```

- [ ] **Step 5: Build + run integration tests**

```bash
cd nexo-backend
dotnet build src/Nexo.Api
dotnet test tests/Nexo.IntegrationTests --filter "Category=Restaurante" --no-build
```

Expected: all existing tests pass, 0 new failures.

- [ ] **Step 6: Commit**

```bash
git add src/Nexo.Api/Controllers/Modules/Restaurante/DeliveryZonesController.cs \
        src/Nexo.Api/Controllers/Modules/Restaurante/CouponsController.cs
git add src/Nexo.Api/Controllers/  # for the modified public controller
git commit -m "feat(restaurante): delivery zones and coupons API controllers"
```

---

## Task 9 — Frontend Types + API functions

**Files:**
- Modify: `nexo-main/src/modules/portal/api/portal.api.ts`
- Create: `nexo-main/src/modules/restaurante/api/restaurante.api.ts` (or modify if it exists)

- [ ] **Step 1: Add portal types inline in portal.api.ts**

Add to `portal.api.ts`:

```typescript
// ── Delivery Zones ────────────────────────────────────────────────────────────

export interface DeliveryZoneDto {
  id:           string;
  neighborhood: string;
  fee:          number;
}

export const getDeliveryZones = (slug: string): Promise<DeliveryZoneDto[]> =>
  get<DeliveryZoneDto[]>(`${BASE}/public/delivery-zones/${slug}`);

// ── Coupon validation ─────────────────────────────────────────────────────────

export interface ValidateCouponRequest {
  publicSlug:    string;
  couponCode:    string;
  customerPhone: string;
  itemsSubtotal: number;
  deliveryFee:   number;
  neighborhood?: string;
}

export interface ValidateCouponResponse {
  valid:         boolean;
  error?:        string;
  discountAmount: number;
  discountType:  string;
  discountValue: number;
}

export const validateCoupon = (req: ValidateCouponRequest): Promise<ValidateCouponResponse> =>
  post<ValidateCouponResponse>(`${BASE}/public/coupons/validate`, req);
```

Also update `CreatePortalOrderRequest` to add new fields:
```typescript
export interface CreatePortalOrderRequest {
  publicSlug:          string;
  orderType:           "Delivery" | "Takeaway";
  customerName:        string;
  customerPhone:       string;
  customerEmail?:      string | null;
  deliveryAddressJson?: string | null;
  notes?:              string | null;
  deliveryZoneId?:     string | null;   // NEW
  couponCode?:         string | null;   // NEW
  items?:              CreatePortalOrderItem[];
}
```

- [ ] **Step 2: Add restaurante management API functions**

Find or create `nexo-main/src/modules/restaurante/api/restaurante.api.ts`. Add:

```typescript
import { apiClient } from "@/services/api-client";

export interface DeliveryZoneDto {
  id:           string;
  neighborhood: string;
  fee:          number;
}

export interface UpsertDeliveryZonesRequest {
  zones: Array<{ neighborhood: string; fee: number }>;
}

export const getDeliveryZones = (): Promise<DeliveryZoneDto[]> =>
  apiClient.get<DeliveryZoneDto[]>("/restaurante/delivery-zones");

export const upsertDeliveryZones = (req: UpsertDeliveryZonesRequest): Promise<DeliveryZoneDto[]> =>
  apiClient.put<DeliveryZoneDto[]>("/restaurante/delivery-zones", req);

export interface CouponDto {
  id:                      string;
  code:                    string;
  description?:            string;
  discountType:            "Percentage" | "FixedAmount" | "DeliveryFee";
  discountValue:           number;
  isActive:                boolean;
  minOrderAmount?:         number;
  minDeliveryFee?:         number;
  restrictToNeighborhoods?: string[];
  restrictToProductIds?:   string[];
  isFirstOrderOnly:        boolean;
  restrictToCustomerPhone?: string;
  maxUses?:                number;
  usedCount:               number;
  validFrom?:              string;
  validUntil?:             string;
}

export interface CreateCouponRequest {
  code:                    string;
  discountType:            string;
  discountValue:           number;
  description?:            string;
  minOrderAmount?:         number;
  minDeliveryFee?:         number;
  restrictToNeighborhoods?: string[];
  restrictToProductIds?:   string[];
  isFirstOrderOnly?:       boolean;
  restrictToCustomerPhone?: string;
  maxUses?:                number;
  validFrom?:              string;
  validUntil?:             string;
}

export type UpdateCouponRequest = Omit<CreateCouponRequest, "code">;

export const getCoupons = (): Promise<CouponDto[]> =>
  apiClient.get<CouponDto[]>("/restaurante/coupons");

export const createCoupon = (req: CreateCouponRequest): Promise<CouponDto> =>
  apiClient.post<CouponDto>("/restaurante/coupons", req);

export const updateCoupon = (id: string, req: UpdateCouponRequest): Promise<CouponDto> =>
  apiClient.put<CouponDto>(`/restaurante/coupons/${id}`, req);

export const revokeCoupon = (id: string): Promise<void> =>
  apiClient.delete<void>(`/restaurante/coupons/${id}`);
```

- [ ] **Step 3: Commit**

```bash
cd nexo-main
git add src/modules/portal/api/portal.api.ts \
        src/modules/restaurante/api/restaurante.api.ts
git commit -m "feat(portal): add delivery zones + coupon API functions"
```

---

## Task 10 — CartSheet rework (CEP + zone + coupon + breakdown)

**Files:**
- Modify: `nexo-main/src/modules/portal/components/CartSheet.tsx`

- [ ] **Step 1: Rewrite CartSheet.tsx**

```tsx
import { useState, useEffect } from "react";
import { X, Minus, Plus, Loader2, MapPin, Tag } from "lucide-react";
import { Sheet, SheetContent, SheetHeader, SheetTitle } from "@/components/ui/sheet";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Textarea } from "@/components/ui/textarea";
import { cn } from "@/lib/utils";
import { useMutation, useQuery } from "@tanstack/react-query";
import { useNavigate } from "react-router-dom";
import type { CartItem } from "../types";
import {
  createPortalOrder,
  getDeliveryZones,
  validateCoupon,
  type CreatePortalOrderRequest,
  type DeliveryZoneDto,
} from "../api/portal.api";

interface CartSheetProps {
  open:            boolean;
  onClose:         () => void;
  items:           CartItem[];
  onChangeQty:     (productId: string, delta: number) => void;
  onRemove:        (productId: string) => void;
  slug:            string;
  deliveryEnabled: boolean;
  takeawayEnabled: boolean;
}

interface ViaCepResponse {
  bairro?:      string;
  logradouro?:  string;
  localidade?:  string;
  uf?:          string;
  erro?:        boolean;
}

export function CartSheet({
  open, onClose, items, onChangeQty, onRemove,
  slug, deliveryEnabled, takeawayEnabled,
}: CartSheetProps) {
  const navigate = useNavigate();

  const [orderType, setOrderType] = useState<"Delivery" | "Takeaway">(
    deliveryEnabled ? "Delivery" : "Takeaway"
  );
  const [name,    setName]    = useState("");
  const [phone,   setPhone]   = useState("");
  const [email,   setEmail]   = useState("");
  const [notes,   setNotes]   = useState("");

  // CEP + zone
  const [cep,           setCep]           = useState("");
  const [cepAddress,    setCepAddress]    = useState<ViaCepResponse | null>(null);
  const [cepLoading,    setCepLoading]    = useState(false);
  const [cepError,      setCepError]      = useState<string | null>(null);
  const [selectedZone,  setSelectedZone]  = useState<DeliveryZoneDto | null>(null);
  const [manualAddress, setManualAddress] = useState("");

  // Coupon
  const [couponInput,    setCouponInput]    = useState("");
  const [appliedCoupon,  setAppliedCoupon]  = useState<string | null>(null);
  const [couponError,    setCouponError]    = useState<string | null>(null);
  const [discountAmount, setDiscountAmount] = useState(0);
  const [couponLoading,  setCouponLoading]  = useState(false);

  // Load delivery zones when sheet opens (only for delivery)
  const { data: zones = [] } = useQuery({
    queryKey: ["delivery-zones", slug],
    queryFn:  () => getDeliveryZones(slug),
    enabled:  open && orderType === "Delivery",
    staleTime: 5 * 60 * 1000,
  });

  // Reset zone when neighborhoods change or order type switches
  useEffect(() => {
    if (orderType !== "Delivery") {
      setSelectedZone(null);
      setCepAddress(null);
      setCep("");
      setCepError(null);
    }
  }, [orderType]);

  // Lookup CEP
  const lookupCep = async (value: string) => {
    const digits = value.replace(/\D/g, "");
    if (digits.length !== 8) return;
    setCepLoading(true);
    setCepError(null);
    setSelectedZone(null);
    try {
      const res = await fetch(`https://viacep.com.br/ws/${digits}/json/`);
      const data: ViaCepResponse = await res.json();
      if (data.erro) { setCepError("CEP não encontrado."); setCepAddress(null); return; }
      setCepAddress(data);
      // Auto-match neighborhood to a zone
      const matched = zones.find(z =>
        z.neighborhood.toLowerCase() === (data.bairro ?? "").toLowerCase());
      if (matched) {
        setSelectedZone(matched);
      } else if (data.bairro) {
        setCepError(`Seu bairro (${data.bairro}) não está na área de entrega.`);
      } else {
        setCepError("Não foi possível identificar o bairro pelo CEP.");
      }
    } catch {
      setCepError("Erro ao buscar CEP. Verifique e tente novamente.");
    } finally {
      setCepLoading(false);
    }
  };

  const handleCepChange = (value: string) => {
    setCep(value);
    const digits = value.replace(/\D/g, "");
    if (digits.length === 8) lookupCep(value);
  };

  // Apply coupon
  const handleApplyCoupon = async () => {
    if (!couponInput.trim() || !phone.trim()) return;
    setCouponLoading(true);
    setCouponError(null);
    try {
      const res = await validateCoupon({
        publicSlug:    slug,
        couponCode:    couponInput.trim(),
        customerPhone: phone.trim(),
        itemsSubtotal: itemsSubtotal,
        deliveryFee:   selectedZone?.fee ?? 0,
        neighborhood:  selectedZone?.neighborhood,
      });
      if (res.valid) {
        setAppliedCoupon(couponInput.trim().toUpperCase());
        setDiscountAmount(res.discountAmount);
        setCouponError(null);
      } else {
        setCouponError(res.error ?? "Cupom inválido.");
        setAppliedCoupon(null);
        setDiscountAmount(0);
      }
    } catch {
      setCouponError("Erro ao validar cupom.");
    } finally {
      setCouponLoading(false);
    }
  };

  const removeCoupon = () => {
    setAppliedCoupon(null);
    setDiscountAmount(0);
    setCouponInput("");
    setCouponError(null);
  };

  // Financials
  const itemsSubtotal = items.reduce(
    (s, i) => s + (i.price + i.modifiers.reduce((ms, m) => ms + m.price, 0)) * i.quantity, 0);
  const deliveryFee = orderType === "Delivery" ? (selectedZone?.fee ?? 0) : 0;
  const total = itemsSubtotal + deliveryFee - discountAmount;

  const deliveryAddress = cepAddress
    ? [cepAddress.logradouro, manualAddress, cepAddress.bairro, cepAddress.localidade, cepAddress.uf]
        .filter(Boolean).join(", ")
    : manualAddress;

  const mut = useMutation({
    mutationFn: (req: CreatePortalOrderRequest) => createPortalOrder(req),
    onSuccess: (data) => {
      onClose();
      navigate(`/rastrear/${data.trackingToken}`);
    },
  });

  const canSubmit =
    name.trim() && phone.trim() &&
    (orderType === "Takeaway" || (selectedZone && deliveryAddress.trim())) &&
    items.length > 0 && !mut.isPending;

  const handleSubmit = () => {
    if (!canSubmit) return;
    mut.mutate({
      publicSlug:          slug,
      orderType,
      customerName:        name.trim(),
      customerPhone:       phone.trim(),
      customerEmail:       email.trim() || null,
      deliveryAddressJson: orderType === "Delivery"
        ? JSON.stringify({ address: deliveryAddress.trim(), cep: cep.replace(/\D/g, "") })
        : null,
      notes:               notes.trim() || null,
      deliveryZoneId:      orderType === "Delivery" ? (selectedZone?.id ?? null) : null,
      couponCode:          appliedCoupon ?? null,
      items: items.map((i) => ({
        productId: i.productId,
        quantity:  i.quantity,
        notes:     i.notes || null,
        modifiers: i.modifiers.map((m) => ({ modifierId: m.modifierId })),
      })),
    });
  };

  return (
    <Sheet open={open} onOpenChange={(v) => !v && onClose()}>
      <SheetContent side="bottom" className="rounded-t-2xl flex flex-col max-h-[92vh] pb-safe-bottom pb-6">
        <SheetHeader className="mb-4 shrink-0">
          <SheetTitle>Seu pedido</SheetTitle>
        </SheetHeader>

        <div className="flex-1 overflow-y-auto min-h-0 flex flex-col gap-4">
          {/* Items */}
          <div className="flex flex-col gap-2 shrink-0">
            {items.map((item) => (
              <div key={item.productId} className="flex items-start gap-2">
                <div className="flex items-center gap-2 shrink-0">
                  <button onClick={() => onChangeQty(item.productId, -1)}
                    className="h-7 w-7 rounded-full border border-border flex items-center justify-center">
                    <Minus className="h-3 w-3" />
                  </button>
                  <span className="w-5 text-center text-sm font-medium tabular-nums">{item.quantity}</span>
                  <button onClick={() => onChangeQty(item.productId, 1)}
                    className="h-7 w-7 rounded-full border border-border flex items-center justify-center">
                    <Plus className="h-3 w-3" />
                  </button>
                </div>
                <div className="flex-1 min-w-0">
                  <p className="text-sm font-medium leading-tight">{item.productName}</p>
                  {item.modifiers.length > 0 && (
                    <p className="text-xs text-muted-foreground">{item.modifiers.map(m => m.label).join(", ")}</p>
                  )}
                  {item.notes && <p className="text-xs text-muted-foreground italic">{item.notes}</p>}
                </div>
                <div className="flex items-center gap-2 shrink-0">
                  <span className="text-sm tabular-nums">
                    R$ {((item.price + item.modifiers.reduce((s, m) => s + m.price, 0)) * item.quantity).toFixed(2)}
                  </span>
                  <button onClick={() => onRemove(item.productId)} className="text-muted-foreground hover:text-destructive">
                    <X className="h-3.5 w-3.5" />
                  </button>
                </div>
              </div>
            ))}
          </div>

          {/* Order type */}
          {deliveryEnabled && takeawayEnabled && (
            <div className="flex rounded-lg border border-border overflow-hidden shrink-0">
              {(["Delivery", "Takeaway"] as const).map((t) => (
                <button key={t} onClick={() => setOrderType(t)}
                  className={cn("flex-1 py-2.5 text-sm font-medium transition-colors",
                    orderType === t ? "bg-primary text-primary-foreground" : "text-muted-foreground hover:text-foreground")}>
                  {t === "Delivery" ? "Entrega" : "Retirada"}
                </button>
              ))}
            </div>
          )}

          {/* Customer */}
          <div className="flex flex-col gap-2 shrink-0">
            <Input placeholder="Seu nome *" value={name} onChange={(e) => setName(e.target.value)} />
            <Input placeholder="Telefone / WhatsApp *" value={phone} onChange={(e) => setPhone(e.target.value)} type="tel" />
            <Input placeholder="E-mail (opcional)" value={email} onChange={(e) => setEmail(e.target.value)} type="email" />
          </div>

          {/* Delivery: CEP + zone */}
          {orderType === "Delivery" && (
            <div className="flex flex-col gap-2 shrink-0">
              <div className="relative">
                <Input
                  placeholder="CEP *"
                  value={cep}
                  onChange={(e) => handleCepChange(e.target.value)}
                  maxLength={9}
                  className="pr-8"
                />
                {cepLoading && (
                  <Loader2 className="absolute right-2.5 top-1/2 -translate-y-1/2 h-4 w-4 animate-spin text-muted-foreground" />
                )}
              </div>

              {cepError && (
                <p className="text-xs text-destructive">{cepError}</p>
              )}

              {cepAddress && !cepError && (
                <div className="rounded-lg border border-border p-3 text-sm flex flex-col gap-1">
                  <div className="flex items-center gap-1.5 text-muted-foreground">
                    <MapPin className="h-3.5 w-3.5 shrink-0" />
                    <span>{[cepAddress.logradouro, cepAddress.bairro, cepAddress.localidade].filter(Boolean).join(", ")}</span>
                  </div>
                  {selectedZone && (
                    <p className="text-xs text-green-600 font-medium">
                      Entrega disponível — Taxa: R$ {selectedZone.fee.toFixed(2)}
                    </p>
                  )}
                </div>
              )}

              {/* Manual complement / house number */}
              <Input
                placeholder="Número e complemento *"
                value={manualAddress}
                onChange={(e) => setManualAddress(e.target.value)}
              />

              {/* Zone selector (fallback if CEP didn't auto-match) */}
              {zones.length > 0 && !selectedZone && cepAddress && !cepError && (
                <div className="flex flex-col gap-1">
                  <p className="text-xs text-muted-foreground">Selecione o bairro:</p>
                  <div className="grid grid-cols-2 gap-1.5 max-h-36 overflow-y-auto">
                    {zones.map((z) => (
                      <button key={z.id}
                        onClick={() => setSelectedZone(z)}
                        className="text-left rounded-lg border border-border px-2.5 py-1.5 text-xs hover:border-primary transition-colors">
                        <span className="font-medium">{z.neighborhood}</span>
                        <span className="text-muted-foreground block">R$ {z.fee.toFixed(2)}</span>
                      </button>
                    ))}
                  </div>
                </div>
              )}
            </div>
          )}

          {/* Notes */}
          <Textarea
            placeholder="Observações gerais (opcional)"
            value={notes}
            onChange={(e) => setNotes(e.target.value)}
            rows={2}
            className="resize-none shrink-0"
          />

          {/* Coupon */}
          <div className="flex flex-col gap-1.5 shrink-0">
            {appliedCoupon ? (
              <div className="flex items-center justify-between rounded-lg border border-green-200 bg-green-50 dark:bg-green-950/20 dark:border-green-800 px-3 py-2">
                <div className="flex items-center gap-2 text-green-700 dark:text-green-400">
                  <Tag className="h-3.5 w-3.5" />
                  <span className="text-sm font-medium">{appliedCoupon}</span>
                  <span className="text-xs">− R$ {discountAmount.toFixed(2)}</span>
                </div>
                <button onClick={removeCoupon} className="text-muted-foreground hover:text-destructive">
                  <X className="h-3.5 w-3.5" />
                </button>
              </div>
            ) : (
              <div className="flex gap-2">
                <Input
                  placeholder="Cupom de desconto"
                  value={couponInput}
                  onChange={(e) => setCouponInput(e.target.value.toUpperCase())}
                  className="flex-1"
                />
                <Button
                  variant="outline"
                  size="sm"
                  onClick={handleApplyCoupon}
                  disabled={!couponInput.trim() || couponLoading}
                  className="shrink-0"
                >
                  {couponLoading ? <Loader2 className="h-4 w-4 animate-spin" /> : "Aplicar"}
                </Button>
              </div>
            )}
            {couponError && <p className="text-xs text-destructive">{couponError}</p>}
          </div>

          {/* Price breakdown */}
          <div className="border-t border-border pt-2 flex flex-col gap-1 shrink-0">
            <div className="flex justify-between text-sm">
              <span className="text-muted-foreground">Subtotal</span>
              <span className="tabular-nums">R$ {itemsSubtotal.toFixed(2)}</span>
            </div>
            {orderType === "Delivery" && (
              <div className="flex justify-between text-sm">
                <span className="text-muted-foreground">Taxa de entrega</span>
                <span className="tabular-nums">
                  {selectedZone ? `R$ ${deliveryFee.toFixed(2)}` : "—"}
                </span>
              </div>
            )}
            {discountAmount > 0 && (
              <div className="flex justify-between text-sm text-green-600 dark:text-green-400">
                <span>Desconto</span>
                <span className="tabular-nums">− R$ {discountAmount.toFixed(2)}</span>
              </div>
            )}
            <div className="flex justify-between font-bold mt-1">
              <span>Total</span>
              <span className="tabular-nums">R$ {total.toFixed(2)}</span>
            </div>
          </div>
        </div>

        {mut.isError && (
          <p className="text-sm text-destructive text-center shrink-0 mt-2">
            Erro ao enviar pedido. Tente novamente.
          </p>
        )}

        <Button
          className="w-full h-12 text-base mt-4 shrink-0"
          onClick={handleSubmit}
          disabled={!canSubmit}
        >
          {mut.isPending ? "Enviando..." : `Confirmar pedido · R$ ${total.toFixed(2)}`}
        </Button>
      </SheetContent>
    </Sheet>
  );
}
```

- [ ] **Step 2: Verify it compiles**

```bash
cd nexo-main
npm run build -- --mode development 2>&1 | head -30
```

Expected: no TypeScript errors.

- [ ] **Step 3: Commit**

```bash
git add src/modules/portal/components/CartSheet.tsx
git commit -m "feat(portal): CEP lookup, delivery zone selection, coupon field, price breakdown"
```

---

## Task 11 — PortalSetupPage: Zonas de Entrega tab

**Files:**
- Modify: `nexo-main/src/modules/restaurante/pages/PortalSetupPage.tsx`

- [ ] **Step 1: Add imports**

At the top of `PortalSetupPage.tsx`, add:

```typescript
import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { getDeliveryZones, upsertDeliveryZones } from "../api/restaurante.api";
import type { DeliveryZoneDto } from "../api/restaurante.api";
```

- [ ] **Step 2: Add the static Fortaleza neighborhoods list**

Add before the component:

```typescript
const FORTALEZA_NEIGHBORHOODS = [
  "Aeroporto","Aldeota","Barra do Ceará","Bela Vista","Benfica","Bom Futuro",
  "Cajazeiras","Cais do Porto","Centro","Cidade 2000","Conjunto Ceará",
  "Conjunto Esperança","Couto Fernandes","Dias Macedo","Damas","Demócrito Rocha",
  "Edson Queiroz","Ellery","Fátima","Floresta","Genibaú","Godofredo Maciel",
  "Granja Lisboa","Granja Portugal","Guajeru","Henrique Jorge","Itaperi",
  "Itaoca","Jacarecanga","Jardim América","João XXIII","Jóquei Clube",
  "José Bonifácio","José Walter","Jangurussu","Lagoa Redonda","Maraponga",
  "Messejana","Mondubim","Montese","Morro Santana","Mucuripe","Padre Andrade",
  "Parangaba","Parquelândia","Parque Dois Irmãos","Passaré","Pici",
  "Pirambu","Praia de Iracema","Praia do Futuro","Quintino Cunha",
  "Rodolfo Teófilo","São Gerardo","São João do Tauape","Serrinha",
  "Siqueira","Tancredo Neves","Varjota","Vicente Pinzón","Vila Ellery",
  "Vila Peri","Vila União","Zé Padre",
].sort();
```

- [ ] **Step 3: Add zone state and queries inside the component**

```typescript
  const queryClient = useQueryClient();

  // Zone state: map from neighborhood → fee (empty string = not active)
  const [zoneFees, setZoneFees] = useState<Record<string, string>>({});

  const { data: savedZones = [] } = useQuery({
    queryKey: ["delivery-zones"],
    queryFn:  getDeliveryZones,
  });

  // Sync saved zones into local state when loaded
  useEffect(() => {
    const map: Record<string, string> = {};
    savedZones.forEach((z) => { map[z.neighborhood] = String(z.fee); });
    setZoneFees(map);
  }, [savedZones]);

  const upsertMut = useMutation({
    mutationFn: upsertDeliveryZones,
    onSuccess:  () => queryClient.invalidateQueries({ queryKey: ["delivery-zones"] }),
  });

  const handleSaveZones = () => {
    const zones = Object.entries(zoneFees)
      .filter(([, fee]) => fee !== "" && !isNaN(Number(fee)) && Number(fee) >= 0)
      .map(([neighborhood, fee]) => ({ neighborhood, fee: Number(fee) }));
    upsertMut.mutate({ zones });
  };

  const toggleNeighborhood = (n: string) => {
    setZoneFees((prev) => {
      if (n in prev) {
        const next = { ...prev };
        delete next[n];
        return next;
      }
      return { ...prev, [n]: "0" };
    });
  };
```

- [ ] **Step 4: Add the Zonas tab UI**

Find the tab list in `PortalSetupPage.tsx` and add:
```tsx
<TabsTrigger value="zonas">Zonas de Entrega</TabsTrigger>
```

Add the tab content panel:
```tsx
<TabsContent value="zonas" className="space-y-4">
  <div className="flex items-center justify-between">
    <div>
      <h3 className="font-medium">Zonas de Entrega — Fortaleza</h3>
      <p className="text-sm text-muted-foreground">
        Ative os bairros que você atende e defina a taxa de entrega.
      </p>
    </div>
    <Button onClick={handleSaveZones} disabled={upsertMut.isPending} size="sm">
      {upsertMut.isPending ? "Salvando..." : "Salvar zonas"}
    </Button>
  </div>

  <div className="grid grid-cols-1 sm:grid-cols-2 gap-2 max-h-[60vh] overflow-y-auto pr-1">
    {FORTALEZA_NEIGHBORHOODS.map((n) => {
      const active = n in zoneFees;
      return (
        <div key={n}
          className={cn(
            "flex items-center gap-3 rounded-lg border px-3 py-2 transition-colors",
            active ? "border-primary bg-primary/5" : "border-border"
          )}>
          <input
            type="checkbox"
            checked={active}
            onChange={() => toggleNeighborhood(n)}
            className="h-4 w-4 rounded border-border accent-primary cursor-pointer"
          />
          <span className="flex-1 text-sm">{n}</span>
          {active && (
            <div className="flex items-center gap-1">
              <span className="text-xs text-muted-foreground">R$</span>
              <input
                type="number"
                min="0"
                step="0.50"
                value={zoneFees[n] ?? "0"}
                onChange={(e) => setZoneFees((prev) => ({ ...prev, [n]: e.target.value }))}
                className="w-16 text-right text-sm border border-border rounded px-1.5 py-0.5 bg-background"
              />
            </div>
          )}
        </div>
      );
    })}
  </div>

  {upsertMut.isSuccess && (
    <p className="text-sm text-green-600">Zonas salvas com sucesso!</p>
  )}
  {upsertMut.isError && (
    <p className="text-sm text-destructive">Erro ao salvar zonas.</p>
  )}
</TabsContent>
```

- [ ] **Step 5: Commit**

```bash
git add src/modules/restaurante/pages/PortalSetupPage.tsx
git commit -m "feat(restaurante): delivery zones management tab in portal settings"
```

---

## Task 12 — PortalSetupPage: Cupons tab

**Files:**
- Modify: `nexo-main/src/modules/restaurante/pages/PortalSetupPage.tsx`

- [ ] **Step 1: Add coupon imports + queries**

```typescript
import {
  getCoupons, createCoupon, updateCoupon, revokeCoupon,
  type CouponDto, type CreateCouponRequest,
} from "../api/restaurante.api";
import { Badge } from "@/components/ui/badge";
import { Dialog, DialogContent, DialogHeader, DialogTitle, DialogFooter } from "@/components/ui/dialog";
import { Label } from "@/components/ui/label";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
```

Add inside the component (alongside zone state):

```typescript
  const { data: coupons = [] } = useQuery({
    queryKey: ["coupons"],
    queryFn:  getCoupons,
  });

  const [couponDialog, setCouponDialog] = useState(false);
  const [editingCoupon, setEditingCoupon] = useState<CouponDto | null>(null);
  const [couponForm, setCouponForm] = useState<CreateCouponRequest>({
    code: "", discountType: "Percentage", discountValue: 0,
  });

  const createMut = useMutation({
    mutationFn: createCoupon,
    onSuccess:  () => { queryClient.invalidateQueries({ queryKey: ["coupons"] }); setCouponDialog(false); },
  });

  const updateMut = useMutation({
    mutationFn: ({ id, req }: { id: string; req: Omit<CreateCouponRequest, "code"> }) =>
      updateCoupon(id, req),
    onSuccess:  () => { queryClient.invalidateQueries({ queryKey: ["coupons"] }); setCouponDialog(false); },
  });

  const revokeMut = useMutation({
    mutationFn: revokeCoupon,
    onSuccess:  () => queryClient.invalidateQueries({ queryKey: ["coupons"] }),
  });

  const openNewCoupon = () => {
    setEditingCoupon(null);
    setCouponForm({ code: "", discountType: "Percentage", discountValue: 0 });
    setCouponDialog(true);
  };

  const openEditCoupon = (c: CouponDto) => {
    setEditingCoupon(c);
    setCouponForm({
      code:          c.code,
      discountType:  c.discountType,
      discountValue: c.discountValue,
      description:   c.description,
      minOrderAmount: c.minOrderAmount,
      maxUses:       c.maxUses,
      validUntil:    c.validUntil ? c.validUntil.slice(0, 10) : undefined,
    });
    setCouponDialog(true);
  };

  const handleSaveCoupon = () => {
    if (editingCoupon) {
      const { code, ...rest } = couponForm;
      updateMut.mutate({ id: editingCoupon.id, req: rest });
    } else {
      createMut.mutate(couponForm);
    }
  };
```

- [ ] **Step 2: Add Cupons tab trigger + content**

Add tab trigger:
```tsx
<TabsTrigger value="cupons">Cupons</TabsTrigger>
```

Add tab content:
```tsx
<TabsContent value="cupons" className="space-y-4">
  <div className="flex items-center justify-between">
    <div>
      <h3 className="font-medium">Cupons de Desconto</h3>
      <p className="text-sm text-muted-foreground">Crie e gerencie cupons para seus clientes.</p>
    </div>
    <Button onClick={openNewCoupon} size="sm">Novo cupom</Button>
  </div>

  <div className="flex flex-col gap-2">
    {coupons.length === 0 && (
      <p className="text-sm text-muted-foreground text-center py-8">Nenhum cupom criado.</p>
    )}
    {coupons.map((c) => (
      <div key={c.id} className="flex items-start gap-3 rounded-lg border border-border px-3 py-2.5">
        <div className="flex-1 min-w-0">
          <div className="flex items-center gap-2">
            <span className="font-mono font-medium text-sm">{c.code}</span>
            <Badge variant={c.isActive ? "default" : "secondary"} className="text-xs">
              {c.isActive ? "Ativo" : "Revogado"}
            </Badge>
          </div>
          <p className="text-xs text-muted-foreground mt-0.5">
            {c.discountType === "Percentage"
              ? `${c.discountValue}% de desconto`
              : c.discountType === "FixedAmount"
              ? `R$ ${c.discountValue.toFixed(2)} de desconto`
              : `R$ ${c.discountValue.toFixed(2)} na entrega`}
            {c.minOrderAmount ? ` · Mín. R$ ${c.minOrderAmount.toFixed(2)}` : ""}
            {c.maxUses ? ` · ${c.usedCount}/${c.maxUses} usos` : ` · ${c.usedCount} usos`}
            {c.validUntil ? ` · Válido até ${new Date(c.validUntil).toLocaleDateString("pt-BR")}` : ""}
          </p>
          {c.description && <p className="text-xs text-muted-foreground italic">{c.description}</p>}
        </div>
        <div className="flex gap-1.5 shrink-0">
          <Button variant="ghost" size="sm" onClick={() => openEditCoupon(c)}>Editar</Button>
          {c.isActive && (
            <Button variant="ghost" size="sm" className="text-destructive"
              onClick={() => revokeMut.mutate(c.id)}
              disabled={revokeMut.isPending}>
              Revogar
            </Button>
          )}
        </div>
      </div>
    ))}
  </div>

  {/* Coupon dialog */}
  <Dialog open={couponDialog} onOpenChange={setCouponDialog}>
    <DialogContent>
      <DialogHeader>
        <DialogTitle>{editingCoupon ? "Editar cupom" : "Novo cupom"}</DialogTitle>
      </DialogHeader>
      <div className="flex flex-col gap-3">
        {!editingCoupon && (
          <div>
            <Label>Código *</Label>
            <Input
              placeholder="BEMVINDO10"
              value={couponForm.code}
              onChange={(e) => setCouponForm((f) => ({ ...f, code: e.target.value.toUpperCase() }))}
            />
          </div>
        )}
        <div>
          <Label>Tipo de desconto *</Label>
          <Select
            value={couponForm.discountType}
            onValueChange={(v) => setCouponForm((f) => ({ ...f, discountType: v as CreateCouponRequest["discountType"] }))}
          >
            <SelectTrigger><SelectValue /></SelectTrigger>
            <SelectContent>
              <SelectItem value="Percentage">Porcentagem (%)</SelectItem>
              <SelectItem value="FixedAmount">Valor fixo (R$)</SelectItem>
              <SelectItem value="DeliveryFee">Taxa de entrega (R$)</SelectItem>
            </SelectContent>
          </Select>
        </div>
        <div>
          <Label>
            {couponForm.discountType === "Percentage" ? "Desconto (%) *" : "Desconto (R$) *"}
          </Label>
          <Input
            type="number" min="0" step="0.01"
            value={couponForm.discountValue}
            onChange={(e) => setCouponForm((f) => ({ ...f, discountValue: Number(e.target.value) }))}
          />
        </div>
        <div>
          <Label>Descrição (opcional)</Label>
          <Input
            value={couponForm.description ?? ""}
            onChange={(e) => setCouponForm((f) => ({ ...f, description: e.target.value }))}
          />
        </div>
        <div>
          <Label>Pedido mínimo (R$) — opcional</Label>
          <Input
            type="number" min="0" step="0.01"
            value={couponForm.minOrderAmount ?? ""}
            onChange={(e) => setCouponForm((f) => ({
              ...f, minOrderAmount: e.target.value ? Number(e.target.value) : undefined,
            }))}
          />
        </div>
        <div>
          <Label>Limite de usos — opcional</Label>
          <Input
            type="number" min="1"
            value={couponForm.maxUses ?? ""}
            onChange={(e) => setCouponForm((f) => ({
              ...f, maxUses: e.target.value ? Number(e.target.value) : undefined,
            }))}
          />
        </div>
        <div>
          <Label>Válido até — opcional</Label>
          <Input
            type="date"
            value={couponForm.validUntil ?? ""}
            onChange={(e) => setCouponForm((f) => ({
              ...f, validUntil: e.target.value || undefined,
            }))}
          />
        </div>
      </div>
      <DialogFooter>
        <Button variant="outline" onClick={() => setCouponDialog(false)}>Cancelar</Button>
        <Button
          onClick={handleSaveCoupon}
          disabled={createMut.isPending || updateMut.isPending}
        >
          {createMut.isPending || updateMut.isPending ? "Salvando..." : "Salvar"}
        </Button>
      </DialogFooter>
    </DialogContent>
  </Dialog>
</TabsContent>
```

- [ ] **Step 3: Build verify**

```bash
cd nexo-main
npm run build -- --mode development 2>&1 | head -30
```

Expected: 0 TypeScript errors.

- [ ] **Step 4: Commit**

```bash
git add src/modules/restaurante/pages/PortalSetupPage.tsx
git commit -m "feat(restaurante): coupons management tab in portal settings"
```

---

## Task 13 — Final build + deploy

- [ ] **Step 1: Full backend build**

```bash
cd nexo-backend
dotnet build
```

Expected: 0 errors, 0 warnings (or only pre-existing warnings).

- [ ] **Step 2: Run integration tests**

```bash
dotnet test tests/Nexo.IntegrationTests --no-build
```

Expected: all pass.

- [ ] **Step 3: Full frontend build**

```bash
cd nexo-main
npm run build
```

Expected: build succeeds.

- [ ] **Step 4: Push and deploy**

```bash
cd nexo-backend && git push origin master
cd nexo-main   && git push origin master
```

Railway will auto-deploy both.

- [ ] **Step 5: Smoke test on production**

1. Open restaurant portal in browser.
2. Add items to cart, click "Ver pedido".
3. Select "Entrega", enter a valid Fortaleza CEP (e.g. 60130-240 = Aldeota).
4. Verify address fills in and fee shows.
5. Enter a coupon code — verify breakdown updates.
6. Complete order — verify it reaches `/rastrear/{token}`.
7. In PortalSetupPage → "Zonas de Entrega" — enable a neighborhood, set fee, save.
8. In PortalSetupPage → "Cupons" — create a coupon, verify it appears in list.

---

## Self-Review

**Spec coverage:**
- ✅ CEP lookup via ViaCEP → bairro match → zone fee
- ✅ Coupon field with breakdown (items subtotal, discount, delivery fee, total)
- ✅ Restaurant owner: configure neighborhoods + fees
- ✅ Coupon types: Percentage, FixedAmount, DeliveryFee
- ✅ Coupon conditions: MinOrderAmount, MinDeliveryFee, RestrictToNeighborhoods, IsFirstOrderOnly, RestrictToCustomerPhone, MaxUses, ValidFrom/ValidUntil
- ✅ Server-side validation only (delivery fee never trusted from client)
- ✅ CouponUsage recorded on each order
- ✅ Revoke coupon

**Placeholder scan:** All code steps are complete. No TBDs.

**Type consistency:** `DeliveryZoneDto`, `CouponDto`, `CreateCouponRequest` are defined once (in backend DTOs and frontend API files) and used consistently throughout.
