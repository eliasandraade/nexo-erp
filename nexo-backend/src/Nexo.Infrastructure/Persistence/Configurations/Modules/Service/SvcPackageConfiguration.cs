using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nexo.Domain.Modules.Service;

namespace Nexo.Infrastructure.Persistence.Configurations.Modules.Service;

public class SvcPackageConfiguration : IEntityTypeConfiguration<SvcPackage>
{
    public void Configure(EntityTypeBuilder<SvcPackage> builder)
    {
        // Key, tenant/store columns + FKs, is_active, audit columns, tenant_store_active index.
        builder.ConfigureStoreScopedSvcEntity("svc_packages");

        builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
        builder.Property(x => x.Description).HasColumnName("description").HasMaxLength(1000);
        builder.Property(x => x.Price).HasColumnName("price").HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(x => x.ValidityDays).HasColumnName("validity_days");

        builder.HasMany(x => x.Items)
            .WithOne()
            .HasForeignKey(x => x.PackageId)
            .HasConstraintName("fk_svc_package_items_package")
            .OnDelete(DeleteBehavior.Cascade);
    }
}
