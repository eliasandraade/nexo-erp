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
