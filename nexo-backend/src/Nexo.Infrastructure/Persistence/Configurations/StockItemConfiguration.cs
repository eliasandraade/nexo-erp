using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nexo.Domain.Entities;

namespace Nexo.Infrastructure.Persistence.Configurations;

public class StockItemConfiguration : IEntityTypeConfiguration<StockItem>
{
    public void Configure(EntityTypeBuilder<StockItem> builder)
    {
        builder.ToTable("stock_items");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");

        builder.Property(x => x.TenantId)
            .HasColumnName("tenant_id")
            .IsRequired();

        builder.Property(x => x.StoreId)
            .HasColumnName("store_id")
            .IsRequired();

        builder.Property(x => x.ProductId)
            .HasColumnName("product_id")
            .IsRequired();

        builder.Property(x => x.CurrentQuantity)
            .HasColumnName("current_quantity")
            .HasColumnType("numeric(18,4)")
            .IsRequired();

        builder.Property(x => x.ReservedQuantity)
            .HasColumnName("reserved_quantity")
            .HasColumnType("numeric(18,4)")
            .IsRequired();

        builder.Property(x => x.LastMovementAt)
            .HasColumnName("last_movement_at")
            .HasColumnType("timestamptz");

        // PostgreSQL xmin system column — optimistic concurrency token.
        // EF will include "WHERE xmin = <original>" on every UPDATE.
        // DbUpdateConcurrencyException is thrown if another transaction modified the row first.
        builder.Property(x => x.RowVersion)
            .HasColumnName("xmin")
            .HasColumnType("xid")
            .IsRowVersion()
            .ValueGeneratedOnAddOrUpdate();

        builder.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamptz")
            .IsRequired();

        builder.Property(x => x.UpdatedAt)
            .HasColumnName("updated_at")
            .HasColumnType("timestamptz")
            .IsRequired();

        // One StockItem per product per store (not per tenant) — each store tracks its own stock
        builder.HasIndex(x => new { x.TenantId, x.StoreId, x.ProductId }).IsUnique();

        builder.HasOne(x => x.Product)
            .WithOne(x => x.StockItem)
            .HasForeignKey<StockItem>(x => x.ProductId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Nexo.Domain.Entities.Tenant>()
            .WithMany()
            .HasForeignKey(x => x.TenantId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Nexo.Domain.Entities.Store>()
            .WithMany()
            .HasForeignKey(x => x.StoreId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
