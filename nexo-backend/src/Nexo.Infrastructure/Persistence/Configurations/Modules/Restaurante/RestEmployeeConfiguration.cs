using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nexo.Domain.Modules.Restaurante;

namespace Nexo.Infrastructure.Persistence.Configurations.Modules.Restaurante;

public class RestEmployeeConfiguration : IEntityTypeConfiguration<RestEmployee>
{
    public void Configure(EntityTypeBuilder<RestEmployee> builder)
    {
        builder.ToTable("rest_employees", "nexo");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.StoreId).HasColumnName("store_id").IsRequired();
        builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
        builder.Property(x => x.Role).HasColumnName("role").HasMaxLength(100).IsRequired();
        builder.Property(x => x.AdmissionDate).HasColumnName("admission_date").IsRequired();
        builder.Property(x => x.MonthlySalary).HasColumnName("monthly_salary")
            .HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(x => x.Notes).HasColumnName("notes").HasMaxLength(1000);
        builder.Property(x => x.IsActive).HasColumnName("is_active")
            .HasDefaultValue(true).IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at")
            .HasColumnType("timestamptz").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at")
            .HasColumnType("timestamptz").IsRequired();

        builder.HasOne<Nexo.Domain.Entities.Tenant>()
            .WithMany()
            .HasForeignKey(x => x.TenantId)
            .HasConstraintName("fk_rest_employees_tenants")
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Nexo.Domain.Entities.Store>()
            .WithMany()
            .HasForeignKey(x => x.StoreId)
            .HasConstraintName("fk_rest_employees_stores")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.StoreId).HasDatabaseName("ix_rest_employees_store_id");
        builder.HasIndex(x => new { x.TenantId, x.StoreId, x.IsActive })
            .HasDatabaseName("ix_rest_employees_tenant_store_active");
    }
}
