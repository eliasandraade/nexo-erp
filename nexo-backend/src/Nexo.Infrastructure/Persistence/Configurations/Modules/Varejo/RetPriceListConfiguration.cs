using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nexo.Domain.Modules.Varejo;

namespace Nexo.Infrastructure.Persistence.Configurations.Modules.Varejo;

public class RetPriceListConfiguration : IEntityTypeConfiguration<RetPriceList>
{
    public void Configure(EntityTypeBuilder<RetPriceList> builder)
    {
        builder.ToTable("ret_price_lists", "nexo");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");

        builder.Property(x => x.TenantId)
            .HasColumnName("tenant_id")
            .IsRequired();

        builder.Property(x => x.Name)
            .HasColumnName("name")
            .HasMaxLength(150)
            .IsRequired();

        builder.Property(x => x.Description)
            .HasColumnName("description")
            .HasMaxLength(500);

        builder.Property(x => x.IsDefault)
            .HasColumnName("is_default")
            .HasDefaultValue(false)
            .IsRequired();

        builder.Property(x => x.IsActive)
            .HasColumnName("is_active")
            .HasDefaultValue(true)
            .IsRequired();

        builder.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamptz")
            .IsRequired();

        builder.Property(x => x.UpdatedAt)
            .HasColumnName("updated_at")
            .HasColumnType("timestamptz")
            .IsRequired();

        // Navigation: items
        builder.HasMany(x => x.Items)
            .WithOne(x => x.PriceList)
            .HasForeignKey(x => x.PriceListId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Nexo.Domain.Entities.Tenant>()
            .WithMany()
            .HasForeignKey(x => x.TenantId)
            .HasConstraintName("fk_ret_price_lists_tenants")
            .OnDelete(DeleteBehavior.Cascade);

        // Indexes
        builder.HasIndex(x => x.TenantId)
            .HasDatabaseName("ix_ret_price_lists_tenant_id");

        builder.HasIndex(x => new { x.TenantId, x.IsDefault })
            .HasDatabaseName("ix_ret_price_lists_tenant_id_is_default");
    }
}
