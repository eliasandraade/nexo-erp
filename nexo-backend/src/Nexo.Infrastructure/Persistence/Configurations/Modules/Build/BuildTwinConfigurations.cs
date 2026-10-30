using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nexo.Domain.Entities;
using Nexo.Domain.Modules.Build;

namespace Nexo.Infrastructure.Persistence.Configurations.Modules.Build;

// Wave 4 — digital twin tables (migration BuildWave4DigitalTwin). All bld_*, schema nexo, snake_case. JSON columns use the
// type json (not jsonb), like the other Build JSON columns.

public class BuildTenantSettingsConfiguration : IEntityTypeConfiguration<BuildTenantSettings>
{
    public void Configure(EntityTypeBuilder<BuildTenantSettings> b)
    {
        b.ToTable("bld_tenant_settings", "nexo");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        b.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        b.Property(x => x.Build3DEnabled).HasColumnName("build_3d_enabled").IsRequired();
        b.Property(x => x.BuildAiEnabled).HasColumnName("build_ai_enabled").IsRequired();
        b.Property(x => x.BuildVisionEnabled).HasColumnName("build_vision_enabled").IsRequired();
        b.Property(x => x.BuildPredictEnabled).HasColumnName("build_predict_enabled").IsRequired();
        b.Property(x => x.UpdatedBy).HasColumnName("updated_by");
        b.Property(x => x.UpdatedByName).HasColumnName("updated_by_name").HasMaxLength(200);
        b.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamptz").IsRequired();

        b.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId)
            .HasConstraintName("fk_bld_tenant_settings_tenants").OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => x.TenantId).IsUnique().HasDatabaseName("ux_bld_tenant_settings_tenant");
    }
}

public class BuildModelConfiguration : IEntityTypeConfiguration<BuildModel>
{
    public void Configure(EntityTypeBuilder<BuildModel> b)
    {
        BuildCommonColumns.Base(b, "bld_models");
        b.Property(x => x.ProjectId).HasColumnName("project_id").IsRequired();
        b.Property(x => x.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
        b.Property(x => x.Discipline).HasColumnName("discipline").HasConversion<int>().IsRequired();
        // no FK on purpose (would be a cycle with bld_model_versions.model_id)
        b.Property(x => x.CurrentVersionId).HasColumnName("current_version_id");
        b.Property(x => x.CreatedBy).HasColumnName("created_by").IsRequired();

        b.HasOne<BuildProject>().WithMany().HasForeignKey(x => x.ProjectId)
            .HasConstraintName("fk_bld_models_project").OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => new { x.TenantId, x.ProjectId }).HasDatabaseName("ix_bld_models_project");
    }
}

public class BuildModelVersionConfiguration : IEntityTypeConfiguration<BuildModelVersion>
{
    public void Configure(EntityTypeBuilder<BuildModelVersion> b)
    {
        BuildCommonColumns.Base(b, "bld_model_versions");
        b.Ignore(x => x.IsProcessed);
        b.Property(x => x.ProjectId).HasColumnName("project_id").IsRequired();
        b.Property(x => x.ModelId).HasColumnName("model_id").IsRequired();
        b.Property(x => x.Number).HasColumnName("number").IsRequired();
        b.Property(x => x.SourceFormat).HasColumnName("source_format").HasConversion<int>().IsRequired();
        b.Property(x => x.SourceFileKey).HasColumnName("source_file_key").HasMaxLength(500);
        b.Property(x => x.SourceFileName).HasColumnName("source_file_name").HasMaxLength(255).IsRequired();
        b.Property(x => x.SizeBytes).HasColumnName("size_bytes").IsRequired();
        b.Property(x => x.Sha256).HasColumnName("sha256").HasMaxLength(64);
        b.Property(x => x.GeometrySource).HasColumnName("geometry_source").HasConversion<int>().IsRequired();
        b.Property(x => x.ViewerFileKey).HasColumnName("viewer_file_key").HasMaxLength(500);
        b.Property(x => x.ViewerSource).HasColumnName("viewer_source").HasConversion<int>().IsRequired();
        b.Property(x => x.Status).HasColumnName("status").HasConversion<int>().IsRequired();
        b.Property(x => x.StatusMessage).HasColumnName("status_message").HasMaxLength(1000);
        b.Property(x => x.ElementCount).HasColumnName("element_count").IsRequired();
        b.Property(x => x.StoreyCount).HasColumnName("storey_count").IsRequired();
        b.Property(x => x.Schema).HasColumnName("schema").HasMaxLength(40);
        b.Property(x => x.LayersJson).HasColumnName("layers").HasColumnType("json");
        b.Property(x => x.ProcessedAt).HasColumnName("processed_at").HasColumnType("timestamptz");
        b.Property(x => x.CreatedBy).HasColumnName("created_by").IsRequired();

        b.HasOne<BuildModel>().WithMany().HasForeignKey(x => x.ModelId)
            .HasConstraintName("fk_bld_model_versions_model").OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => new { x.TenantId, x.ModelId, x.Number }).IsUnique().HasDatabaseName("ux_bld_model_versions_model_number");
        b.HasIndex(x => new { x.TenantId, x.ProjectId }).HasDatabaseName("ix_bld_model_versions_project");
    }
}

public class BuildModelProcessingJobConfiguration : IEntityTypeConfiguration<BuildModelProcessingJob>
{
    public void Configure(EntityTypeBuilder<BuildModelProcessingJob> b)
    {
        BuildCommonColumns.Base(b, "bld_model_processing_jobs");
        b.Property(x => x.VersionId).HasColumnName("version_id").IsRequired();
        b.Property(x => x.Kind).HasColumnName("kind").HasConversion<int>().IsRequired();
        b.Property(x => x.Status).HasColumnName("status").HasConversion<int>().IsRequired();
        b.Property(x => x.Attempts).HasColumnName("attempts").IsRequired();
        b.Property(x => x.LockedUntil).HasColumnName("locked_until").HasColumnType("timestamptz");
        b.Property(x => x.Error).HasColumnName("error").HasMaxLength(2000);
        b.Property(x => x.StartedAt).HasColumnName("started_at").HasColumnType("timestamptz");
        b.Property(x => x.FinishedAt).HasColumnName("finished_at").HasColumnType("timestamptz");

        b.HasOne<BuildModelVersion>().WithMany().HasForeignKey(x => x.VersionId)
            .HasConstraintName("fk_bld_model_processing_jobs_version").OnDelete(DeleteBehavior.Cascade);
        // the worker's claim scans by status then age, across tenants
        b.HasIndex(x => new { x.Status, x.CreatedAt }).HasDatabaseName("ix_bld_model_processing_jobs_status_created");
        b.HasIndex(x => new { x.TenantId, x.VersionId }).HasDatabaseName("ix_bld_model_processing_jobs_version");
    }
}

public class BuildModelZoneConfiguration : IEntityTypeConfiguration<BuildModelZone>
{
    public void Configure(EntityTypeBuilder<BuildModelZone> b)
    {
        BuildCommonColumns.Base(b, "bld_model_zones");
        b.Ignore(x => x.IsManual);
        b.Property(x => x.ProjectId).HasColumnName("project_id").IsRequired();
        b.Property(x => x.ModelVersionId).HasColumnName("model_version_id");
        b.Property(x => x.Kind).HasColumnName("kind").HasConversion<int>().IsRequired();
        b.Property(x => x.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
        b.Property(x => x.ExternalId).HasColumnName("external_id").HasMaxLength(100);
        b.Property(x => x.ParentZoneId).HasColumnName("parent_zone_id");
        b.Property(x => x.Elevation).HasColumnName("elevation").HasColumnType("numeric(12,3)");
        b.Property(x => x.Order).HasColumnName("order").IsRequired();
        b.Property(x => x.Outline2DJson).HasColumnName("outline_2d").HasColumnType("json");
        b.Property(x => x.SheetVersionId).HasColumnName("sheet_version_id");
        b.Property(x => x.SimplifiedBoxJson).HasColumnName("simplified_box").HasColumnType("json");
        b.Property(x => x.CreatedBy).HasColumnName("created_by");

        b.HasOne<BuildProject>().WithMany().HasForeignKey(x => x.ProjectId)
            .HasConstraintName("fk_bld_model_zones_project").OnDelete(DeleteBehavior.Cascade);
        b.HasOne<BuildModelVersion>().WithMany().HasForeignKey(x => x.ModelVersionId)
            .HasConstraintName("fk_bld_model_zones_version").OnDelete(DeleteBehavior.Cascade).IsRequired(false);
        b.HasOne<BuildModelVersion>().WithMany().HasForeignKey(x => x.SheetVersionId)
            .HasConstraintName("fk_bld_model_zones_sheet").OnDelete(DeleteBehavior.SetNull).IsRequired(false);
        b.HasOne<BuildModelZone>().WithMany().HasForeignKey(x => x.ParentZoneId)
            .HasConstraintName("fk_bld_model_zones_parent").OnDelete(DeleteBehavior.SetNull).IsRequired(false);
        b.HasIndex(x => new { x.TenantId, x.ProjectId }).HasDatabaseName("ix_bld_model_zones_project");
        b.HasIndex(x => new { x.TenantId, x.ModelVersionId }).HasDatabaseName("ix_bld_model_zones_version");
    }
}

public class BuildModelElementConfiguration : IEntityTypeConfiguration<BuildModelElement>
{
    public void Configure(EntityTypeBuilder<BuildModelElement> b)
    {
        BuildCommonColumns.Base(b, "bld_model_elements");
        b.Property(x => x.ProjectId).HasColumnName("project_id").IsRequired();
        b.Property(x => x.ModelVersionId).HasColumnName("model_version_id").IsRequired();
        b.Property(x => x.ExternalId).HasColumnName("external_id").HasMaxLength(100).IsRequired();
        b.Property(x => x.Category).HasColumnName("category").HasMaxLength(100).IsRequired();
        b.Property(x => x.Name).HasColumnName("name").HasMaxLength(300);
        b.Property(x => x.TypeName).HasColumnName("type_name").HasMaxLength(300);
        b.Property(x => x.Material).HasColumnName("material").HasMaxLength(200);
        // zone rows are replaced with the version's elements on reprocessing: no FK, the processor writes both
        b.Property(x => x.ZoneId).HasColumnName("zone_id");
        b.Property(x => x.PropertiesJson).HasColumnName("properties").HasColumnType("json");

        b.HasOne<BuildModelVersion>().WithMany().HasForeignKey(x => x.ModelVersionId)
            .HasConstraintName("fk_bld_model_elements_version").OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => new { x.TenantId, x.ModelVersionId, x.ExternalId }).IsUnique().HasDatabaseName("ux_bld_model_elements_version_external");
        b.HasIndex(x => new { x.TenantId, x.ModelVersionId, x.Category }).HasDatabaseName("ix_bld_model_elements_version_category");
        b.HasIndex(x => new { x.TenantId, x.ModelVersionId, x.ZoneId }).HasDatabaseName("ix_bld_model_elements_version_zone");
    }
}

public class BuildModelStageBindingConfiguration : IEntityTypeConfiguration<BuildModelStageBinding>
{
    public void Configure(EntityTypeBuilder<BuildModelStageBinding> b)
    {
        BuildCommonColumns.Base(b, "bld_model_stage_bindings");
        b.Property(x => x.ProjectId).HasColumnName("project_id").IsRequired();
        b.Property(x => x.StageId).HasColumnName("stage_id").IsRequired();
        b.Property(x => x.Scope).HasColumnName("scope").HasConversion<int>().IsRequired();
        b.Property(x => x.ModelVersionId).HasColumnName("model_version_id");
        b.Property(x => x.ElementExternalId).HasColumnName("element_external_id").HasMaxLength(100);
        b.Property(x => x.ZoneId).HasColumnName("zone_id");
        b.Property(x => x.Category).HasColumnName("category").HasMaxLength(100);
        b.Property(x => x.Source).HasColumnName("source").HasConversion<int>().IsRequired();
        b.Property(x => x.CreatedBy).HasColumnName("created_by").IsRequired();

        b.HasOne<BuildProject>().WithMany().HasForeignKey(x => x.ProjectId)
            .HasConstraintName("fk_bld_model_stage_bindings_project").OnDelete(DeleteBehavior.Cascade);
        b.HasOne<BuildStage>().WithMany().HasForeignKey(x => x.StageId)
            .HasConstraintName("fk_bld_model_stage_bindings_stage").OnDelete(DeleteBehavior.Cascade);
        b.HasOne<BuildModelZone>().WithMany().HasForeignKey(x => x.ZoneId)
            .HasConstraintName("fk_bld_model_stage_bindings_zone").OnDelete(DeleteBehavior.Cascade).IsRequired(false);
        b.HasIndex(x => new { x.TenantId, x.ProjectId, x.Scope }).HasDatabaseName("ix_bld_model_stage_bindings_project_scope");
        b.HasIndex(x => new { x.TenantId, x.StageId }).HasDatabaseName("ix_bld_model_stage_bindings_stage");
    }
}

public class BuildElementProgressEventConfiguration : IEntityTypeConfiguration<BuildElementProgressEvent>
{
    public void Configure(EntityTypeBuilder<BuildElementProgressEvent> b)
    {
        BuildCommonColumns.Base(b, "bld_element_progress_events");
        b.Property(x => x.ProjectId).HasColumnName("project_id").IsRequired();
        b.Property(x => x.ModelVersionId).HasColumnName("model_version_id").IsRequired();
        b.Property(x => x.ElementExternalId).HasColumnName("element_external_id").HasMaxLength(100).IsRequired();
        b.Property(x => x.State).HasColumnName("state").HasConversion<int>().IsRequired();
        b.Property(x => x.Source).HasColumnName("source").HasConversion<int>().IsRequired();
        b.Property(x => x.SourceRef).HasColumnName("source_ref");
        b.Property(x => x.Note).HasColumnName("note").HasMaxLength(1000);
        b.Property(x => x.EffectiveDate).HasColumnName("effective_date").IsRequired();
        b.Property(x => x.RecordedBy).HasColumnName("recorded_by").IsRequired();
        b.Property(x => x.RecordedByName).HasColumnName("recorded_by_name").HasMaxLength(200).IsRequired();

        b.HasOne<BuildProject>().WithMany().HasForeignKey(x => x.ProjectId)
            .HasConstraintName("fk_bld_element_progress_events_project").OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => new { x.TenantId, x.ProjectId, x.ElementExternalId, x.EffectiveDate })
            .HasDatabaseName("ix_bld_element_progress_events_element_date");
    }
}

public class BuildRealityCaptureConfiguration : IEntityTypeConfiguration<BuildRealityCapture>
{
    public void Configure(EntityTypeBuilder<BuildRealityCapture> b)
    {
        BuildCommonColumns.Base(b, "bld_reality_captures");
        b.Property(x => x.ProjectId).HasColumnName("project_id").IsRequired();
        b.Property(x => x.Type).HasColumnName("type").HasConversion<int>().IsRequired();
        b.Property(x => x.CapturedAt).HasColumnName("captured_at").HasColumnType("timestamptz").IsRequired();
        b.Property(x => x.StageId).HasColumnName("stage_id");
        b.Property(x => x.ZoneId).HasColumnName("zone_id");
        b.Property(x => x.ElementExternalId).HasColumnName("element_external_id").HasMaxLength(100);
        b.Property(x => x.Title).HasColumnName("title").HasMaxLength(200).IsRequired();
        b.Property(x => x.Notes).HasColumnName("notes").HasMaxLength(2000);
        b.Property(x => x.MediaKey).HasColumnName("media_key").HasMaxLength(500).IsRequired();
        b.Property(x => x.ThumbnailKey).HasColumnName("thumbnail_key").HasMaxLength(500);
        b.Property(x => x.ContentType).HasColumnName("content_type").HasMaxLength(100).IsRequired();
        b.Property(x => x.SizeBytes).HasColumnName("size_bytes").IsRequired();
        b.Property(x => x.ClientVisible).HasColumnName("client_visible").IsRequired();
        b.Property(x => x.CreatedBy).HasColumnName("created_by").IsRequired();

        b.HasOne<BuildProject>().WithMany().HasForeignKey(x => x.ProjectId)
            .HasConstraintName("fk_bld_reality_captures_project").OnDelete(DeleteBehavior.Cascade);
        b.HasOne<BuildStage>().WithMany().HasForeignKey(x => x.StageId)
            .HasConstraintName("fk_bld_reality_captures_stage").OnDelete(DeleteBehavior.SetNull).IsRequired(false);
        b.HasOne<BuildModelZone>().WithMany().HasForeignKey(x => x.ZoneId)
            .HasConstraintName("fk_bld_reality_captures_zone").OnDelete(DeleteBehavior.SetNull).IsRequired(false);
        b.HasIndex(x => new { x.TenantId, x.ProjectId, x.CapturedAt }).HasDatabaseName("ix_bld_reality_captures_project_captured");
    }
}
