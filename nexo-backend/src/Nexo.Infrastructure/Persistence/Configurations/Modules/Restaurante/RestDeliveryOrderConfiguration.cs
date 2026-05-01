using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nexo.Domain.Modules.Restaurante;

namespace Nexo.Infrastructure.Persistence.Configurations.Modules.Restaurante;

public class RestDeliveryOrderConfiguration : IEntityTypeConfiguration<RestDeliveryOrder>
{
    public void Configure(EntityTypeBuilder<RestDeliveryOrder> builder)
    {
        builder.ToTable("rest_delivery_orders", "nexo");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.StoreId).HasColumnName("store_id").IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamptz").IsRequired();

        // ── Identificação ────────────────────────────────────────────────────
        builder.Property(x => x.OrderNumber).HasColumnName("order_number").IsRequired();
        builder.Property(x => x.TrackingToken).HasColumnName("tracking_token").HasMaxLength(32).IsRequired();
        builder.Property(x => x.ExternalOrderId).HasColumnName("external_order_id").HasMaxLength(200);
        builder.Property(x => x.ExternalEventType).HasColumnName("external_event_type").HasMaxLength(100);
        builder.Property(x => x.RawPayload).HasColumnName("raw_payload").HasColumnType("jsonb");

        // ── Canal ────────────────────────────────────────────────────────────
        builder.Property(x => x.Channel).HasColumnName("channel")
            .HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(x => x.OrderType).HasColumnName("order_type")
            .HasConversion<string>().HasMaxLength(20).IsRequired();

        // ── Status ───────────────────────────────────────────────────────────
        builder.Property(x => x.Status).HasColumnName("status")
            .HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(x => x.RejectionReason).HasColumnName("rejection_reason").HasMaxLength(500);

        // ── Cliente ───────────────────────────────────────────────────────────
        builder.Property(x => x.CustomerName).HasColumnName("customer_name").HasMaxLength(200).IsRequired();
        builder.Property(x => x.CustomerPhone).HasColumnName("customer_phone").HasMaxLength(20).IsRequired();
        builder.Property(x => x.CustomerEmail).HasColumnName("customer_email").HasMaxLength(200);
        builder.Property(x => x.CustomerId).HasColumnName("customer_id");
        builder.Property(x => x.DeliveryAddressJson).HasColumnName("delivery_address_json").HasColumnType("jsonb");

        // ── Financeiro ────────────────────────────────────────────────────────
        builder.Property(x => x.DeliveryFee).HasColumnName("delivery_fee")
            .HasColumnType("numeric(18,2)").HasDefaultValue(0m).IsRequired();
        builder.Property(x => x.CouponCode).HasColumnName("coupon_code").HasMaxLength(50);
        builder.Property(x => x.DiscountAmount).HasColumnName("discount_amount")
            .HasColumnType("numeric(18,2)").HasDefaultValue(0m).IsRequired();
        builder.Ignore(x => x.ItemsSubtotal);
        builder.Ignore(x => x.Total);

        // ── Logística ─────────────────────────────────────────────────────────
        builder.Property(x => x.EstimatedMinutes).HasColumnName("estimated_minutes");
        builder.Property(x => x.RiderName).HasColumnName("rider_name").HasMaxLength(200);
        builder.Property(x => x.RiderPhone).HasColumnName("rider_phone").HasMaxLength(20);

        // ── Vínculo interno ────────────────────────────────────────────────────
        builder.Property(x => x.RestOrderId).HasColumnName("rest_order_id");

        // ── Observações ────────────────────────────────────────────────────────
        builder.Property(x => x.Notes).HasColumnName("notes").HasMaxLength(1000);

        // ── Timestamps ────────────────────────────────────────────────────────
        builder.Property(x => x.ReceivedAt).HasColumnName("received_at").HasColumnType("timestamptz").IsRequired();
        builder.Property(x => x.AcceptedAt).HasColumnName("accepted_at").HasColumnType("timestamptz");
        builder.Property(x => x.ReadyAt).HasColumnName("ready_at").HasColumnType("timestamptz");
        builder.Property(x => x.DispatchedAt).HasColumnName("dispatched_at").HasColumnType("timestamptz");
        builder.Property(x => x.DeliveredAt).HasColumnName("delivered_at").HasColumnType("timestamptz");
        builder.Property(x => x.CancelledAt).HasColumnName("cancelled_at").HasColumnType("timestamptz");

        // ── Relacionamentos ────────────────────────────────────────────────────
        builder.HasMany(x => x.Items)
            .WithOne()
            .HasForeignKey(x => x.DeliveryOrderId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Nexo.Domain.Entities.Tenant>()
            .WithMany()
            .HasForeignKey(x => x.TenantId)
            .HasConstraintName("fk_rest_delivery_orders_tenants")
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Nexo.Domain.Entities.Store>()
            .WithMany()
            .HasForeignKey(x => x.StoreId)
            .HasConstraintName("fk_rest_delivery_orders_stores")
            .OnDelete(DeleteBehavior.Restrict);

        // ── Índices ────────────────────────────────────────────────────────────
        // Tracking token: único globalmente para rastreio público
        builder.HasIndex(x => x.TrackingToken)
            .IsUnique()
            .HasDatabaseName("ix_rest_delivery_orders_tracking_token");

        // Sequencial por store — UNIQUE garante sem duplicatas mesmo em inserts concorrentes.
        // SaveChangesAsync no repositório traduz a violação em OrderNumberCollisionException
        // para que o serviço possa retentar sem conhecer tipos Npgsql.
        builder.HasIndex(x => new { x.TenantId, x.StoreId, x.OrderNumber })
            .IsUnique()
            .HasDatabaseName("ix_rest_delivery_orders_store_number");

        // Kanban: queries por status + store
        builder.HasIndex(x => new { x.TenantId, x.StoreId, x.Status })
            .HasDatabaseName("ix_rest_delivery_orders_store_status");

        // Filtro por canal
        builder.HasIndex(x => new { x.TenantId, x.StoreId, x.Channel })
            .HasDatabaseName("ix_rest_delivery_orders_store_channel");

        // Deduplicação de pedidos externos (ExternalOrderId + Channel)
        builder.HasIndex(x => new { x.TenantId, x.StoreId, x.Channel, x.ExternalOrderId })
            .HasFilter("external_order_id IS NOT NULL")
            .HasDatabaseName("ix_rest_delivery_orders_external_dedup");

        builder.HasIndex(x => x.TenantId)
            .HasDatabaseName("ix_rest_delivery_orders_tenant_id");
    }
}
