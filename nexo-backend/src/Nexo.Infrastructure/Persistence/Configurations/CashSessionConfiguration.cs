using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nexo.Domain.Entities;

namespace Nexo.Infrastructure.Persistence.Configurations;

public class CashSessionConfiguration : IEntityTypeConfiguration<CashSession>
{
    public void Configure(EntityTypeBuilder<CashSession> builder)
    {
        builder.ToTable("cash_sessions");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");

        builder.Property(x => x.TenantId)
            .HasColumnName("tenant_id")
            .IsRequired();

        builder.Property(x => x.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(x => x.OpenedByUserId)
            .HasColumnName("opened_by_user_id")
            .IsRequired();

        builder.Property(x => x.ClosedByUserId)
            .HasColumnName("closed_by_user_id");

        builder.Property(x => x.OpeningBalance)
            .HasColumnName("opening_balance")
            .HasColumnType("numeric(18,2)")
            .IsRequired();

        builder.Property(x => x.ClosingBalance)
            .HasColumnName("closing_balance")
            .HasColumnType("numeric(18,2)");

        builder.Property(x => x.OpenedAt)
            .HasColumnName("opened_at")
            .HasColumnType("timestamptz")
            .IsRequired();

        builder.Property(x => x.ClosedAt)
            .HasColumnName("closed_at")
            .HasColumnType("timestamptz");

        builder.Property(x => x.Notes)
            .HasColumnName("notes")
            .HasMaxLength(500);

        builder.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamptz")
            .IsRequired();

        builder.Property(x => x.UpdatedAt)
            .HasColumnName("updated_at")
            .HasColumnType("timestamptz")
            .IsRequired();

        builder.HasIndex(x => x.TenantId);

        // Rule: one open session per tenant + user.
        // A partial unique index would be ideal (WHERE status = 'Open'), but EF doesn't
        // support that directly. The unique index here covers (tenant_id, opened_by_user_id, status).
        // The service layer enforces the Open-only constraint via GetOpenSessionByUserAsync.
        builder.HasIndex(x => new { x.TenantId, x.OpenedByUserId, x.Status })
            .HasDatabaseName("ix_cash_sessions_tenant_user_status");

        builder.HasOne(x => x.OpenedBy)
            .WithMany()
            .HasForeignKey(x => x.OpenedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.ClosedBy)
            .WithMany()
            .HasForeignKey(x => x.ClosedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Nexo.Domain.Entities.Tenant>()
            .WithMany()
            .HasForeignKey(x => x.TenantId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
