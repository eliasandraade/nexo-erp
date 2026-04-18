using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nexo.Domain.Entities;

namespace Nexo.Infrastructure.Persistence.Configurations;

public class ModuleSubscriptionEventConfiguration : IEntityTypeConfiguration<ModuleSubscriptionEvent>
{
    public void Configure(EntityTypeBuilder<ModuleSubscriptionEvent> b)
    {
        b.ToTable("module_subscription_events");

        b.HasKey(e => e.Id);

        b.Property(e => e.TenantId).IsRequired();
        b.Property(e => e.ModuleKey).HasMaxLength(64).IsRequired();
        b.Property(e => e.EventType).HasMaxLength(32).IsRequired();
        b.Property(e => e.Notes).HasMaxLength(512);
        b.Property(e => e.PlanType).HasMaxLength(32);

        // Queries: "history for tenant X" and "history for tenant X + module Y"
        b.HasIndex(e => new { e.TenantId, e.CreatedAt });
        b.HasIndex(e => new { e.TenantId, e.ModuleKey });

        b.HasOne(e => e.Tenant)
            .WithMany()
            .HasForeignKey(e => e.TenantId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
