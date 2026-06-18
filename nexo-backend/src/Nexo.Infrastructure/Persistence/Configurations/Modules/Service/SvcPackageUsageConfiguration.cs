using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nexo.Domain.Modules.Service;

namespace Nexo.Infrastructure.Persistence.Configurations.Modules.Service;

public class SvcPackageUsageConfiguration : IEntityTypeConfiguration<SvcPackageUsage>
{
    public void Configure(EntityTypeBuilder<SvcPackageUsage> builder)
    {
        builder.ConfigureStoreScopedSvcEntityNoActive("svc_package_usages");

        builder.Property(x => x.CustomerPackageId).HasColumnName("customer_package_id").IsRequired();
        builder.Property(x => x.CustomerPackageItemId).HasColumnName("customer_package_item_id").IsRequired();
        builder.Property(x => x.CatalogItemId).HasColumnName("catalog_item_id").IsRequired();
        builder.Property(x => x.OrderId).HasColumnName("order_id");
        builder.Property(x => x.OrderItemId).HasColumnName("order_item_id");
        builder.Property(x => x.Quantity).HasColumnName("quantity").HasColumnType("numeric(18,3)").IsRequired();
        builder.Property(x => x.Notes).HasColumnName("notes").HasMaxLength(2000);

        builder.HasOne<SvcCustomerPackage>().WithMany().HasForeignKey(x => x.CustomerPackageId)
            .HasConstraintName("fk_svc_package_usages_cp").OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<SvcCustomerPackageItem>().WithMany().HasForeignKey(x => x.CustomerPackageItemId)
            .HasConstraintName("fk_svc_package_usages_cp_item").OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<SvcCatalogItem>().WithMany().HasForeignKey(x => x.CatalogItemId)
            .HasConstraintName("fk_svc_package_usages_catalog_items").OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<SvcOrder>().WithMany().HasForeignKey(x => x.OrderId)
            .HasConstraintName("fk_svc_package_usages_orders").OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<SvcOrderItem>().WithMany().HasForeignKey(x => x.OrderItemId)
            .HasConstraintName("fk_svc_package_usages_order_items").OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.CustomerPackageId).HasDatabaseName("ix_svc_package_usages_cp_id");
    }
}
