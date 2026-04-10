using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nexo.Domain.Modules.Restaurante;

namespace Nexo.Infrastructure.Persistence.Configurations.Modules.Restaurante;

public class RestTableConfiguration : IEntityTypeConfiguration<RestTable>
{
    public void Configure(EntityTypeBuilder<RestTable> builder)
    {
        builder.ToTable("rest_tables", "nexo");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.AreaId).HasColumnName("area_id").IsRequired();
        builder.Property(x => x.Number).HasColumnName("number").HasMaxLength(50).IsRequired();
        builder.Property(x => x.Capacity).HasColumnName("capacity").IsRequired();
        builder.Property(x => x.Status)
            .HasColumnName("status")
            .HasMaxLength(20)
            .HasConversion<string>()
            .IsRequired();
        builder.Property(x => x.IsActive).HasColumnName("is_active").HasDefaultValue(true).IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamptz").IsRequired();

        builder.HasOne(x => x.Area)
            .WithMany(x => x.Tables)
            .HasForeignKey(x => x.AreaId)
            .HasConstraintName("fk_rest_tables_areas")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Nexo.Domain.Entities.Tenant>()
            .WithMany()
            .HasForeignKey(x => x.TenantId)
            .HasConstraintName("fk_rest_tables_tenants")
            .OnDelete(DeleteBehavior.Cascade);

        // Número de mesa único por tenant
        builder.HasIndex(x => new { x.TenantId, x.Number })
            .IsUnique()
            .HasDatabaseName("ix_rest_tables_tenant_number");

        builder.HasIndex(x => new { x.TenantId, x.AreaId })
            .HasDatabaseName("ix_rest_tables_tenant_area");

        builder.HasIndex(x => new { x.TenantId, x.Status })
            .HasDatabaseName("ix_rest_tables_tenant_status");
    }
}
