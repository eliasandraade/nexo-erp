using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nexo.Domain.Modules.Service;

namespace Nexo.Infrastructure.Persistence.Configurations.Modules.Service;

public class SvcCatalogItemConfiguration : IEntityTypeConfiguration<SvcCatalogItem>
{
    public void Configure(EntityTypeBuilder<SvcCatalogItem> builder)
    {
        // Keys, tenant/store columns + FKs, is_active, audit columns, indexes.
        builder.ConfigureStoreScopedSvcEntity("svc_catalog_items");

        // Entity-specific columns
        builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
        builder.Property(x => x.Description).HasColumnName("description").HasMaxLength(1000);
        builder.Property(x => x.Category).HasColumnName("category").HasMaxLength(100);
        builder.Property(x => x.DurationMinutes).HasColumnName("duration_minutes").IsRequired();
        builder.Property(x => x.Price).HasColumnName("price").HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(x => x.CommissionPercent)
            .HasColumnName("commission_percent").HasColumnType("numeric(5,2)");
        builder.Property(x => x.RequiresSubject).HasColumnName("requires_subject")
            .HasDefaultValue(false).IsRequired();
    }
}
