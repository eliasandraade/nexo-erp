using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nexo.Domain.Modules.Build;

namespace Nexo.Infrastructure.Persistence.Configurations.Modules.Build;

public class BuildDailyLogConfiguration : IEntityTypeConfiguration<BuildDailyLog>
{
    public void Configure(EntityTypeBuilder<BuildDailyLog> builder)
    {
        builder.ToTable("bld_daily_logs", "nexo");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.ProjectId).HasColumnName("project_id").IsRequired();
        builder.Property(x => x.Date).HasColumnName("date").IsRequired();
        builder.Property(x => x.WeatherSummary).HasColumnName("weather_summary").HasMaxLength(200);
        builder.Property(x => x.Notes).HasColumnName("notes").HasMaxLength(5000).IsRequired();
        builder.Property(x => x.CreatedBy).HasColumnName("created_by").IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamptz").IsRequired();

        builder.HasMany(x => x.Photos)
            .WithOne()
            .HasForeignKey(x => x.DailyLogId)
            .HasConstraintName("fk_bld_daily_log_photos_log")
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => x.TenantId).HasDatabaseName("ix_bld_daily_logs_tenant_id");

        // One entry per project per date — enforced at DB
        builder.HasIndex(x => new { x.TenantId, x.ProjectId, x.Date })
            .IsUnique()
            .HasDatabaseName("uix_bld_daily_logs_project_date");
    }
}
