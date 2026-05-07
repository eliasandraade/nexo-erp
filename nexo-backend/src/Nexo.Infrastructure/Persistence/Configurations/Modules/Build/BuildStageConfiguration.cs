using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nexo.Domain.Modules.Build;

namespace Nexo.Infrastructure.Persistence.Configurations.Modules.Build;

public class BuildStageConfiguration : IEntityTypeConfiguration<BuildStage>
{
    public void Configure(EntityTypeBuilder<BuildStage> builder)
    {
        builder.ToTable("bld_stages", "nexo");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.ProjectId).HasColumnName("project_id").IsRequired();
        builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
        builder.Property(x => x.Description).HasColumnName("description").HasMaxLength(1000);
        builder.Property(x => x.Order).HasColumnName("order").IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").HasConversion<int>().IsRequired();
        builder.Property(x => x.PlannedStartDate).HasColumnName("planned_start_date");
        builder.Property(x => x.PlannedEndDate).HasColumnName("planned_end_date");
        builder.Property(x => x.ActualStartDate).HasColumnName("actual_start_date");
        builder.Property(x => x.ActualEndDate).HasColumnName("actual_end_date");
        builder.Property(x => x.ProgressPercent).HasColumnName("progress_percent").IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamptz").IsRequired();

        builder.HasIndex(x => x.TenantId).HasDatabaseName("ix_bld_stages_tenant_id");
        builder.HasIndex(x => new { x.ProjectId, x.Order }).HasDatabaseName("ix_bld_stages_project_order");
    }
}
