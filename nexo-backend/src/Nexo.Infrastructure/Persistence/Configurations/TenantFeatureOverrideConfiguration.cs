using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nexo.Domain.Entities;

namespace Nexo.Infrastructure.Persistence.Configurations;

public class TenantFeatureOverrideConfiguration : IEntityTypeConfiguration<TenantFeatureOverride>
{
    public void Configure(EntityTypeBuilder<TenantFeatureOverride> b)
    {
        b.ToTable("tenant_feature_overrides");

        b.HasKey(o => o.Id);

        b.Property(o => o.TenantId).IsRequired();
        b.Property(o => o.FlagKey).HasMaxLength(128).IsRequired();
        b.Property(o => o.IsEnabled).IsRequired();
        b.Property(o => o.Notes).HasMaxLength(512);

        // Unique: a tenant can only have one override per flag
        b.HasIndex(o => new { o.TenantId, o.FlagKey }).IsUnique();

        b.HasOne(o => o.Tenant)
            .WithMany()
            .HasForeignKey(o => o.TenantId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasOne(o => o.Flag)
            .WithMany()
            .HasForeignKey(o => o.FlagKey)
            .HasPrincipalKey(f => f.Key)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
