using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nexo.Domain.Modules.Restaurante;

namespace Nexo.Infrastructure.Persistence.Configurations.Modules.Restaurante;

public class RestOrderItemConfiguration : IEntityTypeConfiguration<RestOrderItem>
{
    public void Configure(EntityTypeBuilder<RestOrderItem> builder)
    {
        builder.ToTable("rest_order_items", "nexo");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.OrderId).HasColumnName("order_id").IsRequired();
        builder.Property(x => x.ProductId).HasColumnName("product_id").IsRequired();
        builder.Property(x => x.Quantity).HasColumnName("quantity").HasColumnType("numeric(18,4)").IsRequired();
        builder.Property(x => x.UnitPrice).HasColumnName("unit_price").HasColumnType("numeric(18,4)").IsRequired();
        builder.Property(x => x.Total).HasColumnName("total").HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(x => x.Notes).HasColumnName("notes").HasMaxLength(500);
        builder.Property(x => x.Status)
            .HasColumnName("status")
            .HasMaxLength(20)
            .HasConversion<string>()
            .IsRequired();
        builder.Property(x => x.SentToKitchenAt).HasColumnName("sent_to_kitchen_at").HasColumnType("timestamptz");
        builder.Property(x => x.PreparedAt).HasColumnName("prepared_at").HasColumnType("timestamptz");
        builder.Property(x => x.DeliveredAt).HasColumnName("delivered_at").HasColumnType("timestamptz");
        builder.Property(x => x.CancelledAt).HasColumnName("cancelled_at").HasColumnType("timestamptz");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamptz").IsRequired();

        builder.HasOne(x => x.Product)
            .WithMany()
            .HasForeignKey(x => x.ProductId)
            .HasConstraintName("fk_rest_order_items_products")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Nexo.Domain.Entities.Tenant>()
            .WithMany()
            .HasForeignKey(x => x.TenantId)
            .HasConstraintName("fk_rest_order_items_tenants")
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => new { x.TenantId, x.OrderId })
            .HasDatabaseName("ix_rest_order_items_tenant_order");
        builder.HasIndex(x => new { x.TenantId, x.ProductId })
            .HasDatabaseName("ix_rest_order_items_tenant_product");
        builder.HasIndex(x => new { x.TenantId, x.Status })
            .HasDatabaseName("ix_rest_order_items_tenant_status");
    }
}
