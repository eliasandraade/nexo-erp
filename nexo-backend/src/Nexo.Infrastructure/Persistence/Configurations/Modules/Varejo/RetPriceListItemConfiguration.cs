using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nexo.Domain.Modules.Varejo;

namespace Nexo.Infrastructure.Persistence.Configurations.Modules.Varejo;

public class RetPriceListItemConfiguration : IEntityTypeConfiguration<RetPriceListItem>
{
    public void Configure(EntityTypeBuilder<RetPriceListItem> builder)
    {
        builder.ToTable("ret_price_list_items", "nexo");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");

        builder.Property(x => x.TenantId)
            .HasColumnName("tenant_id")
            .IsRequired();

        builder.Property(x => x.PriceListId)
            .HasColumnName("price_list_id")
            .IsRequired();

        builder.Property(x => x.ProductId)
            .HasColumnName("product_id")
            .IsRequired();

        builder.Property(x => x.Price)
            .HasColumnName("price")
            .HasColumnType("numeric(18,4)")
            .IsRequired();

        builder.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamptz")
            .IsRequired();

        builder.Property(x => x.UpdatedAt)
            .HasColumnName("updated_at")
            .HasColumnType("timestamptz")
            .IsRequired();

        // Um produto pode aparecer no máximo uma vez por lista
        builder.HasIndex(x => new { x.PriceListId, x.ProductId })
            .IsUnique()
            .HasDatabaseName("ix_ret_price_list_items_list_product");

        builder.HasIndex(x => new { x.TenantId, x.ProductId })
            .HasDatabaseName("ix_ret_price_list_items_tenant_id_product");

        builder.HasOne<Nexo.Domain.Entities.Product>()
            .WithMany()
            .HasForeignKey(x => x.ProductId)
            .HasConstraintName("fk_ret_price_list_items_products")
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Nexo.Domain.Entities.Tenant>()
            .WithMany()
            .HasForeignKey(x => x.TenantId)
            .HasConstraintName("fk_ret_price_list_items_tenants")
            .OnDelete(DeleteBehavior.Cascade);
    }
}
