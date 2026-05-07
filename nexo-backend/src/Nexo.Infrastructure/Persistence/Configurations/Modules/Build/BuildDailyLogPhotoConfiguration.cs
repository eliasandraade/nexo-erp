using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nexo.Domain.Modules.Build;

namespace Nexo.Infrastructure.Persistence.Configurations.Modules.Build;

public class BuildDailyLogPhotoConfiguration : IEntityTypeConfiguration<BuildDailyLogPhoto>
{
    public void Configure(EntityTypeBuilder<BuildDailyLogPhoto> builder)
    {
        builder.ToTable("bld_daily_log_photos", "nexo");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.DailyLogId).HasColumnName("daily_log_id").IsRequired();
        builder.Property(x => x.StorageKey).HasColumnName("storage_key").HasMaxLength(500).IsRequired();
        builder.Property(x => x.Caption).HasColumnName("caption").HasMaxLength(300);
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamptz").IsRequired();

        builder.HasIndex(x => x.TenantId).HasDatabaseName("ix_bld_daily_log_photos_tenant_id");
        builder.HasIndex(x => x.DailyLogId).HasDatabaseName("ix_bld_daily_log_photos_log_id");
    }
}
