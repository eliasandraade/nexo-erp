using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nexo.Domain.Modules.Build;

namespace Nexo.Infrastructure.Persistence.Configurations.Modules.Build;

public class BuildBudgetItemConfiguration : IEntityTypeConfiguration<BuildBudgetItem>
{
    public void Configure(EntityTypeBuilder<BuildBudgetItem> builder)
    {
        builder.ToTable("bld_budget_items", "nexo");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.BudgetId).HasColumnName("budget_id").IsRequired();
        builder.Property(x => x.StageId).HasColumnName("stage_id");
        builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(300).IsRequired();
        builder.Property(x => x.Category).HasColumnName("category").HasMaxLength(100).IsRequired();
        builder.Property(x => x.Quantity).HasColumnName("quantity").HasColumnType("numeric(12,3)").IsRequired();
        builder.Property(x => x.Unit).HasColumnName("unit").HasMaxLength(20).IsRequired();
        builder.Property(x => x.UnitCost).HasColumnName("unit_cost").HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(x => x.TotalCost).HasColumnName("total_cost").HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamptz").IsRequired();

        // StageId is optional — items may not be linked to a specific stage
        builder.HasOne<BuildStage>()
            .WithMany()
            .HasForeignKey(x => x.StageId)
            .HasConstraintName("fk_bld_budget_items_stage")
            .OnDelete(DeleteBehavior.SetNull)
            .IsRequired(false);

        builder.HasIndex(x => x.TenantId).HasDatabaseName("ix_bld_budget_items_tenant_id");
        builder.HasIndex(x => x.BudgetId).HasDatabaseName("ix_bld_budget_items_budget_id");
    }
}
