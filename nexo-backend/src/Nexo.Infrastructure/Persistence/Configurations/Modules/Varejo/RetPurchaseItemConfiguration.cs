using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nexo.Domain.Modules.Varejo;

namespace Nexo.Infrastructure.Persistence.Configurations.Modules.Varejo;

public class RetPurchaseItemConfiguration : IEntityTypeConfiguration<RetPurchaseItem>
{
    public void Configure(EntityTypeBuilder<RetPurchaseItem> builder)
    {
        builder.ToTable("ret_purchase_items", "nexo");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");

        builder.Property(x => x.TenantId)
            .HasColumnName("tenant_id")
            .IsRequired();

        builder.Property(x => x.PurchaseId)
            .HasColumnName("purchase_id")
            .IsRequired();

        builder.Property(x => x.ProductId)
            .HasColumnName("product_id")
            .IsRequired();

        builder.Property(x => x.Quantity)
            .HasColumnName("quantity")
            .HasColumnType("numeric(18,4)")
            .IsRequired();

        builder.Property(x => x.UnitCost)
            .HasColumnName("unit_cost")
            .HasColumnType("numeric(18,4)")
            .IsRequired();

        builder.Property(x => x.Total)
            .HasColumnName("total")
            .HasColumnType("numeric(18,2)")
            .IsRequired();

        builder.Property(x => x.Notes)
            .HasColumnName("notes")
            .HasMaxLength(500);

        builder.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamptz")
            .IsRequired();

        builder.Property(x => x.UpdatedAt)
            .HasColumnName("updated_at")
            .HasColumnType("timestamptz")
            .IsRequired();

        // FK: produto
        builder.HasOne<Nexo.Domain.Entities.Product>()
            .WithMany()
            .HasForeignKey(x => x.ProductId)
            .HasConstraintName("fk_ret_purchase_items_products")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Nexo.Domain.Entities.Tenant>()
            .WithMany()
            .HasForeignKey(x => x.TenantId)
            .HasConstraintName("fk_ret_purchase_items_tenants")
            .OnDelete(DeleteBehavior.Cascade);

        // Indexes
        builder.HasIndex(x => new { x.TenantId, x.PurchaseId })
            .HasDatabaseName("ix_ret_purchase_items_tenant_id_purchase_id");

        builder.HasIndex(x => new { x.TenantId, x.ProductId })
            .HasDatabaseName("ix_ret_purchase_items_tenant_id_product_id");
    }
}
