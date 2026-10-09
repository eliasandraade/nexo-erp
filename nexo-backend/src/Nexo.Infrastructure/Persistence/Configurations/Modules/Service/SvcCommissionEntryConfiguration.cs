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
        builder.ToTable("svc_commission_entries", "nexo", t =>
        {
            t.HasCheckConstraint("ck_svc_commission_entries_percent",
                "commission_percent > 0 AND commission_percent <= 100");
            // An earning is non-negative and never points at another entry; a reversal is
            // non-positive, always points at the earning it discounts, and is never itself reversed.
            t.HasCheckConstraint("ck_svc_commission_entries_kind",
                "(kind = 'Earning' AND base_amount >= 0 AND commission_amount >= 0 AND reversal_of_entry_id IS NULL)"
                + " OR (kind = 'Reversal' AND base_amount <= 0 AND commission_amount <= 0"
                + " AND reversal_of_entry_id IS NOT NULL AND reversed_at IS NULL)");
        });

        builder.Property(x => x.ProfessionalId).HasColumnName("professional_id").IsRequired();
        builder.Property(x => x.CustomerId).HasColumnName("customer_id").IsRequired();
        builder.Property(x => x.Kind)
            .HasColumnName("kind").HasConversion<string>().HasMaxLength(20).IsRequired();
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
        builder.Property(x => x.ReversedAt).HasColumnName("reversed_at").HasColumnType("timestamptz");
        builder.Property(x => x.ReversalOfEntryId).HasColumnName("reversal_of_entry_id");
        builder.Property(x => x.Notes).HasColumnName("notes").HasMaxLength(500);

        builder.HasOne<SvcProfessional>().WithMany().HasForeignKey(x => x.ProfessionalId)
            .HasConstraintName("fk_svc_commission_entries_professionals").OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Customer>().WithMany().HasForeignKey(x => x.CustomerId)
            .HasConstraintName("fk_svc_commission_entries_customers").OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<SvcCommissionPayout>().WithMany().HasForeignKey(x => x.PayoutId)
            .HasConstraintName("fk_svc_commission_entries_payouts").OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<SvcCommissionEntry>().WithMany().HasForeignKey(x => x.ReversalOfEntryId)
            .HasConstraintName("fk_svc_commission_entries_reversal_of").OnDelete(DeleteBehavior.Restrict);

        // One ACTIVE earning per source (idempotency); a reversed earning frees the source so a
        // re-settled order can earn again.
        builder.HasIndex(x => new { x.TenantId, x.Source, x.SourceId })
            .IsUnique().HasFilter("kind = 'Earning' AND reversed_at IS NULL")
            .HasDatabaseName("ux_svc_commission_entries_source");
        // At most one reversal per earning.
        builder.HasIndex(x => x.ReversalOfEntryId)
            .IsUnique().HasFilter("reversal_of_entry_id IS NOT NULL")
            .HasDatabaseName("ux_svc_commission_entries_reversal_of");
        builder.HasIndex(x => new { x.ProfessionalId, x.RecognizedAt })
            .HasDatabaseName("ix_svc_commission_entries_professional_recognized");
        builder.HasIndex(x => x.PayoutId).HasDatabaseName("ix_svc_commission_entries_payout_id");
        builder.HasIndex(x => x.CustomerId).HasDatabaseName("ix_svc_commission_entries_customer_id");
    }
}
