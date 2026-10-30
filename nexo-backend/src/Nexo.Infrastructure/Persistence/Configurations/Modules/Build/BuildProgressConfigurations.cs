using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nexo.Domain.Entities;
using Nexo.Domain.Modules.Build;

namespace Nexo.Infrastructure.Persistence.Configurations.Modules.Build;

public class BuildStageProgressEventConfiguration : IEntityTypeConfiguration<BuildStageProgressEvent>
{
    public void Configure(EntityTypeBuilder<BuildStageProgressEvent> b)
    {
        b.ToTable("bld_stage_progress_events", "nexo");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        b.Property(x => x.ProjectId).HasColumnName("project_id").IsRequired();
        b.Property(x => x.StageId).HasColumnName("stage_id").IsRequired();
        b.Property(x => x.PreviousPercent).HasColumnName("previous_percent").IsRequired();
        b.Property(x => x.Percent).HasColumnName("percent").IsRequired();
        b.Property(x => x.Source).HasColumnName("source").HasConversion<int>().IsRequired();
        b.Property(x => x.SourceRef).HasColumnName("source_ref");
        b.Property(x => x.Note).HasColumnName("note").HasMaxLength(1000);
        b.Property(x => x.EffectiveDate).HasColumnName("effective_date").IsRequired();
        b.Property(x => x.RecordedBy).HasColumnName("recorded_by").IsRequired();
        b.Property(x => x.RecordedByName).HasColumnName("recorded_by_name").HasMaxLength(200).IsRequired();
        b.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz").IsRequired();
        b.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamptz").IsRequired();

        b.HasOne<BuildProject>().WithMany().HasForeignKey(x => x.ProjectId)
            .HasConstraintName("fk_bld_stage_progress_events_project").OnDelete(DeleteBehavior.Cascade);
        // no FK to the stage: the history outlives a deleted stage
        b.HasIndex(x => new { x.TenantId, x.ProjectId, x.CreatedAt }).HasDatabaseName("ix_bld_stage_progress_events_project");
        b.HasIndex(x => x.StageId).HasDatabaseName("ix_bld_stage_progress_events_stage");
    }
}

public class BuildScheduleRevisionConfiguration : IEntityTypeConfiguration<BuildScheduleRevision>
{
    public void Configure(EntityTypeBuilder<BuildScheduleRevision> b)
    {
        b.ToTable("bld_schedule_revisions", "nexo");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        b.Property(x => x.ProjectId).HasColumnName("project_id").IsRequired();
        b.Property(x => x.Number).HasColumnName("number").IsRequired();
        b.Property(x => x.Kind).HasColumnName("kind").HasConversion<int>().IsRequired();
        b.Property(x => x.Reason).HasColumnName("reason").HasMaxLength(1000);
        b.Property(x => x.ItemsJson).HasColumnName("items").HasColumnType("json").IsRequired();
        b.Property(x => x.CreatedBy).HasColumnName("created_by").IsRequired();
        b.Property(x => x.CreatedByName).HasColumnName("created_by_name").HasMaxLength(200).IsRequired();
        b.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz").IsRequired();
        b.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamptz").IsRequired();

        b.HasOne<BuildProject>().WithMany().HasForeignKey(x => x.ProjectId)
            .HasConstraintName("fk_bld_schedule_revisions_project").OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => new { x.TenantId, x.ProjectId, x.Number }).IsUnique()
            .HasDatabaseName("uix_bld_schedule_revisions_project_number");
    }
}

public class BuildProgressSnapshotConfiguration : IEntityTypeConfiguration<BuildProgressSnapshot>
{
    public void Configure(EntityTypeBuilder<BuildProgressSnapshot> b)
    {
        b.ToTable("bld_progress_snapshots", "nexo");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        b.Property(x => x.ProjectId).HasColumnName("project_id").IsRequired();
        b.Property(x => x.Date).HasColumnName("date").IsRequired();
        b.Property(x => x.PlannedPercent).HasColumnName("planned_percent").HasColumnType("numeric(5,2)");
        b.Property(x => x.ActualPercent).HasColumnName("actual_percent").HasColumnType("numeric(5,2)").IsRequired();
        b.Property(x => x.ForecastPercent).HasColumnName("forecast_percent").HasColumnType("numeric(5,2)");
        b.Property(x => x.ForecastDeliveryDate).HasColumnName("forecast_delivery_date");
        b.Property(x => x.StagesJson).HasColumnName("stages").HasColumnType("json").IsRequired();
        b.Property(x => x.Methodology).HasColumnName("methodology").HasMaxLength(60).IsRequired();
        b.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz").IsRequired();
        b.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamptz").IsRequired();

        b.HasOne<BuildProject>().WithMany().HasForeignKey(x => x.ProjectId)
            .HasConstraintName("fk_bld_progress_snapshots_project").OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => new { x.ProjectId, x.Date }).IsUnique().HasDatabaseName("uix_bld_progress_snapshots_project_date");
        b.HasIndex(x => x.TenantId).HasDatabaseName("ix_bld_progress_snapshots_tenant_id");
    }
}

public class BuildClientUpdateConfiguration : IEntityTypeConfiguration<BuildClientUpdate>
{
    public void Configure(EntityTypeBuilder<BuildClientUpdate> b)
    {
        b.ToTable("bld_client_updates", "nexo");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        b.Property(x => x.ProjectId).HasColumnName("project_id").IsRequired();
        b.Property(x => x.Kind).HasColumnName("kind").HasConversion<int>().IsRequired();
        b.Property(x => x.Title).HasColumnName("title").HasMaxLength(200).IsRequired();
        b.Property(x => x.Body).HasColumnName("body").HasMaxLength(10000).IsRequired();
        b.Property(x => x.Status).HasColumnName("status").HasConversion<int>().IsRequired();
        b.Property(x => x.PublishedAt).HasColumnName("published_at").HasColumnType("timestamptz");
        b.Property(x => x.Source).HasColumnName("source").HasConversion<int>().IsRequired();
        b.Property(x => x.CreatedBy).HasColumnName("created_by").IsRequired();
        b.Property(x => x.CreatedByName).HasColumnName("created_by_name").HasMaxLength(200).IsRequired();
        b.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz").IsRequired();
        b.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamptz").IsRequired();

        b.HasOne<BuildProject>().WithMany().HasForeignKey(x => x.ProjectId)
            .HasConstraintName("fk_bld_client_updates_project").OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => new { x.TenantId, x.ProjectId, x.Status, x.PublishedAt }).HasDatabaseName("ix_bld_client_updates_project_status");
    }
}

public class BuildPortalCompanyConfiguration : IEntityTypeConfiguration<BuildPortalCompany>
{
    public void Configure(EntityTypeBuilder<BuildPortalCompany> b)
    {
        b.ToTable("bld_portal_companies", "nexo");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        b.Property(x => x.Slug).HasColumnName("slug").HasMaxLength(60).IsRequired();
        b.Property(x => x.DisplayName).HasColumnName("display_name").HasMaxLength(200).IsRequired();
        b.Property(x => x.LogoKey).HasColumnName("logo_key").HasMaxLength(500);
        b.Property(x => x.ContactEmail).HasColumnName("contact_email").HasMaxLength(200);
        b.Property(x => x.ContactPhone).HasColumnName("contact_phone").HasMaxLength(40);
        b.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz").IsRequired();
        b.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamptz").IsRequired();

        b.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId)
            .HasConstraintName("fk_bld_portal_companies_tenants").OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => x.TenantId).IsUnique().HasDatabaseName("uix_bld_portal_companies_tenant");
        b.HasIndex(x => x.Slug).IsUnique().HasDatabaseName("uix_bld_portal_companies_slug");
    }
}

public class BuildProjectPortalConfiguration : IEntityTypeConfiguration<BuildProjectPortal>
{
    public void Configure(EntityTypeBuilder<BuildProjectPortal> b)
    {
        b.ToTable("bld_project_portals", "nexo");
        b.HasKey(x => x.Id);
        b.Ignore(x => x.Visibility);
        b.Ignore(x => x.HasPassword);
        b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        b.Property(x => x.ProjectId).HasColumnName("project_id").IsRequired();
        b.Property(x => x.Slug).HasColumnName("slug").HasMaxLength(60);
        b.Property(x => x.IsPublished).HasColumnName("is_published").IsRequired();
        b.Property(x => x.PublishedAt).HasColumnName("published_at").HasColumnType("timestamptz");
        b.Property(x => x.Access).HasColumnName("access").HasConversion<int>().IsRequired();
        b.Property(x => x.PasswordHash).HasColumnName("password_hash").HasMaxLength(200);
        b.Property(x => x.PasswordVersion).HasColumnName("password_version").IsRequired();
        b.Property(x => x.FailedAttempts).HasColumnName("failed_attempts").IsRequired();
        b.Property(x => x.LockedUntil).HasColumnName("locked_until").HasColumnType("timestamptz");
        b.Property(x => x.Title).HasColumnName("title").HasMaxLength(200);
        b.Property(x => x.WelcomeMessage).HasColumnName("welcome_message").HasMaxLength(2000);
        b.Property(x => x.LogoKey).HasColumnName("logo_key").HasMaxLength(500);
        b.Property(x => x.CoverKey).HasColumnName("cover_key").HasMaxLength(500);
        b.Property(x => x.PrimaryColor).HasColumnName("primary_color").HasMaxLength(7);
        b.Property(x => x.AccentColor).HasColumnName("accent_color").HasMaxLength(7);
        b.Property(x => x.ContactName).HasColumnName("contact_name").HasMaxLength(200);
        b.Property(x => x.ContactPhone).HasColumnName("contact_phone").HasMaxLength(40);
        b.Property(x => x.ContactEmail).HasColumnName("contact_email").HasMaxLength(200);
        b.Property(x => x.ContactWhatsApp).HasColumnName("contact_whatsapp").HasMaxLength(40);
        b.Property(x => x.VisibilityJson).HasColumnName("visibility").HasColumnType("json").IsRequired();
        b.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz").IsRequired();
        b.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamptz").IsRequired();

        b.HasOne<BuildProject>().WithMany().HasForeignKey(x => x.ProjectId)
            .HasConstraintName("fk_bld_project_portals_project").OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => x.ProjectId).IsUnique().HasDatabaseName("uix_bld_project_portals_project");
        b.HasIndex(x => new { x.TenantId, x.Slug }).IsUnique().HasDatabaseName("uix_bld_project_portals_tenant_slug")
            .HasFilter("slug IS NOT NULL");
    }
}

public class BuildPortalSessionConfiguration : IEntityTypeConfiguration<BuildPortalSession>
{
    public void Configure(EntityTypeBuilder<BuildPortalSession> b)
    {
        b.ToTable("bld_portal_sessions", "nexo");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        b.Property(x => x.PortalId).HasColumnName("portal_id").IsRequired();
        b.Property(x => x.TokenHash).HasColumnName("token_hash").HasMaxLength(64).IsRequired();
        b.Property(x => x.PasswordVersion).HasColumnName("password_version").IsRequired();
        b.Property(x => x.ExpiresAt).HasColumnName("expires_at").HasColumnType("timestamptz").IsRequired();
        b.Property(x => x.RevokedAt).HasColumnName("revoked_at").HasColumnType("timestamptz");
        b.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz").IsRequired();
        b.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamptz").IsRequired();

        b.HasOne<BuildProjectPortal>().WithMany().HasForeignKey(x => x.PortalId)
            .HasConstraintName("fk_bld_portal_sessions_portal").OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => x.TokenHash).IsUnique().HasDatabaseName("uix_bld_portal_sessions_token_hash");
        b.HasIndex(x => new { x.PortalId, x.ExpiresAt }).HasDatabaseName("ix_bld_portal_sessions_portal");
    }
}
