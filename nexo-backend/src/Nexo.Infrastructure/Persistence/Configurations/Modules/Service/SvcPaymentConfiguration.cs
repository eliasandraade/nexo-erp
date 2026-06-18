using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nexo.Domain.Entities;
using Nexo.Domain.Modules.Service;

namespace Nexo.Infrastructure.Persistence.Configurations.Modules.Service;

public class SvcPaymentConfiguration : IEntityTypeConfiguration<SvcPayment>
{
    public void Configure(EntityTypeBuilder<SvcPayment> builder)
    {
        builder.ConfigureStoreScopedSvcEntityNoActive("svc_payments");

        builder.Property(x => x.CustomerId).HasColumnName("customer_id").IsRequired();
        builder.Property(x => x.OrderId).HasColumnName("order_id");
        builder.Property(x => x.CustomerPackageId).HasColumnName("customer_package_id");
        builder.Property(x => x.Amount).HasColumnName("amount").HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(x => x.Method).HasColumnName("method").HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.PaidAt).HasColumnName("paid_at").HasColumnType("timestamptz").IsRequired();
        builder.Property(x => x.ExternalReference).HasColumnName("external_reference").HasMaxLength(200);
        builder.Property(x => x.Notes).HasColumnName("notes").HasMaxLength(2000);
        builder.Property(x => x.VoidReason).HasColumnName("void_reason").HasMaxLength(500);
        builder.Property(x => x.VoidedAt).HasColumnName("voided_at").HasColumnType("timestamptz");

        builder.HasOne<Customer>().WithMany().HasForeignKey(x => x.CustomerId)
            .HasConstraintName("fk_svc_payments_customers").OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<SvcOrder>().WithMany().HasForeignKey(x => x.OrderId)
            .HasConstraintName("fk_svc_payments_orders").OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<SvcCustomerPackage>().WithMany().HasForeignKey(x => x.CustomerPackageId)
            .HasConstraintName("fk_svc_payments_customer_packages").OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.CustomerId).HasDatabaseName("ix_svc_payments_customer_id");
        builder.HasIndex(x => x.OrderId).HasDatabaseName("ix_svc_payments_order_id");
        builder.HasIndex(x => x.CustomerPackageId).HasDatabaseName("ix_svc_payments_customer_package_id");
        builder.HasIndex("TenantId", "StoreId", "Status").HasDatabaseName("ix_svc_payments_tenant_store_status");
        builder.HasIndex(x => x.PaidAt).HasDatabaseName("ix_svc_payments_paid_at");
    }
}
