using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nexo.Domain.Entities;
using Nexo.Domain.Enums;

namespace Nexo.Infrastructure.Persistence.Configurations;

public class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> builder)
    {
        builder.ToTable("tenants");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");

        builder.Property(x => x.Slug)
            .HasColumnName("slug")
            .HasMaxLength(120)
            .IsRequired();

        builder.Property(x => x.CompanyName)
            .HasColumnName("company_name")
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(x => x.TradeName)
            .HasColumnName("trade_name")
            .HasMaxLength(200);

        builder.Property(x => x.TaxId)
            .HasColumnName("tax_id")
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(x => x.Email)
            .HasColumnName("email")
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(x => x.Phone)
            .HasColumnName("phone")
            .HasMaxLength(30);

        builder.Property(x => x.BusinessType)
            .HasColumnName("business_type")
            .HasMaxLength(50);

        builder.Property(x => x.StripeCustomerId)
            .HasColumnName("stripe_customer_id")
            .HasMaxLength(100);

        builder.Property(x => x.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(x => x.TrialEndsAt)
            .HasColumnName("trial_ends_at")
            .HasColumnType("timestamptz");

        builder.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamptz")
            .IsRequired();

        builder.Property(x => x.UpdatedAt)
            .HasColumnName("updated_at")
            .HasColumnType("timestamptz")
            .IsRequired();

        builder.HasIndex(x => x.Slug).IsUnique();
        builder.HasIndex(x => x.TaxId).IsUnique();
        builder.HasIndex(x => x.StripeCustomerId).IsUnique();
        builder.HasIndex(x => x.Status);
    }
}
