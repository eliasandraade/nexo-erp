using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nexo.Domain.Entities;

namespace Nexo.Infrastructure.Persistence.Configurations;

public class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable("products");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");

        builder.Property(x => x.TenantId)
            .HasColumnName("tenant_id")
            .IsRequired();

        builder.Property(x => x.Code)
            .HasColumnName("code")
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(x => x.Barcode)
            .HasColumnName("barcode")
            .HasMaxLength(50);

        builder.Property(x => x.Name)
            .HasColumnName("name")
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(x => x.Description)
            .HasColumnName("description")
            .HasMaxLength(1000);

        builder.Property(x => x.CategoryId)
            .HasColumnName("category_id");

        builder.Property(x => x.Unit)
            .HasColumnName("unit")
            .HasConversion<string>()
            .HasMaxLength(10)
            .IsRequired();

        builder.Property(x => x.CostPrice)
            .HasColumnName("cost_price")
            .HasColumnType("numeric(18,4)")
            .IsRequired();

        builder.Property(x => x.SalePrice)
            .HasColumnName("sale_price")
            .HasColumnType("numeric(18,4)")
            .IsRequired();

        builder.Property(x => x.TrackStock)
            .HasColumnName("track_stock")
            .HasDefaultValue(true);

        builder.Property(x => x.MinStockQuantity)
            .HasColumnName("min_stock_quantity")
            .HasColumnType("numeric(18,4)");

        builder.Property(x => x.MaxStockQuantity)
            .HasColumnName("max_stock_quantity")
            .HasColumnType("numeric(18,4)");

        builder.Property(x => x.IsActive)
            .HasColumnName("is_active")
            .HasDefaultValue(true);

        builder.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamptz")
            .IsRequired();

        builder.Property(x => x.UpdatedAt)
            .HasColumnName("updated_at")
            .HasColumnType("timestamptz")
            .IsRequired();

        builder.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
        builder.HasIndex(x => new { x.TenantId, x.Barcode });
        builder.HasIndex(x => x.TenantId);

        builder.HasOne(x => x.Category)
            .WithMany(x => x.Products)
            .HasForeignKey(x => x.CategoryId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne<Nexo.Domain.Entities.Tenant>()
            .WithMany()
            .HasForeignKey(x => x.TenantId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
