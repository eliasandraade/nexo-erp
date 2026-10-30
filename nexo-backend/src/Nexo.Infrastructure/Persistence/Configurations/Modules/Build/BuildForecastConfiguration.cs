using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nexo.Domain.Modules.Build;

namespace Nexo.Infrastructure.Persistence.Configurations.Modules.Build;

/// <summary>Wave 6 — stored delivery forecasts (history kept; json columns, never jsonb).</summary>
public class BuildForecastConfiguration : IEntityTypeConfiguration<BuildForecast>
{
    public void Configure(EntityTypeBuilder<BuildForecast> b)
    {
        b.ToTable("bld_forecasts", "nexo");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        b.Property(x => x.ProjectId).HasColumnName("project_id").IsRequired();
        b.Property(x => x.Date).HasColumnName("date").IsRequired();
        b.Property(x => x.GeneratedAt).HasColumnName("generated_at").HasColumnType("timestamptz").IsRequired();
        b.Property(x => x.Methodology).HasColumnName("methodology").HasMaxLength(60).IsRequired();
        b.Property(x => x.Version).HasColumnName("version").IsRequired();
        b.Property(x => x.ForecastDelivery).HasColumnName("forecast_delivery");
        b.Property(x => x.PlannedDelivery).HasColumnName("planned_delivery");
        b.Property(x => x.Confidence).HasColumnName("confidence").HasColumnType("numeric(4,3)").IsRequired();
        b.Property(x => x.FactorsJson).HasColumnName("factors").HasColumnType("json").IsRequired();
        b.Property(x => x.StagesJson).HasColumnName("stages").HasColumnType("json").IsRequired();
        b.Property(x => x.Source).HasColumnName("source").HasConversion<int>().IsRequired();
        b.Property(x => x.CreatedBy).HasColumnName("created_by");
        b.Property(x => x.CreatedByName).HasColumnName("created_by_name").HasMaxLength(200);
        b.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz").IsRequired();
        b.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamptz").IsRequired();

        b.HasOne<BuildProject>().WithMany().HasForeignKey(x => x.ProjectId)
            .HasConstraintName("fk_bld_forecasts_project").OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => new { x.ProjectId, x.GeneratedAt }).HasDatabaseName("ix_bld_forecasts_project_generated");
        b.HasIndex(x => new { x.ProjectId, x.Date }).HasDatabaseName("ix_bld_forecasts_project_date");
        b.HasIndex(x => x.TenantId).HasDatabaseName("ix_bld_forecasts_tenant_id");
    }
}
