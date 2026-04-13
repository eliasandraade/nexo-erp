using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nexo.Domain.Modules.Restaurante;

namespace Nexo.Infrastructure.Persistence.Configurations.Modules.Restaurante;

public class RestOrderItemModifierConfiguration : IEntityTypeConfiguration<RestOrderItemModifier>
{
    public void Configure(EntityTypeBuilder<RestOrderItemModifier> builder)
    {
        builder.ToTable("rest_order_item_modifiers", "nexo");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.OrderItemId).HasColumnName("order_item_id").IsRequired();
        builder.Property(x => x.ModifierId).HasColumnName("modifier_id").IsRequired();
        builder.Property(x => x.LabelSnapshot).HasColumnName("label_snapshot").HasMaxLength(100).IsRequired();
        builder.Property(x => x.PriceSnapshot).HasColumnName("price_snapshot")
            .HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamptz").IsRequired();

        builder.HasOne<Nexo.Domain.Entities.Tenant>()
            .WithMany()
            .HasForeignKey(x => x.TenantId)
            .HasConstraintName("fk_rest_order_item_modifiers_tenants")
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => x.OrderItemId)
            .HasDatabaseName("ix_rest_order_item_modifiers_order_item_id");

        builder.HasIndex(x => new { x.TenantId, x.OrderItemId })
            .HasDatabaseName("ix_rest_order_item_modifiers_item");
    }
}
