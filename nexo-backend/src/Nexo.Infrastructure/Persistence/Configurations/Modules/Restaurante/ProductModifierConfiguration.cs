using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nexo.Domain.Modules.Restaurante;

namespace Nexo.Infrastructure.Persistence.Configurations.Modules.Restaurante;

public class ProductModifierConfiguration : IEntityTypeConfiguration<ProductModifier>
{
    public void Configure(EntityTypeBuilder<ProductModifier> builder)
    {
        builder.ToTable("product_modifiers", "nexo");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.GroupId).HasColumnName("group_id").IsRequired();
        builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(100).IsRequired();
        builder.Property(x => x.PriceAdjustment).HasColumnName("price_adjustment")
            .HasColumnType("numeric(18,2)").HasDefaultValue(0m).IsRequired();
        builder.Property(x => x.SortOrder).HasColumnName("sort_order").HasDefaultValue((short)0).IsRequired();
        builder.Property(x => x.IsActive).HasColumnName("is_active").HasDefaultValue(true).IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamptz").IsRequired();

        builder.HasOne<Nexo.Domain.Entities.Tenant>()
            .WithMany()
            .HasForeignKey(x => x.TenantId)
            .HasConstraintName("fk_product_modifiers_tenants")
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => new { x.TenantId, x.GroupId })
            .HasDatabaseName("ix_product_modifiers_tenant_group");

        builder.HasIndex(x => x.GroupId)
            .HasDatabaseName("ix_product_modifiers_group_id");
    }
}
