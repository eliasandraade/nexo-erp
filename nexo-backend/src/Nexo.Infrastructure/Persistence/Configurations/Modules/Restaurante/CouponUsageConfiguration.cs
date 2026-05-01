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
