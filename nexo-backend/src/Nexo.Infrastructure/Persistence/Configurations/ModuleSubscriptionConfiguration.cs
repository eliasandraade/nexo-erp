using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nexo.Domain.Entities;
using Nexo.Domain.Enums;

namespace Nexo.Infrastructure.Persistence.Configurations;

public class ModuleSubscriptionConfiguration : IEntityTypeConfiguration<ModuleSubscription>
{
    public void Configure(EntityTypeBuilder<ModuleSubscription> builder)
    {
        builder.ToTable("module_subscriptions");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");

        builder.Property(x => x.TenantId)
            .HasColumnName("tenant_id")
            .IsRequired();

        builder.Property(x => x.ModuleKey)
            .HasColumnName("module_key")
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(x => x.StripeSubscriptionId)
            .HasColumnName("stripe_subscription_id")
            .HasMaxLength(100);

        builder.Property(x => x.StripePriceId)
            .HasColumnName("stripe_price_id")
            .HasMaxLength(100);

        builder.Property(x => x.PlanType)
            .HasColumnName("plan_type")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(x => x.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(x => x.CurrentPeriodStart)
            .HasColumnName("current_period_start")
            .HasColumnType("timestamptz")
            .IsRequired();

        builder.Property(x => x.CurrentPeriodEnd)
            .HasColumnName("current_period_end")
            .HasColumnType("timestamptz");

        builder.Property(x => x.CancelAtPeriodEnd)
            .HasColumnName("cancel_at_period_end")
            .HasDefaultValue(false);

        builder.Property(x => x.CanceledAt)
            .HasColumnName("canceled_at")
            .HasColumnType("timestamptz");

        builder.Property(x => x.GrantedById)
            .HasColumnName("granted_by_id");

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

        // One subscription per (tenant, module) — updated in place
        builder.HasIndex(x => new { x.TenantId, x.ModuleKey }).IsUnique();
        builder.HasIndex(x => new { x.Status, x.CurrentPeriodEnd });
        builder.HasIndex(x => x.TenantId);

        // FK: Tenant
        builder.HasOne<Nexo.Domain.Entities.Tenant>()
            .WithMany()
            .HasForeignKey(x => x.TenantId)
            .OnDelete(DeleteBehavior.Cascade);

        // FK: GrantedBy (optional)
        builder.HasOne(x => x.GrantedBy)
            .WithMany()
            .HasForeignKey(x => x.GrantedById)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
