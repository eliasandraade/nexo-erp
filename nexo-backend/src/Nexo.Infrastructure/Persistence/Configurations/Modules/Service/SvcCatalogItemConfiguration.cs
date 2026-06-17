using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nexo.Domain.Entities;
using Nexo.Domain.Modules.Service;

namespace Nexo.Infrastructure.Persistence.Configurations.Modules.Service;

public class SvcCatalogItemConfiguration : IEntityTypeConfiguration<SvcCatalogItem>
{
    public void Configure(EntityTypeBuilder<SvcCatalogItem> builder)
    {
        builder.ToTable("svc_catalog_items", "nexo");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.StoreId).HasColumnName("store_id").IsRequired();
        builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
        builder.Property(x => x.Description).HasColumnName("description").HasMaxLength(1000);
        builder.Property(x => x.Category).HasColumnName("category").HasMaxLength(100);
        builder.Property(x => x.DurationMinutes).HasColumnName("duration_minutes").IsRequired();
        builder.Property(x => x.Price).HasColumnName("price").HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(x => x.CommissionPercent)
            .HasColumnName("commission_percent").HasColumnType("numeric(5,2)");
        builder.Property(x => x.RequiresSubject).HasColumnName("requires_subject")
            .HasDefaultValue(false).IsRequired();
        builder.Property(x => x.IsActive).HasColumnName("is_active").HasDefaultValue(true).IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamptz").IsRequired();

        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(x => x.TenantId)
            .HasConstraintName("fk_svc_catalog_items_tenants")
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Store>()
            .WithMany()
            .HasForeignKey(x => x.StoreId)
            .HasConstraintName("fk_svc_catalog_items_stores")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.StoreId).HasDatabaseName("ix_svc_catalog_items_store_id");
        builder.HasIndex(x => new { x.TenantId, x.StoreId, x.IsActive })
            .HasDatabaseName("ix_svc_catalog_items_tenant_store_active");
    }
}
