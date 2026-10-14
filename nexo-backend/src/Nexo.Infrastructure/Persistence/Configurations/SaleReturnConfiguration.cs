using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nexo.Domain.Entities;

namespace Nexo.Infrastructure.Persistence.Configurations;

public class SaleReturnConfiguration : IEntityTypeConfiguration<SaleReturn>
{
    public void Configure(EntityTypeBuilder<SaleReturn> builder)
    {
        builder.ToTable("sale_returns", t =>
            t.HasCheckConstraint("ck_sale_returns_refund_positive", "refund_amount > 0"));

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.SaleId).HasColumnName("sale_id").IsRequired();
        builder.Property(x => x.RefundMethod).HasColumnName("refund_method")
            .HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.RefundAmount).HasColumnName("refund_amount")
            .HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(x => x.Reason).HasColumnName("reason").HasMaxLength(500).IsRequired();
        builder.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key");
        builder.Property(x => x.CashSessionId).HasColumnName("cash_session_id");
        builder.Property(x => x.CreatedByUserId).HasColumnName("created_by_user_id").IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamptz").IsRequired();

        builder.HasIndex(x => new { x.TenantId, x.SaleId });
        // A retried devolução (same client key) can never refund twice.
        builder.HasIndex(x => new { x.TenantId, x.IdempotencyKey })
            .IsUnique()
            .HasFilter("idempotency_key IS NOT NULL")
            .HasDatabaseName("ux_sale_returns_idempotency");

        builder.HasMany(x => x.Items)
            .WithOne()
            .HasForeignKey(i => i.SaleReturnId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(x => x.Items).HasField("_items").UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasOne<Sale>().WithMany().HasForeignKey(x => x.SaleId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class SaleReturnItemConfiguration : IEntityTypeConfiguration<SaleReturnItem>
{
    public void Configure(EntityTypeBuilder<SaleReturnItem> builder)
    {
        builder.ToTable("sale_return_items", t =>
            t.HasCheckConstraint("ck_sale_return_items_quantity_positive", "quantity > 0"));

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.SaleReturnId).HasColumnName("sale_return_id").IsRequired();
        builder.Property(x => x.SaleItemId).HasColumnName("sale_item_id").IsRequired();
        builder.Property(x => x.ProductId).HasColumnName("product_id").IsRequired();
        builder.Property(x => x.Quantity).HasColumnName("quantity").HasColumnType("numeric(18,4)").IsRequired();
        builder.Property(x => x.Amount).HasColumnName("amount").HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamptz").IsRequired();

        builder.HasIndex(x => new { x.TenantId, x.SaleItemId });
        builder.HasOne<SaleItem>().WithMany().HasForeignKey(x => x.SaleItemId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Cascade);
    }
}
