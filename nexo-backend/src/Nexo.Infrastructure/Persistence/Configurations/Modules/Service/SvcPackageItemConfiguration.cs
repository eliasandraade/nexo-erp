using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nexo.Domain.Modules.Service;

namespace Nexo.Infrastructure.Persistence.Configurations.Modules.Service;

public class SvcPackageItemConfiguration : IEntityTypeConfiguration<SvcPackageItem>
{
    public void Configure(EntityTypeBuilder<SvcPackageItem> builder)
    {
        builder.ConfigureStoreScopedSvcEntityNoActive("svc_package_items");

        builder.Property(x => x.PackageId).HasColumnName("package_id").IsRequired();
        builder.Property(x => x.CatalogItemId).HasColumnName("catalog_item_id").IsRequired();
        builder.Property(x => x.NameSnapshot).HasColumnName("name_snapshot").HasMaxLength(200).IsRequired();
        builder.Property(x => x.IncludedQuantity).HasColumnName("included_quantity").HasColumnType("numeric(18,3)").IsRequired();

        builder.HasOne<SvcCatalogItem>().WithMany().HasForeignKey(x => x.CatalogItemId)
            .HasConstraintName("fk_svc_package_items_catalog_items").OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.PackageId).HasDatabaseName("ix_svc_package_items_package_id");
    }
}
