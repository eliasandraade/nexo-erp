using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nexo.Domain.Entities;
using Nexo.Domain.Modules.Service;

namespace Nexo.Infrastructure.Persistence.Configurations.Modules.Service;

public class SvcAppointmentConfiguration : IEntityTypeConfiguration<SvcAppointment>
{
    public void Configure(EntityTypeBuilder<SvcAppointment> builder)
    {
        // Key, tenant/store columns + FKs, audit columns (no is_active — appointments use Status).
        builder.ConfigureStoreScopedSvcEntityNoActive("svc_appointments");

        builder.Property(x => x.CustomerId).HasColumnName("customer_id").IsRequired();
        builder.Property(x => x.ProfessionalId).HasColumnName("professional_id").IsRequired();
        builder.Property(x => x.CatalogItemId).HasColumnName("catalog_item_id").IsRequired();
        builder.Property(x => x.SubjectId).HasColumnName("subject_id");
        builder.Property(x => x.StartsAt).HasColumnName("starts_at").HasColumnType("timestamptz").IsRequired();
        builder.Property(x => x.EndsAt).HasColumnName("ends_at").HasColumnType("timestamptz").IsRequired();
        builder.Property(x => x.Status)
            .HasColumnName("status").HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.Notes).HasColumnName("notes").HasMaxLength(2000);
        builder.Property(x => x.CancellationReason).HasColumnName("cancellation_reason").HasMaxLength(500);
        builder.Property(x => x.PriceSnapshot)
            .HasColumnName("price_snapshot").HasColumnType("numeric(18,2)").IsRequired();

        builder.HasOne<Customer>().WithMany().HasForeignKey(x => x.CustomerId)
            .HasConstraintName("fk_svc_appointments_customers").OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<SvcProfessional>().WithMany().HasForeignKey(x => x.ProfessionalId)
            .HasConstraintName("fk_svc_appointments_professionals").OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<SvcCatalogItem>().WithMany().HasForeignKey(x => x.CatalogItemId)
            .HasConstraintName("fk_svc_appointments_catalog_items").OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<SvcSubject>().WithMany().HasForeignKey(x => x.SubjectId)
            .HasConstraintName("fk_svc_appointments_subjects").OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex("TenantId", "StoreId", "ProfessionalId", "StartsAt")
            .HasDatabaseName("ix_svc_appointments_professional_starts");
        builder.HasIndex(x => x.CustomerId).HasDatabaseName("ix_svc_appointments_customer_id");
    }
}
