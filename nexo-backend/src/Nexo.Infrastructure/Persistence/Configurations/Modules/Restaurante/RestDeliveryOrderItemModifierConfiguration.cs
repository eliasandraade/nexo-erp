using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nexo.Domain.Modules.Restaurante;

namespace Nexo.Infrastructure.Persistence.Configurations.Modules.Restaurante;

public class RestDeliveryOrderItemModifierConfiguration
    : IEntityTypeConfiguration<RestDeliveryOrderItemModifier>
{
    public void Configure(EntityTypeBuilder<RestDeliveryOrderItemModifier> builder)
    {
        builder.ToTable("rest_delivery_order_item_modifiers", "nexo");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamptz").IsRequired();

        builder.Property(x => x.DeliveryOrderItemId).HasColumnName("delivery_order_item_id").IsRequired();
        builder.Property(x => x.ModifierId).HasColumnName("modifier_id");
        builder.Property(x => x.ExternalModifierId).HasColumnName("external_modifier_id").HasMaxLength(100);
        builder.Property(x => x.LabelSnapshot).HasColumnName("label_snapshot").HasMaxLength(200).IsRequired();
        builder.Property(x => x.PriceSnapshot).HasColumnName("price_snapshot")
            .HasColumnType("numeric(18,4)").IsRequired();

        builder.HasOne<Nexo.Domain.Entities.Tenant>()
            .WithMany()
            .HasForeignKey(x => x.TenantId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => x.DeliveryOrderItemId)
            .HasDatabaseName("ix_rest_delivery_order_item_modifiers_item_id");
        builder.HasIndex(x => x.TenantId)
            .HasDatabaseName("ix_rest_delivery_order_item_modifiers_tenant_id");
    }
}
