using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nexo.Domain.Entities;
using Nexo.Domain.Modules.Service;

namespace Nexo.Infrastructure.Persistence.Configurations.Modules.Service;

public class SvcProfessionalConfiguration : IEntityTypeConfiguration<SvcProfessional>
{
    public void Configure(EntityTypeBuilder<SvcProfessional> builder)
    {
        builder.ToTable("svc_professionals", "nexo");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.StoreId).HasColumnName("store_id").IsRequired();
        builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
        builder.Property(x => x.Role).HasColumnName("role").HasMaxLength(100);
        builder.Property(x => x.Specialty).HasColumnName("specialty").HasMaxLength(150);
        builder.Property(x => x.Color).HasColumnName("color").HasMaxLength(20);
        builder.Property(x => x.Phone).HasColumnName("phone").HasMaxLength(30);
        builder.Property(x => x.Email).HasColumnName("email").HasMaxLength(200);
        builder.Property(x => x.DefaultCommissionPercent)
            .HasColumnName("default_commission_percent").HasColumnType("numeric(5,2)");
        builder.Property(x => x.UserId).HasColumnName("user_id");
        builder.Property(x => x.IsActive).HasColumnName("is_active").HasDefaultValue(true).IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamptz").IsRequired();

        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(x => x.TenantId)
            .HasConstraintName("fk_svc_professionals_tenants")
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Store>()
            .WithMany()
            .HasForeignKey(x => x.StoreId)
            .HasConstraintName("fk_svc_professionals_stores")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.StoreId).HasDatabaseName("ix_svc_professionals_store_id");
        builder.HasIndex(x => new { x.TenantId, x.StoreId, x.IsActive })
            .HasDatabaseName("ix_svc_professionals_tenant_store_active");
    }
}
