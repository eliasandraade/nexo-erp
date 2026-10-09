using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nexo.Domain.Entities;
using Nexo.Domain.Modules.Service;

namespace Nexo.Infrastructure.Persistence.Configurations.Modules.Service;

/// <summary>
/// EF mapping for svc_commission_entries — append-only, store-scoped, no is_active.
/// The unique index on (tenant_id, source, source_id) is the database-level guarantee that one
/// order item / appointment / package usage can never be commissioned twice, even when two
/// requests (or two API replicas) recognise the same event at the same time.
/// </summary>
public class SvcCommissionEntryConfiguration : IEntityTypeConfiguration<SvcCommissionEntry>
{
    public void Configure(EntityTypeBuilder<SvcCommissionEntry> builder)
    {
        builder.ConfigureStoreScopedSvcEntityNoActive("svc_commission_entries");
        builder.ToTable("svc_commission_entries", "nexo", t => t.HasCheckConstraint(
            "ck_svc_commission_entries_amounts",
            "base_amount >= 0 AND commission_amount >= 0 AND commission_percent > 0 AND commission_percent <= 100"));

        builder.Property(x => x.ProfessionalId).HasColumnName("professional_id").IsRequired();
        builder.Property(x => x.CustomerId).HasColumnName("customer_id").IsRequired();
        builder.Property(x => x.Source)
            .HasColumnName("source").HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.SourceId).HasColumnName("source_id").IsRequired();
        builder.Property(x => x.BaseAmount)
            .HasColumnName("base_amount").HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(x => x.CommissionPercent)
            .HasColumnName("commission_percent").HasColumnType("numeric(5,2)").IsRequired();
        builder.Property(x => x.CommissionAmount)
            .HasColumnName("commission_amount").HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(x => x.RecognizedAt)
            .HasColumnName("recognized_at").HasColumnType("timestamptz").IsRequired();
        builder.Property(x => x.PayoutId).HasColumnName("payout_id");
        builder.Property(x => x.Notes).HasColumnName("notes").HasMaxLength(500);

        builder.HasOne<SvcProfessional>().WithMany().HasForeignKey(x => x.ProfessionalId)
            .HasConstraintName("fk_svc_commission_entries_professionals").OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Customer>().WithMany().HasForeignKey(x => x.CustomerId)
            .HasConstraintName("fk_svc_commission_entries_customers").OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<SvcCommissionPayout>().WithMany().HasForeignKey(x => x.PayoutId)
            .HasConstraintName("fk_svc_commission_entries_payouts").OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new { x.TenantId, x.Source, x.SourceId })
            .IsUnique().HasDatabaseName("ux_svc_commission_entries_source");
        builder.HasIndex(x => new { x.ProfessionalId, x.RecognizedAt })
            .HasDatabaseName("ix_svc_commission_entries_professional_recognized");
        builder.HasIndex(x => x.PayoutId).HasDatabaseName("ix_svc_commission_entries_payout_id");
        builder.HasIndex(x => x.CustomerId).HasDatabaseName("ix_svc_commission_entries_customer_id");
    }
}
