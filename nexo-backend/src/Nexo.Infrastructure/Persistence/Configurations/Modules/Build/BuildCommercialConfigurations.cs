using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nexo.Domain.Entities;
using Nexo.Domain.Modules.Build;

namespace Nexo.Infrastructure.Persistence.Configurations.Modules.Build;

// Wave 2 — commercial tables (migration BuildWave2Commercial). All bld_*, schema nexo, snake_case.

internal static class BuildCommonColumns
{
    public static void Base<T>(EntityTypeBuilder<T> b, string table) where T : Nexo.Domain.Common.TenantEntity
    {
        b.ToTable(table, "nexo");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        b.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz").IsRequired();
        b.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamptz").IsRequired();
    }
}

public class BuildBudgetTemplateConfiguration : IEntityTypeConfiguration<BuildBudgetTemplate>
{
    public void Configure(EntityTypeBuilder<BuildBudgetTemplate> b)
    {
        BuildCommonColumns.Base(b, "bld_budget_templates");
        b.Property(x => x.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
        b.Property(x => x.Description).HasColumnName("description").HasMaxLength(2000);
        b.Property(x => x.DefaultMarginPercent).HasColumnName("default_margin_percent").HasColumnType("numeric(6,2)").IsRequired();
        b.Property(x => x.CreatedBy).HasColumnName("created_by").IsRequired();

        b.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId)
            .HasConstraintName("fk_bld_budget_templates_tenants").OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => new { x.TenantId, x.Name }).HasDatabaseName("ix_bld_budget_templates_tenant_name");
    }
}

public class BuildBudgetTemplateItemConfiguration : IEntityTypeConfiguration<BuildBudgetTemplateItem>
{
    public void Configure(EntityTypeBuilder<BuildBudgetTemplateItem> b)
    {
        BuildCommonColumns.Base(b, "bld_budget_template_items");
        b.Property(x => x.TemplateId).HasColumnName("template_id").IsRequired();
        b.Property(x => x.Order).HasColumnName("order").IsRequired();
        b.Property(x => x.Name).HasColumnName("name").HasMaxLength(300).IsRequired();
        b.Property(x => x.Kind).HasColumnName("kind").HasConversion<int>().IsRequired();
        b.Property(x => x.Category).HasColumnName("category").HasMaxLength(100).IsRequired();
        b.Property(x => x.Unit).HasColumnName("unit").HasMaxLength(20).IsRequired();
        b.Property(x => x.QuantityPerM2).HasColumnName("quantity_per_m2").HasColumnType("numeric(12,4)");
        b.Property(x => x.FixedQuantity).HasColumnName("fixed_quantity").HasColumnType("numeric(12,3)");
        b.Property(x => x.UnitCost).HasColumnName("unit_cost").HasColumnType("numeric(18,2)").IsRequired();
        b.Property(x => x.StageName).HasColumnName("stage_name").HasMaxLength(200);

        b.HasOne<BuildBudgetTemplate>().WithMany().HasForeignKey(x => x.TemplateId)
            .HasConstraintName("fk_bld_budget_template_items_template").OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => new { x.TenantId, x.TemplateId }).HasDatabaseName("ix_bld_budget_template_items_template");
    }
}

public class BuildChangeOrderConfiguration : IEntityTypeConfiguration<BuildChangeOrder>
{
    public void Configure(EntityTypeBuilder<BuildChangeOrder> b)
    {
        BuildCommonColumns.Base(b, "bld_change_orders");
        b.Ignore(x => x.IsDecided);
        b.Property(x => x.ProjectId).HasColumnName("project_id").IsRequired();
        b.Property(x => x.Number).HasColumnName("number").IsRequired();
        b.Property(x => x.PublicId).HasColumnName("public_id").IsRequired();
        b.Property(x => x.Title).HasColumnName("title").HasMaxLength(200).IsRequired();
        b.Property(x => x.Description).HasColumnName("description").HasMaxLength(4000);
        b.Property(x => x.Reason).HasColumnName("reason").HasConversion<int>().IsRequired();
        b.Property(x => x.CostImpact).HasColumnName("cost_impact").HasColumnType("numeric(18,2)").IsRequired();
        b.Property(x => x.ScheduleImpactDays).HasColumnName("schedule_impact_days").IsRequired();
        b.Property(x => x.StageId).HasColumnName("stage_id");
        b.Property(x => x.Status).HasColumnName("status").HasConversion<int>().IsRequired()
            .IsConcurrencyToken();   // two simultaneous decisions: the second one fails (409)
        b.Property(x => x.SentAt).HasColumnName("sent_at").HasColumnType("timestamptz");
        b.Property(x => x.DecidedAt).HasColumnName("decided_at").HasColumnType("timestamptz");
        b.Property(x => x.DecidedBy).HasColumnName("decided_by").HasMaxLength(200);
        b.Property(x => x.DecidedByUserId).HasColumnName("decided_by_user_id");
        b.Property(x => x.DecisionChannel).HasColumnName("decision_channel").HasConversion<int?>();
        b.Property(x => x.ClientComment).HasColumnName("client_comment").HasMaxLength(2000);
        b.Property(x => x.SignerName).HasColumnName("signer_name").HasMaxLength(200);
        b.Property(x => x.SourceSelectionId).HasColumnName("source_selection_id");
        b.Property(x => x.AppliedToBudget).HasColumnName("applied_to_budget").IsRequired();
        b.Property(x => x.CreatedBy).HasColumnName("created_by").IsRequired();
        b.Property(x => x.CreatedByName).HasColumnName("created_by_name").HasMaxLength(200).IsRequired();

        b.HasOne<BuildProject>().WithMany().HasForeignKey(x => x.ProjectId)
            .HasConstraintName("fk_bld_change_orders_project").OnDelete(DeleteBehavior.Cascade);
        b.HasOne<BuildStage>().WithMany().HasForeignKey(x => x.StageId)
            .HasConstraintName("fk_bld_change_orders_stage").OnDelete(DeleteBehavior.SetNull).IsRequired(false);
        b.HasIndex(x => new { x.TenantId, x.ProjectId, x.Number }).IsUnique()
            .HasDatabaseName("uix_bld_change_orders_project_number");
        b.HasIndex(x => x.PublicId).IsUnique().HasDatabaseName("uix_bld_change_orders_public_id");
        b.HasIndex(x => new { x.TenantId, x.ProjectId, x.Status }).HasDatabaseName("ix_bld_change_orders_project_status");
    }
}

public class BuildChangeOrderItemConfiguration : IEntityTypeConfiguration<BuildChangeOrderItem>
{
    public void Configure(EntityTypeBuilder<BuildChangeOrderItem> b)
    {
        BuildCommonColumns.Base(b, "bld_change_order_items");
        b.Property(x => x.ChangeOrderId).HasColumnName("change_order_id").IsRequired();
        b.Property(x => x.Order).HasColumnName("order").IsRequired();
        b.Property(x => x.Name).HasColumnName("name").HasMaxLength(300).IsRequired();
        b.Property(x => x.Kind).HasColumnName("kind").HasConversion<int>().IsRequired();
        b.Property(x => x.Quantity).HasColumnName("quantity").HasColumnType("numeric(12,3)").IsRequired();
        b.Property(x => x.Unit).HasColumnName("unit").HasMaxLength(20).IsRequired();
        b.Property(x => x.UnitCost).HasColumnName("unit_cost").HasColumnType("numeric(18,2)").IsRequired();
        b.Property(x => x.TotalCost).HasColumnName("total_cost").HasColumnType("numeric(18,2)").IsRequired();

        b.HasOne<BuildChangeOrder>().WithMany().HasForeignKey(x => x.ChangeOrderId)
            .HasConstraintName("fk_bld_change_order_items_change_order").OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => new { x.TenantId, x.ChangeOrderId }).HasDatabaseName("ix_bld_change_order_items_change_order");
    }
}

public class BuildSelectionConfiguration : IEntityTypeConfiguration<BuildSelection>
{
    public void Configure(EntityTypeBuilder<BuildSelection> b)
    {
        BuildCommonColumns.Base(b, "bld_selections");
        b.Property(x => x.ProjectId).HasColumnName("project_id").IsRequired();
        b.Property(x => x.PublicId).HasColumnName("public_id").IsRequired();
        b.Property(x => x.Title).HasColumnName("title").HasMaxLength(200).IsRequired();
        b.Property(x => x.Description).HasColumnName("description").HasMaxLength(4000);
        b.Property(x => x.StageId).HasColumnName("stage_id");
        b.Property(x => x.Deadline).HasColumnName("deadline");
        b.Property(x => x.Status).HasColumnName("status").HasConversion<int>().IsRequired()
            .IsConcurrencyToken();   // two simultaneous decisions: the second one fails (409)
        b.Property(x => x.ClientVisible).HasColumnName("client_visible").IsRequired();
        b.Property(x => x.ChosenOptionId).HasColumnName("chosen_option_id");
        b.Property(x => x.ClientComment).HasColumnName("client_comment").HasMaxLength(2000);
        b.Property(x => x.DecidedAt).HasColumnName("decided_at").HasColumnType("timestamptz");
        b.Property(x => x.ChosenVia).HasColumnName("chosen_via").HasConversion<int?>();
        b.Property(x => x.ConfirmedAt).HasColumnName("confirmed_at").HasColumnType("timestamptz");
        b.Property(x => x.ConfirmedByName).HasColumnName("confirmed_by_name").HasMaxLength(200);
        b.Property(x => x.ChangeOrderId).HasColumnName("change_order_id");
        b.Property(x => x.CreatedBy).HasColumnName("created_by").IsRequired();

        b.HasOne<BuildProject>().WithMany().HasForeignKey(x => x.ProjectId)
            .HasConstraintName("fk_bld_selections_project").OnDelete(DeleteBehavior.Cascade);
        b.HasOne<BuildStage>().WithMany().HasForeignKey(x => x.StageId)
            .HasConstraintName("fk_bld_selections_stage").OnDelete(DeleteBehavior.SetNull).IsRequired(false);
        b.HasIndex(x => x.PublicId).IsUnique().HasDatabaseName("uix_bld_selections_public_id");
        b.HasIndex(x => new { x.TenantId, x.ProjectId, x.Status }).HasDatabaseName("ix_bld_selections_project_status");
    }
}

public class BuildSelectionOptionConfiguration : IEntityTypeConfiguration<BuildSelectionOption>
{
    public void Configure(EntityTypeBuilder<BuildSelectionOption> b)
    {
        BuildCommonColumns.Base(b, "bld_selection_options");
        b.Ignore(x => x.ChangesContract);
        b.Property(x => x.SelectionId).HasColumnName("selection_id").IsRequired();
        b.Property(x => x.PublicId).HasColumnName("public_id").IsRequired();
        b.Property(x => x.Order).HasColumnName("order").IsRequired();
        b.Property(x => x.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
        b.Property(x => x.Description).HasColumnName("description").HasMaxLength(2000);
        b.Property(x => x.PriceDelta).HasColumnName("price_delta").HasColumnType("numeric(18,2)").IsRequired();
        b.Property(x => x.IsStandard).HasColumnName("is_standard").IsRequired();
        b.Property(x => x.ImageKey).HasColumnName("image_key").HasMaxLength(500);
        b.Property(x => x.LeadTimeDays).HasColumnName("lead_time_days");

        b.HasOne<BuildSelection>().WithMany().HasForeignKey(x => x.SelectionId)
            .HasConstraintName("fk_bld_selection_options_selection").OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => new { x.TenantId, x.SelectionId }).HasDatabaseName("ix_bld_selection_options_selection");
        b.HasIndex(x => x.PublicId).IsUnique().HasDatabaseName("uix_bld_selection_options_public_id");
    }
}

public class BuildMaterialRequestConfiguration : IEntityTypeConfiguration<BuildMaterialRequest>
{
    public void Configure(EntityTypeBuilder<BuildMaterialRequest> b)
    {
        BuildCommonColumns.Base(b, "bld_material_requests");
        b.Ignore(x => x.RemainingQuantity);
        b.Property(x => x.ProjectId).HasColumnName("project_id").IsRequired();
        b.Property(x => x.StageId).HasColumnName("stage_id");
        b.Property(x => x.MaterialName).HasColumnName("material_name").HasMaxLength(300).IsRequired();
        b.Property(x => x.Unit).HasColumnName("unit").HasMaxLength(20).IsRequired();
        b.Property(x => x.Quantity).HasColumnName("quantity").HasColumnType("numeric(12,3)").IsRequired();
        b.Property(x => x.NeededBy).HasColumnName("needed_by");
        b.Property(x => x.Status).HasColumnName("status").HasConversion<int>().IsRequired();
        b.Property(x => x.SupplierId).HasColumnName("supplier_id");
        b.Property(x => x.EstimatedUnitCost).HasColumnName("estimated_unit_cost").HasColumnType("numeric(18,2)");
        b.Property(x => x.QuotedAt).HasColumnName("quoted_at").HasColumnType("timestamptz");
        b.Property(x => x.OrderedAt).HasColumnName("ordered_at").HasColumnType("timestamptz");
        b.Property(x => x.DeliveredQuantity).HasColumnName("delivered_quantity").HasColumnType("numeric(12,3)").IsRequired();
        b.Property(x => x.DeliveredAt).HasColumnName("delivered_at");
        b.Property(x => x.CancelledAt).HasColumnName("cancelled_at").HasColumnType("timestamptz");
        b.Property(x => x.MovementId).HasColumnName("movement_id");
        b.Property(x => x.Notes).HasColumnName("notes").HasMaxLength(2000);
        b.Property(x => x.CreatedBy).HasColumnName("created_by").IsRequired();
        b.Property(x => x.CreatedByName).HasColumnName("created_by_name").HasMaxLength(200).IsRequired();

        b.HasOne<BuildProject>().WithMany().HasForeignKey(x => x.ProjectId)
            .HasConstraintName("fk_bld_material_requests_project").OnDelete(DeleteBehavior.Cascade);
        b.HasOne<BuildStage>().WithMany().HasForeignKey(x => x.StageId)
            .HasConstraintName("fk_bld_material_requests_stage").OnDelete(DeleteBehavior.SetNull).IsRequired(false);
        b.HasOne<Supplier>().WithMany().HasForeignKey(x => x.SupplierId)
            .HasConstraintName("fk_bld_material_requests_supplier").OnDelete(DeleteBehavior.SetNull).IsRequired(false);
        b.HasIndex(x => new { x.TenantId, x.ProjectId, x.Status }).HasDatabaseName("ix_bld_material_requests_project_status");
        b.HasIndex(x => x.MovementId).HasDatabaseName("ix_bld_material_requests_movement");
    }
}

public class BuildMaterialDeliveryConfiguration : IEntityTypeConfiguration<BuildMaterialDelivery>
{
    public void Configure(EntityTypeBuilder<BuildMaterialDelivery> b)
    {
        BuildCommonColumns.Base(b, "bld_material_deliveries");
        b.Property(x => x.MaterialRequestId).HasColumnName("material_request_id").IsRequired();
        b.Property(x => x.ProjectId).HasColumnName("project_id").IsRequired();
        b.Property(x => x.Quantity).HasColumnName("quantity").HasColumnType("numeric(12,3)").IsRequired();
        b.Property(x => x.Date).HasColumnName("date").IsRequired();
        b.Property(x => x.Note).HasColumnName("note").HasMaxLength(1000);
        b.Property(x => x.ReceivedBy).HasColumnName("received_by").HasMaxLength(200);
        b.Property(x => x.RecordedBy).HasColumnName("recorded_by").IsRequired();
        b.Property(x => x.RecordedByName).HasColumnName("recorded_by_name").HasMaxLength(200).IsRequired();

        b.HasOne<BuildMaterialRequest>().WithMany().HasForeignKey(x => x.MaterialRequestId)
            .HasConstraintName("fk_bld_material_deliveries_request").OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => new { x.TenantId, x.MaterialRequestId }).HasDatabaseName("ix_bld_material_deliveries_request");
    }
}

public class BuildMeasurementConfiguration : IEntityTypeConfiguration<BuildMeasurement>
{
    public void Configure(EntityTypeBuilder<BuildMeasurement> b)
    {
        BuildCommonColumns.Base(b, "bld_measurements");
        b.Property(x => x.ProjectId).HasColumnName("project_id").IsRequired();
        b.Property(x => x.Number).HasColumnName("number").IsRequired();
        b.Property(x => x.PeriodStart).HasColumnName("period_start").IsRequired();
        b.Property(x => x.PeriodEnd).HasColumnName("period_end").IsRequired();
        b.Property(x => x.Status).HasColumnName("status").HasConversion<int>().IsRequired()
            .IsConcurrencyToken();   // two simultaneous decisions: the second one fails (409)
        b.Property(x => x.Notes).HasColumnName("notes").HasMaxLength(4000);
        b.Property(x => x.SubmittedAt).HasColumnName("submitted_at").HasColumnType("timestamptz");
        b.Property(x => x.ApprovedBy).HasColumnName("approved_by");
        b.Property(x => x.ApprovedByName).HasColumnName("approved_by_name").HasMaxLength(200);
        b.Property(x => x.ApprovedAt).HasColumnName("approved_at").HasColumnType("timestamptz");
        b.Property(x => x.RejectedAt).HasColumnName("rejected_at").HasColumnType("timestamptz");
        b.Property(x => x.RejectedByName).HasColumnName("rejected_by_name").HasMaxLength(200);
        b.Property(x => x.RejectionReason).HasColumnName("rejection_reason").HasMaxLength(1000);
        b.Property(x => x.CreatedBy).HasColumnName("created_by").IsRequired();
        b.Property(x => x.CreatedByName).HasColumnName("created_by_name").HasMaxLength(200).IsRequired();

        b.HasOne<BuildProject>().WithMany().HasForeignKey(x => x.ProjectId)
            .HasConstraintName("fk_bld_measurements_project").OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => new { x.TenantId, x.ProjectId, x.Number }).IsUnique()
            .HasDatabaseName("uix_bld_measurements_project_number");
    }
}

public class BuildMeasurementItemConfiguration : IEntityTypeConfiguration<BuildMeasurementItem>
{
    public void Configure(EntityTypeBuilder<BuildMeasurementItem> b)
    {
        BuildCommonColumns.Base(b, "bld_measurement_items");
        b.Ignore(x => x.DeltaPercent);
        b.Property(x => x.MeasurementId).HasColumnName("measurement_id").IsRequired();
        b.Property(x => x.StageId).HasColumnName("stage_id").IsRequired();
        b.Property(x => x.PreviousPercent).HasColumnName("previous_percent").IsRequired();
        b.Property(x => x.MeasuredPercent).HasColumnName("measured_percent").IsRequired();
        b.Property(x => x.Amount).HasColumnName("amount").HasColumnType("numeric(18,2)");

        b.HasOne<BuildMeasurement>().WithMany().HasForeignKey(x => x.MeasurementId)
            .HasConstraintName("fk_bld_measurement_items_measurement").OnDelete(DeleteBehavior.Cascade);
        // no FK to the stage: an approved measurement is history that outlives a deleted stage
        b.HasIndex(x => new { x.TenantId, x.MeasurementId }).HasDatabaseName("ix_bld_measurement_items_measurement");
    }
}
