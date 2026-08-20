using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nexo.Domain.Modules.Service;

namespace Nexo.Infrastructure.Persistence.Configurations.Modules.Service;

/// <summary>EF mapping for svc_commission_payouts — store-scoped closing of a commission period.</summary>
public class SvcCommissionPayoutConfiguration : IEntityTypeConfiguration<SvcCommissionPayout>
{
    public void Configure(EntityTypeBuilder<SvcCommissionPayout> builder)
    {
        builder.ConfigureStoreScopedSvcEntityNoActive("svc_commission_payouts");

        builder.Property(x => x.ProfessionalId).HasColumnName("professional_id").IsRequired();
        builder.Property(x => x.PeriodStart)
            .HasColumnName("period_start").HasColumnType("timestamptz").IsRequired();
        builder.Property(x => x.PeriodEnd)
            .HasColumnName("period_end").HasColumnType("timestamptz").IsRequired();
        builder.Property(x => x.TotalAmount)
            .HasColumnName("total_amount").HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(x => x.EntryCount).HasColumnName("entry_count").IsRequired();
        builder.Property(x => x.Status)
            .HasColumnName("status").HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.PaidAt).HasColumnName("paid_at").HasColumnType("timestamptz");
        builder.Property(x => x.Notes).HasColumnName("notes").HasMaxLength(500);

        builder.HasOne<SvcProfessional>().WithMany().HasForeignKey(x => x.ProfessionalId)
            .HasConstraintName("fk_svc_commission_payouts_professionals").OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new { x.ProfessionalId, x.PeriodEnd })
            .HasDatabaseName("ix_svc_commission_payouts_professional_period");
    }
}
