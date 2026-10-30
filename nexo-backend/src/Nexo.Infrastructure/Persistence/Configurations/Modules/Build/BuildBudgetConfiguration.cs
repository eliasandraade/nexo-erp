using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nexo.Domain.Entities;
using Nexo.Domain.Modules.Build;

namespace Nexo.Infrastructure.Persistence.Configurations.Modules.Build;

public class BuildBudgetConfiguration : IEntityTypeConfiguration<BuildBudget>
{
    public void Configure(EntityTypeBuilder<BuildBudget> builder)
    {
        builder.ToTable("bld_budgets", "nexo");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.ProjectId).HasColumnName("project_id");
        builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").HasConversion<int>().IsRequired();
        builder.Property(x => x.TotalCost).HasColumnName("total_cost").HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(x => x.MarginPercent).HasColumnName("margin_percent").HasColumnType("numeric(6,2)").IsRequired();
        builder.Property(x => x.FinalPrice).HasColumnName("final_price").HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(x => x.CreatedBy).HasColumnName("created_by").IsRequired();
        // Wave 2 — commercial terms, versions, decision trail (all nullable or defaulted for legacy rows)
        builder.Property(x => x.ValidUntil).HasColumnName("valid_until");
        builder.Property(x => x.Notes).HasColumnName("notes").HasMaxLength(4000);
        builder.Property(x => x.InternalNotes).HasColumnName("internal_notes").HasMaxLength(4000);
        builder.Property(x => x.AreaM2).HasColumnName("area_m2").HasColumnType("numeric(12,2)");
        builder.Property(x => x.Version).HasColumnName("version").IsRequired();
        builder.Property(x => x.ParentBudgetId).HasColumnName("parent_budget_id");
        builder.Property(x => x.RootBudgetId).HasColumnName("root_budget_id");
        builder.Property(x => x.SourceTemplateId).HasColumnName("source_template_id");
        builder.Property(x => x.ApprovedAt).HasColumnName("approved_at").HasColumnType("timestamptz");
        builder.Property(x => x.ApprovedBy).HasColumnName("approved_by");
        builder.Property(x => x.ApprovedByName).HasColumnName("approved_by_name").HasMaxLength(200);
        builder.Property(x => x.RejectedAt).HasColumnName("rejected_at").HasColumnType("timestamptz");
        builder.Property(x => x.RejectedBy).HasColumnName("rejected_by");
        builder.Property(x => x.RejectedByName).HasColumnName("rejected_by_name").HasMaxLength(200);
        builder.Property(x => x.RejectionReason).HasColumnName("rejection_reason").HasMaxLength(1000);
        builder.Ignore(x => x.FamilyId);
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamptz").IsRequired();

        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(x => x.TenantId)
            .HasConstraintName("fk_bld_budgets_tenants")
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(x => x.Items)
            .WithOne()
            .HasForeignKey(x => x.BudgetId)
            .HasConstraintName("fk_bld_budget_items_budget")
            .OnDelete(DeleteBehavior.Cascade);

        // ProjectId is nullable FK — optional link to an existing project
        builder.HasOne<BuildProject>()
            .WithMany()
            .HasForeignKey(x => x.ProjectId)
            .HasConstraintName("fk_bld_budgets_project")
            .OnDelete(DeleteBehavior.SetNull)
            .IsRequired(false);

        builder.HasIndex(x => x.TenantId).HasDatabaseName("ix_bld_budgets_tenant_id");
        builder.HasIndex(x => new { x.TenantId, x.Status }).HasDatabaseName("ix_bld_budgets_tenant_status");
        builder.HasIndex(x => x.ProjectId).HasDatabaseName("ix_bld_budgets_project_id");
        // one number per version of a family (the first budget, root null, is always version 1)
        builder.HasIndex(x => new { x.TenantId, x.RootBudgetId, x.Version }).IsUnique()
            .HasDatabaseName("uix_bld_budgets_family_version").HasFilter("root_budget_id IS NOT NULL");

        // deleting a template keeps the budgets made from it
        builder.HasOne<BuildBudgetTemplate>()
            .WithMany()
            .HasForeignKey(x => x.SourceTemplateId)
            .HasConstraintName("fk_bld_budgets_template")
            .OnDelete(DeleteBehavior.SetNull)
            .IsRequired(false);
    }
}
