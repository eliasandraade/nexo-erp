using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nexo.Domain.Entities;

namespace Nexo.Infrastructure.Persistence.Configurations;

public class FeatureFlagConfiguration : IEntityTypeConfiguration<FeatureFlag>
{
    public void Configure(EntityTypeBuilder<FeatureFlag> b)
    {
        b.ToTable("feature_flags");

        b.HasKey(f => f.Id);

        b.Property(f => f.Key).HasMaxLength(128).IsRequired();
        b.Property(f => f.Name).HasMaxLength(256).IsRequired();
        b.Property(f => f.Description).HasMaxLength(1024);
        b.Property(f => f.DefaultEnabled).IsRequired().HasDefaultValue(false);
        b.Property(f => f.Category).HasMaxLength(64).IsRequired().HasDefaultValue("geral");

        b.HasIndex(f => f.Key).IsUnique();
        b.HasIndex(f => f.Category);
    }
}
