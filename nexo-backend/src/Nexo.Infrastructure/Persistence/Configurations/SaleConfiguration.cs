using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nexo.Domain.Entities;

namespace Nexo.Infrastructure.Persistence.Configurations;

public class SaleConfiguration : IEntityTypeConfiguration<Sale>
{
    public void Configure(EntityTypeBuilder<Sale> builder)
    {
        builder.ToTable("sales");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");

        builder.Property(x => x.TenantId)
            .HasColumnName("tenant_id")
            .IsRequired();

        builder.Property(x => x.Number)
            .HasColumnName("number")
            .IsRequired();

        builder.Property(x => x.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(x => x.CustomerId)
            .HasColumnName("customer_id");

        builder.Property(x => x.SoldByUserId)
            .HasColumnName("sold_by_user_id")
            .IsRequired();

        builder.Property(x => x.CashSessionId)
            .HasColumnName("cash_session_id");

        builder.Property(x => x.Subtotal)
            .HasColumnName("subtotal")
            .HasColumnType("numeric(18,2)")
            .IsRequired();

        builder.Property(x => x.DiscountAmount)
            .HasColumnName("discount_amount")
            .HasColumnType("numeric(18,2)")
            .IsRequired();

        builder.Property(x => x.TaxAmount)
            .HasColumnName("tax_amount")
            .HasColumnType("numeric(18,2)")
            .IsRequired();

        builder.Property(x => x.Total)
            .HasColumnName("total")
            .HasColumnType("numeric(18,2)")
            .IsRequired();

        builder.Property(x => x.Notes)
            .HasColumnName("notes")
            .HasMaxLength(500);

        builder.Property(x => x.ConfirmedAt)
            .HasColumnName("confirmed_at")
            .HasColumnType("timestamptz");

        builder.Property(x => x.PaidAt)
            .HasColumnName("paid_at")
            .HasColumnType("timestamptz");

        builder.Property(x => x.CancelledAt)
            .HasColumnName("cancelled_at")
            .HasColumnType("timestamptz");

        builder.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamptz")
            .IsRequired();

        builder.Property(x => x.UpdatedAt)
            .HasColumnName("updated_at")
            .HasColumnType("timestamptz")
            .IsRequired();

        builder.HasIndex(x => new { x.TenantId, x.Number }).IsUnique();
        builder.HasIndex(x => new { x.TenantId, x.Status });
        builder.HasIndex(x => new { x.TenantId, x.CreatedAt });
        builder.HasIndex(x => new { x.TenantId, x.CustomerId });

        builder.HasOne(x => x.Customer)
            .WithMany(x => x.Sales)
            .HasForeignKey(x => x.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.SoldBy)
            .WithMany()
            .HasForeignKey(x => x.SoldByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.CashSession)
            .WithMany(x => x.Sales)
            .HasForeignKey(x => x.CashSessionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Nexo.Domain.Entities.Tenant>()
            .WithMany()
            .HasForeignKey(x => x.TenantId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
