using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nexo.Domain.Entities;
using Nexo.Domain.Modules.Build;

namespace Nexo.Infrastructure.Persistence.Configurations.Modules.Build;

// Wave 3 — quality and post-construction tables (migration BuildWave3Quality). All bld_*, schema nexo, snake_case.
// JSON lists use the column type json (not jsonb), like the other Build JSON columns.

public class BuildChecklistTemplateConfiguration : IEntityTypeConfiguration<BuildChecklistTemplate>
{
    public void Configure(EntityTypeBuilder<BuildChecklistTemplate> b)
    {
        BuildCommonColumns.Base(b, "bld_checklist_templates");
        b.Property(x => x.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
        b.Property(x => x.Category).HasColumnName("category").HasMaxLength(100);
        b.Property(x => x.StageNameHint).HasColumnName("stage_name_hint").HasMaxLength(200);
        b.Property(x => x.CreatedBy).HasColumnName("created_by").IsRequired();

        b.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId)
            .HasConstraintName("fk_bld_checklist_templates_tenants").OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => new { x.TenantId, x.Name }).HasDatabaseName("ix_bld_checklist_templates_tenant_name");
    }
}

public class BuildChecklistTemplateItemConfiguration : IEntityTypeConfiguration<BuildChecklistTemplateItem>
{
    public void Configure(EntityTypeBuilder<BuildChecklistTemplateItem> b)
    {
        BuildCommonColumns.Base(b, "bld_checklist_template_items");
        b.Property(x => x.TemplateId).HasColumnName("template_id").IsRequired();
        b.Property(x => x.Order).HasColumnName("order").IsRequired();
        b.Property(x => x.Text).HasColumnName("text").HasMaxLength(500).IsRequired();
        b.Property(x => x.Required).HasColumnName("required").IsRequired();
        b.Property(x => x.RequiresPhoto).HasColumnName("requires_photo").IsRequired();

        b.HasOne<BuildChecklistTemplate>().WithMany().HasForeignKey(x => x.TemplateId)
            .HasConstraintName("fk_bld_checklist_template_items_template").OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => new { x.TenantId, x.TemplateId }).HasDatabaseName("ix_bld_checklist_template_items_template");
    }
}

public class BuildInspectionConfiguration : IEntityTypeConfiguration<BuildInspection>
{
    public void Configure(EntityTypeBuilder<BuildInspection> b)
    {
        BuildCommonColumns.Base(b, "bld_inspections");
        b.Ignore(x => x.IsCompleted);
        b.Ignore(x => x.IsOpen);
        b.Property(x => x.ProjectId).HasColumnName("project_id").IsRequired();
        b.Property(x => x.StageId).HasColumnName("stage_id");
        b.Property(x => x.TemplateId).HasColumnName("template_id");
        b.Property(x => x.Kind).HasColumnName("kind").HasConversion<int>().IsRequired();
        b.Property(x => x.Title).HasColumnName("title").HasMaxLength(200).IsRequired();
        b.Property(x => x.ScheduledFor).HasColumnName("scheduled_for");
        b.Property(x => x.Status).HasColumnName("status").HasConversion<int>().IsRequired()
            .IsConcurrencyToken();   // two simultaneous completions: the second one fails (409)
        b.Property(x => x.InspectorName).HasColumnName("inspector_name").HasMaxLength(200);
        b.Property(x => x.StartedAt).HasColumnName("started_at").HasColumnType("timestamptz");
        b.Property(x => x.CompletedAt).HasColumnName("completed_at").HasColumnType("timestamptz");
        b.Property(x => x.CompletedByName).HasColumnName("completed_by_name").HasMaxLength(200);
        b.Property(x => x.Notes).HasColumnName("notes").HasMaxLength(4000);
        b.Property(x => x.CreatedBy).HasColumnName("created_by").IsRequired();

        b.HasOne<BuildProject>().WithMany().HasForeignKey(x => x.ProjectId)
            .HasConstraintName("fk_bld_inspections_project").OnDelete(DeleteBehavior.Cascade);
        b.HasOne<BuildStage>().WithMany().HasForeignKey(x => x.StageId)
            .HasConstraintName("fk_bld_inspections_stage").OnDelete(DeleteBehavior.SetNull).IsRequired(false);
        // the template may be deleted later: past inspections keep their copied items
        b.HasOne<BuildChecklistTemplate>().WithMany().HasForeignKey(x => x.TemplateId)
            .HasConstraintName("fk_bld_inspections_template").OnDelete(DeleteBehavior.SetNull).IsRequired(false);
        b.HasIndex(x => new { x.TenantId, x.ProjectId, x.Kind, x.Status }).HasDatabaseName("ix_bld_inspections_project_kind_status");
    }
}

public class BuildInspectionItemConfiguration : IEntityTypeConfiguration<BuildInspectionItem>
{
    public void Configure(EntityTypeBuilder<BuildInspectionItem> b)
    {
        BuildCommonColumns.Base(b, "bld_inspection_items");
        b.Ignore(x => x.PhotoKeys);
        b.Ignore(x => x.NonConformitySeverity);
        b.Property(x => x.InspectionId).HasColumnName("inspection_id").IsRequired();
        b.Property(x => x.Order).HasColumnName("order").IsRequired();
        b.Property(x => x.Text).HasColumnName("text").HasMaxLength(500).IsRequired();
        b.Property(x => x.Required).HasColumnName("required").IsRequired();
        b.Property(x => x.RequiresPhoto).HasColumnName("requires_photo").IsRequired();
        b.Property(x => x.Result).HasColumnName("result").HasConversion<int>().IsRequired();
        b.Property(x => x.Comment).HasColumnName("comment").HasMaxLength(2000);
        b.Property(x => x.Severity).HasColumnName("severity").HasConversion<int?>();
        b.Property(x => x.PhotoKeysJson).HasColumnName("photo_keys").HasColumnType("json").IsRequired();

        b.HasOne<BuildInspection>().WithMany().HasForeignKey(x => x.InspectionId)
            .HasConstraintName("fk_bld_inspection_items_inspection").OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => new { x.TenantId, x.InspectionId }).HasDatabaseName("ix_bld_inspection_items_inspection");
    }
}

public class BuildNonConformityConfiguration : IEntityTypeConfiguration<BuildNonConformity>
{
    public void Configure(EntityTypeBuilder<BuildNonConformity> b)
    {
        BuildCommonColumns.Base(b, "bld_non_conformities");
        b.Ignore(x => x.PhotoKeys);
        b.Ignore(x => x.IsOpen);
        b.Ignore(x => x.BlocksCompletion);
        b.Ignore(x => x.VerifiedBySameUser);
        b.Property(x => x.ProjectId).HasColumnName("project_id").IsRequired();
        b.Property(x => x.StageId).HasColumnName("stage_id");
        b.Property(x => x.InspectionId).HasColumnName("inspection_id");
        b.Property(x => x.InspectionItemId).HasColumnName("inspection_item_id");
        b.Property(x => x.Number).HasColumnName("number").IsRequired();
        b.Property(x => x.Title).HasColumnName("title").HasMaxLength(200).IsRequired();
        b.Property(x => x.Description).HasColumnName("description").HasMaxLength(4000);
        b.Property(x => x.Severity).HasColumnName("severity").HasConversion<int>().IsRequired();
        b.Property(x => x.Status).HasColumnName("status").HasConversion<int>().IsRequired()
            .IsConcurrencyToken();   // two simultaneous transitions: the second one fails (409)
        b.Property(x => x.ResponsibleName).HasColumnName("responsible_name").HasMaxLength(200);
        b.Property(x => x.DueDate).HasColumnName("due_date");
        b.Property(x => x.RootCause).HasColumnName("root_cause").HasMaxLength(2000);
        b.Property(x => x.CorrectiveAction).HasColumnName("corrective_action").HasMaxLength(2000);
        b.Property(x => x.PhotoKeysJson).HasColumnName("photo_keys").HasColumnType("json").IsRequired();
        b.Property(x => x.CostImpact).HasColumnName("cost_impact").HasColumnType("numeric(18,2)");
        b.Property(x => x.ResolvedAt).HasColumnName("resolved_at").HasColumnType("timestamptz");
        b.Property(x => x.ResolvedBy).HasColumnName("resolved_by");
        b.Property(x => x.ResolvedByName).HasColumnName("resolved_by_name").HasMaxLength(200);
        b.Property(x => x.VerifiedAt).HasColumnName("verified_at").HasColumnType("timestamptz");
        b.Property(x => x.VerifiedBy).HasColumnName("verified_by");
        b.Property(x => x.VerifiedByName).HasColumnName("verified_by_name").HasMaxLength(200);
        b.Property(x => x.CancelReason).HasColumnName("cancel_reason").HasMaxLength(1000);
        b.Property(x => x.CreatedBy).HasColumnName("created_by").IsRequired();
        b.Property(x => x.CreatedByName).HasColumnName("created_by_name").HasMaxLength(200).IsRequired();

        b.HasOne<BuildProject>().WithMany().HasForeignKey(x => x.ProjectId)
            .HasConstraintName("fk_bld_non_conformities_project").OnDelete(DeleteBehavior.Cascade);
        b.HasOne<BuildStage>().WithMany().HasForeignKey(x => x.StageId)
            .HasConstraintName("fk_bld_non_conformities_stage").OnDelete(DeleteBehavior.SetNull).IsRequired(false);
        b.HasOne<BuildInspection>().WithMany().HasForeignKey(x => x.InspectionId)
            .HasConstraintName("fk_bld_non_conformities_inspection").OnDelete(DeleteBehavior.SetNull).IsRequired(false);
        b.HasOne<BuildInspectionItem>().WithMany().HasForeignKey(x => x.InspectionItemId)
            .HasConstraintName("fk_bld_non_conformities_inspection_item").OnDelete(DeleteBehavior.SetNull).IsRequired(false);
        b.HasIndex(x => new { x.TenantId, x.ProjectId, x.Number }).IsUnique()
            .HasDatabaseName("uix_bld_non_conformities_project_number");
        b.HasIndex(x => new { x.TenantId, x.ProjectId, x.Status, x.Severity }).HasDatabaseName("ix_bld_non_conformities_project_status");
        b.HasIndex(x => new { x.TenantId, x.StageId, x.Severity, x.Status }).HasDatabaseName("ix_bld_non_conformities_stage_severity");
        b.HasIndex(x => new { x.TenantId, x.InspectionItemId }).HasDatabaseName("ix_bld_non_conformities_inspection_item");
    }
}

public class BuildHandoverConfiguration : IEntityTypeConfiguration<BuildHandover>
{
    public void Configure(EntityTypeBuilder<BuildHandover> b)
    {
        BuildCommonColumns.Base(b, "bld_handovers");
        b.Ignore(x => x.Documents);
        b.Ignore(x => x.IsDelivered);
        b.Ignore(x => x.IsAccepted);
        b.Ignore(x => x.CanBeAccepted);
        b.Property(x => x.ProjectId).HasColumnName("project_id").IsRequired();
        b.Property(x => x.Status).HasColumnName("status").HasConversion<int>().IsRequired()
            .IsConcurrencyToken();   // two simultaneous deliveries: the second one fails (409)
        b.Property(x => x.ScheduledFor).HasColumnName("scheduled_for");
        b.Property(x => x.WalkthroughInspectionId).HasColumnName("walkthrough_inspection_id");
        b.Property(x => x.DeliveredAt).HasColumnName("delivered_at").HasColumnType("timestamptz");
        b.Property(x => x.DeliveredByName).HasColumnName("delivered_by_name").HasMaxLength(200);
        b.Property(x => x.KeysDelivered).HasColumnName("keys_delivered").IsRequired();
        b.Property(x => x.DocumentsJson).HasColumnName("documents").HasColumnType("json").IsRequired();
        b.Property(x => x.Notes).HasColumnName("notes").HasMaxLength(4000);
        // the acceptance timestamp is a concurrency token too: two simultaneous acceptances → the second one fails (409)
        b.Property(x => x.AcceptedAt).HasColumnName("accepted_at").HasColumnType("timestamptz").IsConcurrencyToken();
        b.Property(x => x.AcceptedByName).HasColumnName("accepted_by_name").HasMaxLength(200);
        b.Property(x => x.AcceptanceChannel).HasColumnName("acceptance_channel").HasConversion<int?>();
        b.Property(x => x.AcceptanceComment).HasColumnName("acceptance_comment").HasMaxLength(2000);

        b.HasOne<BuildProject>().WithMany().HasForeignKey(x => x.ProjectId)
            .HasConstraintName("fk_bld_handovers_project").OnDelete(DeleteBehavior.Cascade);
        b.HasOne<BuildInspection>().WithMany().HasForeignKey(x => x.WalkthroughInspectionId)
            .HasConstraintName("fk_bld_handovers_walkthrough").OnDelete(DeleteBehavior.SetNull).IsRequired(false);
        b.HasIndex(x => new { x.TenantId, x.ProjectId }).IsUnique().HasDatabaseName("uix_bld_handovers_project");
    }
}

public class BuildWarrantyTermConfiguration : IEntityTypeConfiguration<BuildWarrantyTerm>
{
    public void Configure(EntityTypeBuilder<BuildWarrantyTerm> b)
    {
        BuildCommonColumns.Base(b, "bld_warranty_terms");
        b.Ignore(x => x.ExpiresAt);
        b.Property(x => x.ProjectId).HasColumnName("project_id").IsRequired();
        b.Property(x => x.System).HasColumnName("system").HasMaxLength(100).IsRequired();
        b.Property(x => x.Months).HasColumnName("months").IsRequired();
        b.Property(x => x.StartsAt).HasColumnName("starts_at");
        b.Property(x => x.Description).HasColumnName("description").HasMaxLength(2000);
        b.Property(x => x.IsDefault).HasColumnName("is_default").IsRequired();

        b.HasOne<BuildProject>().WithMany().HasForeignKey(x => x.ProjectId)
            .HasConstraintName("fk_bld_warranty_terms_project").OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => new { x.TenantId, x.ProjectId }).HasDatabaseName("ix_bld_warranty_terms_project");
    }
}

public class BuildAssistanceRequestConfiguration : IEntityTypeConfiguration<BuildAssistanceRequest>
{
    public void Configure(EntityTypeBuilder<BuildAssistanceRequest> b)
    {
        BuildCommonColumns.Base(b, "bld_assistance_requests");
        b.Ignore(x => x.PhotoKeys);
        b.Ignore(x => x.IsOpen);
        b.Ignore(x => x.CanAddPhotos);
        b.Ignore(x => x.CanGiveFeedback);
        b.Property(x => x.ProjectId).HasColumnName("project_id").IsRequired();
        b.Property(x => x.PublicId).HasColumnName("public_id").IsRequired();
        b.Property(x => x.Number).HasColumnName("number").IsRequired();
        b.Property(x => x.Category).HasColumnName("category").HasMaxLength(100).IsRequired();
        b.Property(x => x.Title).HasColumnName("title").HasMaxLength(200).IsRequired();
        b.Property(x => x.Description).HasColumnName("description").HasMaxLength(4000).IsRequired();
        b.Property(x => x.Location).HasColumnName("location").HasMaxLength(200);
        b.Property(x => x.OpenedVia).HasColumnName("opened_via").HasConversion<int>().IsRequired();
        b.Property(x => x.OpenedByName).HasColumnName("opened_by_name").HasMaxLength(200).IsRequired();
        b.Property(x => x.ContactPhone).HasColumnName("contact_phone").HasMaxLength(30);
        b.Property(x => x.OpenedOn).HasColumnName("opened_on").IsRequired();
        b.Property(x => x.Status).HasColumnName("status").HasConversion<int>().IsRequired()
            .IsConcurrencyToken();   // two simultaneous transitions: the second one fails (409)
        b.Property(x => x.TriageNotes).HasColumnName("triage_notes").HasMaxLength(2000);
        b.Property(x => x.WarrantyCovered).HasColumnName("warranty_covered");
        b.Property(x => x.WarrantyTermId).HasColumnName("warranty_term_id");
        b.Property(x => x.TriagedAt).HasColumnName("triaged_at").HasColumnType("timestamptz");
        b.Property(x => x.TriagedByName).HasColumnName("triaged_by_name").HasMaxLength(200);
        b.Property(x => x.ScheduledFor).HasColumnName("scheduled_for").HasColumnType("timestamptz");
        b.Property(x => x.StartedAt).HasColumnName("started_at").HasColumnType("timestamptz");
        b.Property(x => x.ResolvedAt).HasColumnName("resolved_at").HasColumnType("timestamptz");
        b.Property(x => x.ResolutionNotes).HasColumnName("resolution_notes").HasMaxLength(2000);
        b.Property(x => x.ClosedAt).HasColumnName("closed_at").HasColumnType("timestamptz");
        b.Property(x => x.RejectedAt).HasColumnName("rejected_at").HasColumnType("timestamptz");
        b.Property(x => x.RejectionReason).HasColumnName("rejection_reason").HasMaxLength(1000);
        // a rating is given once: the column is a concurrency token so two simultaneous ratings → 409
        b.Property(x => x.ClientRating).HasColumnName("client_rating").IsConcurrencyToken();
        b.Property(x => x.ClientFeedback).HasColumnName("client_feedback").HasMaxLength(2000);
        b.Property(x => x.FeedbackAt).HasColumnName("feedback_at").HasColumnType("timestamptz");
        b.Property(x => x.PhotoKeysJson).HasColumnName("photo_keys").HasColumnType("json").IsRequired();

        b.HasOne<BuildProject>().WithMany().HasForeignKey(x => x.ProjectId)
            .HasConstraintName("fk_bld_assistance_requests_project").OnDelete(DeleteBehavior.Cascade);
        b.HasOne<BuildWarrantyTerm>().WithMany().HasForeignKey(x => x.WarrantyTermId)
            .HasConstraintName("fk_bld_assistance_requests_warranty_term").OnDelete(DeleteBehavior.SetNull).IsRequired(false);
        b.HasIndex(x => new { x.TenantId, x.ProjectId, x.Number }).IsUnique()
            .HasDatabaseName("uix_bld_assistance_requests_project_number");
        b.HasIndex(x => x.PublicId).IsUnique().HasDatabaseName("uix_bld_assistance_requests_public_id");
        b.HasIndex(x => new { x.TenantId, x.ProjectId, x.Status }).HasDatabaseName("ix_bld_assistance_requests_project_status");
    }
}
