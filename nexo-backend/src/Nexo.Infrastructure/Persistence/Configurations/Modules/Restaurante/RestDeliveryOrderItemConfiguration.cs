using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nexo.Domain.Modules.Restaurante;

namespace Nexo.Infrastructure.Persistence.Configurations.Modules.Restaurante;

public class RestDeliveryOrderItemConfiguration : IEntityTypeConfiguration<RestDeliveryOrderItem>
{
    public void Configure(EntityTypeBuilder<RestDeliveryOrderItem> builder)
    {
        builder.ToTable("rest_delivery_order_items", "nexo");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamptz").IsRequired();

        builder.Property(x => x.DeliveryOrderId).HasColumnName("delivery_order_id").IsRequired();
        builder.Property(x => x.ProductId).HasColumnName("product_id");
        builder.Property(x => x.ExternalProductId).HasColumnName("external_product_id").HasMaxLength(100);
        builder.Property(x => x.ProductNameSnapshot).HasColumnName("product_name_snapshot")
            .HasMaxLength(300).IsRequired();
        builder.Property(x => x.UnitPriceSnapshot).HasColumnName("unit_price_snapshot")
            .HasColumnType("numeric(18,4)").IsRequired();
        builder.Property(x => x.Quantity).HasColumnName("quantity")
            .HasColumnType("numeric(18,4)").IsRequired();
        builder.Property(x => x.Notes).HasColumnName("notes").HasMaxLength(500);

        builder.HasMany(x => x.Modifiers)
            .WithOne()
            .HasForeignKey(x => x.DeliveryOrderItemId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Nexo.Domain.Entities.Tenant>()
            .WithMany()
            .HasForeignKey(x => x.TenantId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => x.DeliveryOrderId)
            .HasDatabaseName("ix_rest_delivery_order_items_delivery_order_id");
        builder.HasIndex(x => x.TenantId)
            .HasDatabaseName("ix_rest_delivery_order_items_tenant_id");
    }
}
