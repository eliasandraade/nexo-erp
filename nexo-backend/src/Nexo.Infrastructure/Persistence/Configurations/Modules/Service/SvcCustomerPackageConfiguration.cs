using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nexo.Domain.Entities;
using Nexo.Domain.Modules.Service;

namespace Nexo.Infrastructure.Persistence.Configurations.Modules.Service;

public class SvcCustomerPackageConfiguration : IEntityTypeConfiguration<SvcCustomerPackage>
{
    public void Configure(EntityTypeBuilder<SvcCustomerPackage> builder)
    {
        builder.ConfigureStoreScopedSvcEntityNoActive("svc_customer_packages");

        builder.Property(x => x.Code).HasColumnName("code").HasMaxLength(40).IsRequired();
        builder.Property(x => x.PackageId).HasColumnName("package_id").IsRequired();
        builder.Property(x => x.CustomerId).HasColumnName("customer_id").IsRequired();
        builder.Property(x => x.SubjectId).HasColumnName("subject_id");
        builder.Property(x => x.Status)
            .HasColumnName("status").HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.StartsAt).HasColumnName("starts_at").HasColumnType("timestamptz").IsRequired();
        builder.Property(x => x.ExpiresAt).HasColumnName("expires_at").HasColumnType("timestamptz");
        builder.Property(x => x.PriceSnapshot).HasColumnName("price_snapshot").HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(x => x.Notes).HasColumnName("notes").HasMaxLength(2000);

        builder.HasMany(x => x.Items)
            .WithOne()
            .HasForeignKey(x => x.CustomerPackageId)
            .HasConstraintName("fk_svc_customer_package_items_cp")
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<SvcPackage>().WithMany().HasForeignKey(x => x.PackageId)
            .HasConstraintName("fk_svc_customer_packages_packages").OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Customer>().WithMany().HasForeignKey(x => x.CustomerId)
            .HasConstraintName("fk_svc_customer_packages_customers").OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<SvcSubject>().WithMany().HasForeignKey(x => x.SubjectId)
            .HasConstraintName("fk_svc_customer_packages_subjects").OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.CustomerId).HasDatabaseName("ix_svc_customer_packages_customer_id");
        builder.HasIndex(x => x.SubjectId).HasDatabaseName("ix_svc_customer_packages_subject_id");
        builder.HasIndex(x => x.PackageId).HasDatabaseName("ix_svc_customer_packages_package_id");
        builder.HasIndex("TenantId", "StoreId", "Status").HasDatabaseName("ix_svc_customer_packages_tenant_store_status");
    }
}
