using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nexo.Domain.Entities;

namespace Nexo.Infrastructure.Persistence.Configurations;

public class AppSettingsConfiguration : IEntityTypeConfiguration<AppSettings>
{
    public void Configure(EntityTypeBuilder<AppSettings> builder)
    {
        builder.ToTable("app_settings");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");

        builder.Property(x => x.TenantId)
            .HasColumnName("tenant_id")
            .IsRequired();

        builder.Property(x => x.CompanySettingsJson)
            .HasColumnName("company_settings_json")
            .HasColumnType("jsonb")
            .HasDefaultValue("{}")
            .IsRequired();

        builder.Property(x => x.OperationSettingsJson)
            .HasColumnName("operation_settings_json")
            .HasColumnType("jsonb")
            .HasDefaultValue("{}")
            .IsRequired();

        builder.Property(x => x.InventorySettingsJson)
            .HasColumnName("inventory_settings_json")
            .HasColumnType("jsonb")
            .HasDefaultValue("{}")
            .IsRequired();

        builder.Property(x => x.CommissionSettingsJson)
            .HasColumnName("commission_settings_json")
            .HasColumnType("jsonb")
            .HasDefaultValue("{}")
            .IsRequired();

        builder.Property(x => x.PosSettingsJson)
            .HasColumnName("pos_settings_json")
            .HasColumnType("jsonb")
            .HasDefaultValue("{}")
            .IsRequired();

        builder.Property(x => x.SystemSettingsJson)
            .HasColumnName("system_settings_json")
            .HasColumnType("jsonb")
            .HasDefaultValue("{}")
            .IsRequired();

        builder.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamptz")
            .IsRequired();

        builder.Property(x => x.UpdatedAt)
            .HasColumnName("updated_at")
            .HasColumnType("timestamptz")
            .IsRequired();

        // One settings record per tenant
        builder.HasIndex(x => x.TenantId).IsUnique();

        builder.HasOne<Nexo.Domain.Entities.Tenant>()
            .WithMany()
            .HasForeignKey(x => x.TenantId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
