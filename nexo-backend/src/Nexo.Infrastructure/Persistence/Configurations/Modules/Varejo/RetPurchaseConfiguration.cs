using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nexo.Domain.Modules.Varejo;

namespace Nexo.Infrastructure.Persistence.Configurations.Modules.Varejo;

public class RetPurchaseConfiguration : IEntityTypeConfiguration<RetPurchase>
{
    public void Configure(EntityTypeBuilder<RetPurchase> builder)
    {
        builder.ToTable("ret_purchases", "nexo");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");

        builder.Property(x => x.TenantId)
            .HasColumnName("tenant_id")
            .IsRequired();

        builder.Property(x => x.PurchaseNumber)
            .HasColumnName("purchase_number")
            .IsRequired();

        builder.Property(x => x.Status)
            .HasColumnName("status")
            .HasMaxLength(20)
            .HasConversion<string>()
            .IsRequired();

        builder.Property(x => x.SupplierId)
            .HasColumnName("supplier_id")
            .IsRequired();

        builder.Property(x => x.UserId)
            .HasColumnName("user_id");

        builder.Property(x => x.TotalAmount)
            .HasColumnName("total_amount")
            .HasColumnType("numeric(18,2)")
            .IsRequired();

        builder.Property(x => x.InvoiceNumber)
            .HasColumnName("invoice_number")
            .HasMaxLength(100);

        builder.Property(x => x.ReceivedAt)
            .HasColumnName("received_at")
            .HasColumnType("timestamptz");

        builder.Property(x => x.ConfirmedAt)
            .HasColumnName("confirmed_at")
            .HasColumnType("timestamptz");

        builder.Property(x => x.CancelledAt)
            .HasColumnName("cancelled_at")
            .HasColumnType("timestamptz");

        builder.Property(x => x.Notes)
            .HasColumnName("notes")
            .HasMaxLength(1000);

        builder.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamptz")
            .IsRequired();

        builder.Property(x => x.UpdatedAt)
            .HasColumnName("updated_at")
            .HasColumnType("timestamptz")
            .IsRequired();

        // Navigation: items (owned collection, backed field)
        builder.HasMany(x => x.Items)
            .WithOne(x => x.Purchase)
            .HasForeignKey(x => x.PurchaseId)
            .OnDelete(DeleteBehavior.Cascade);

        // FKs
        builder.HasOne<Nexo.Domain.Entities.Supplier>()
            .WithMany()
            .HasForeignKey(x => x.SupplierId)
            .HasConstraintName("fk_ret_purchases_suppliers")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Nexo.Domain.Entities.Tenant>()
            .WithMany()
            .HasForeignKey(x => x.TenantId)
            .HasConstraintName("fk_ret_purchases_tenants")
            .OnDelete(DeleteBehavior.Cascade);

        // Indexes
        builder.HasIndex(x => new { x.TenantId, x.PurchaseNumber })
            .IsUnique()
            .HasDatabaseName("ix_ret_purchases_tenant_id_number");

        builder.HasIndex(x => new { x.TenantId, x.Status })
            .HasDatabaseName("ix_ret_purchases_tenant_id_status");

        builder.HasIndex(x => new { x.TenantId, x.SupplierId })
            .HasDatabaseName("ix_ret_purchases_tenant_id_supplier");

        builder.HasIndex(x => new { x.TenantId, x.CreatedAt })
            .HasDatabaseName("ix_ret_purchases_tenant_id_created_at");
    }
}
