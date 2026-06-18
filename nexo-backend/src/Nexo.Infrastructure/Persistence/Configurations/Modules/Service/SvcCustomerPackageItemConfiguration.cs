using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nexo.Domain.Modules.Service;

namespace Nexo.Infrastructure.Persistence.Configurations.Modules.Service;

public class SvcCustomerPackageItemConfiguration : IEntityTypeConfiguration<SvcCustomerPackageItem>
{
    public void Configure(EntityTypeBuilder<SvcCustomerPackageItem> builder)
    {
        builder.ConfigureStoreScopedSvcEntityNoActive("svc_customer_package_items");

        builder.Property(x => x.CustomerPackageId).HasColumnName("customer_package_id").IsRequired();
        builder.Property(x => x.CatalogItemId).HasColumnName("catalog_item_id").IsRequired();
        builder.Property(x => x.NameSnapshot).HasColumnName("name_snapshot").HasMaxLength(200).IsRequired();
        builder.Property(x => x.TotalQuantity).HasColumnName("total_quantity").HasColumnType("numeric(18,3)").IsRequired();
        builder.Property(x => x.RemainingQuantity).HasColumnName("remaining_quantity").HasColumnType("numeric(18,3)").IsRequired();

        builder.HasOne<SvcCatalogItem>().WithMany().HasForeignKey(x => x.CatalogItemId)
            .HasConstraintName("fk_svc_customer_package_items_catalog_items").OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.CustomerPackageId).HasDatabaseName("ix_svc_customer_package_items_cp_id");
        builder.HasIndex(x => x.CatalogItemId).HasDatabaseName("ix_svc_customer_package_items_catalog_id");
    }
}
