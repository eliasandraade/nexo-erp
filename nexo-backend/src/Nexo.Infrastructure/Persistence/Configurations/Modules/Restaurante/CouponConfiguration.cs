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
