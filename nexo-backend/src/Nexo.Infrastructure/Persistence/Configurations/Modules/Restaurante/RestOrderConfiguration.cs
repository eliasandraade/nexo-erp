using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nexo.Domain.Modules.Restaurante;

namespace Nexo.Infrastructure.Persistence.Configurations.Modules.Restaurante;

public class RestOrderConfiguration : IEntityTypeConfiguration<RestOrder>
{
    public void Configure(EntityTypeBuilder<RestOrder> builder)
    {
        builder.ToTable("rest_orders", "nexo");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.StoreId).HasColumnName("store_id").IsRequired();
        builder.Property(x => x.OrderNumber).HasColumnName("order_number").IsRequired();
        builder.Property(x => x.Status)
            .HasColumnName("status")
            .HasMaxLength(20)
            .HasConversion<string>()
            .IsRequired();
        builder.Property(x => x.TableId).HasColumnName("table_id").IsRequired();
        builder.Property(x => x.WaiterId).HasColumnName("waiter_id").IsRequired();
        builder.Property(x => x.CustomerId).HasColumnName("customer_id");
        builder.Property(x => x.SaleId).HasColumnName("sale_id");
        builder.Property(x => x.Notes).HasColumnName("notes").HasMaxLength(500);
        builder.Property(x => x.OpenedAt).HasColumnName("opened_at").HasColumnType("timestamptz").IsRequired();
        builder.Property(x => x.ClosedAt).HasColumnName("closed_at").HasColumnType("timestamptz");
        builder.Property(x => x.CancelledAt).HasColumnName("cancelled_at").HasColumnType("timestamptz");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamptz").IsRequired();

        // Computed properties — not persisted, must be ignored explicitly.
        builder.Ignore(x => x.ActiveItems);

        // Items — backed field
        builder.HasMany(x => x.Items)
            .WithOne(x => x.Order)
            .HasForeignKey(x => x.OrderId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.Table)
            .WithMany()
            .HasForeignKey(x => x.TableId)
            .HasConstraintName("fk_rest_orders_tables")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Nexo.Domain.Entities.Sale>()
            .WithMany()
            .HasForeignKey(x => x.SaleId)
            .HasConstraintName("fk_rest_orders_sales")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Nexo.Domain.Entities.Tenant>()
            .WithMany()
            .HasForeignKey(x => x.TenantId)
            .HasConstraintName("fk_rest_orders_tenants")
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Nexo.Domain.Entities.Store>()
            .WithMany()
            .HasForeignKey(x => x.StoreId)
            .HasConstraintName("fk_rest_orders_stores")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.StoreId).HasDatabaseName("ix_rest_orders_store_id");

        builder.HasIndex(x => new { x.TenantId, x.StoreId, x.OrderNumber })
            .IsUnique()
            .HasDatabaseName("ix_rest_orders_tenant_store_number");

        builder.HasIndex(x => new { x.TenantId, x.StoreId, x.Status })
            .HasDatabaseName("ix_rest_orders_tenant_store_status");

        builder.HasIndex(x => new { x.TenantId, x.StoreId, x.TableId })
            .HasDatabaseName("ix_rest_orders_tenant_store_table");

        builder.HasIndex(x => new { x.TenantId, x.StoreId, x.CreatedAt })
            .HasDatabaseName("ix_rest_orders_tenant_store_created_at");
    }
}
