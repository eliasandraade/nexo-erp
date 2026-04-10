using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nexo.Domain.Entities;

namespace Nexo.Infrastructure.Persistence.Configurations;

public class ModuleDefinitionConfiguration : IEntityTypeConfiguration<ModuleDefinition>
{
    public void Configure(EntityTypeBuilder<ModuleDefinition> builder)
    {
        builder.ToTable("module_definitions");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");

        builder.Property(x => x.Key)
            .HasColumnName("key")
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(x => x.Name)
            .HasColumnName("name")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(x => x.Description)
            .HasColumnName("description");

        builder.Property(x => x.Version)
            .HasColumnName("version")
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(x => x.IsPublished)
            .HasColumnName("is_published")
            .HasDefaultValue(false);

        builder.Property(x => x.StripeProductId)
            .HasColumnName("stripe_product_id")
            .HasMaxLength(100);

        builder.Property(x => x.StripePriceMonthly)
            .HasColumnName("stripe_price_monthly")
            .HasMaxLength(100);

        builder.Property(x => x.StripePriceQuarterly)
            .HasColumnName("stripe_price_quarterly")
            .HasMaxLength(100);

        builder.Property(x => x.StripePriceSemiannual)
            .HasColumnName("stripe_price_semiannual")
            .HasMaxLength(100);

        builder.Property(x => x.StripePriceAnnual)
            .HasColumnName("stripe_price_annual")
            .HasMaxLength(100);

        builder.Property(x => x.StripePriceLifetime)
            .HasColumnName("stripe_price_lifetime")
            .HasMaxLength(100);

        builder.Property(x => x.PriceMonthly)
            .HasColumnName("price_monthly")
            .HasColumnType("decimal(10,2)");

        builder.Property(x => x.PriceQuarterly)
            .HasColumnName("price_quarterly")
            .HasColumnType("decimal(10,2)");

        builder.Property(x => x.PriceSemiannual)
            .HasColumnName("price_semiannual")
            .HasColumnType("decimal(10,2)");

        builder.Property(x => x.PriceAnnual)
            .HasColumnName("price_annual")
            .HasColumnType("decimal(10,2)");

        builder.Property(x => x.PriceLifetime)
            .HasColumnName("price_lifetime")
            .HasColumnType("decimal(10,2)");

        builder.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamptz")
            .IsRequired();

        builder.Property(x => x.UpdatedAt)
            .HasColumnName("updated_at")
            .HasColumnType("timestamptz")
            .IsRequired();

        builder.HasIndex(x => x.Key).IsUnique();
    }
}
