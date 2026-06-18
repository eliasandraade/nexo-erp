using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nexo.Domain.Entities;
using Nexo.Domain.Modules.Service;

namespace Nexo.Infrastructure.Persistence.Configurations.Modules.Service;

public class SvcOrderConfiguration : IEntityTypeConfiguration<SvcOrder>
{
    public void Configure(EntityTypeBuilder<SvcOrder> builder)
    {
        builder.ConfigureStoreScopedSvcEntityNoActive("svc_orders");

        builder.Property(x => x.Code).HasColumnName("code").HasMaxLength(40).IsRequired();
        builder.Property(x => x.CustomerId).HasColumnName("customer_id").IsRequired();
        builder.Property(x => x.SubjectId).HasColumnName("subject_id");
        builder.Property(x => x.ProfessionalId).HasColumnName("professional_id");
        builder.Property(x => x.AppointmentId).HasColumnName("appointment_id");
        builder.Property(x => x.Status)
            .HasColumnName("status").HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.Notes).HasColumnName("notes").HasMaxLength(2000);
        builder.Property(x => x.CancellationReason).HasColumnName("cancellation_reason").HasMaxLength(500);
        builder.Property(x => x.TotalAmount)
            .HasColumnName("total_amount").HasColumnType("numeric(18,2)").IsRequired();

        builder.HasMany(x => x.Items)
            .WithOne()
            .HasForeignKey(x => x.OrderId)
            .HasConstraintName("fk_svc_order_items_order")
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Customer>().WithMany().HasForeignKey(x => x.CustomerId)
            .HasConstraintName("fk_svc_orders_customers").OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<SvcSubject>().WithMany().HasForeignKey(x => x.SubjectId)
            .HasConstraintName("fk_svc_orders_subjects").OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<SvcProfessional>().WithMany().HasForeignKey(x => x.ProfessionalId)
            .HasConstraintName("fk_svc_orders_professionals").OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<SvcAppointment>().WithMany().HasForeignKey(x => x.AppointmentId)
            .HasConstraintName("fk_svc_orders_appointments").OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.CustomerId).HasDatabaseName("ix_svc_orders_customer_id");
        builder.HasIndex("TenantId", "StoreId", "Status").HasDatabaseName("ix_svc_orders_tenant_store_status");
        builder.HasIndex(x => x.SubjectId).HasDatabaseName("ix_svc_orders_subject_id");
        builder.HasIndex(x => x.ProfessionalId).HasDatabaseName("ix_svc_orders_professional_id");
        // One order per appointment (partial unique — only when appointment_id is set).
        builder.HasIndex(x => x.AppointmentId).IsUnique()
            .HasFilter("appointment_id IS NOT NULL")
            .HasDatabaseName("ux_svc_orders_appointment_id");
    }
}
