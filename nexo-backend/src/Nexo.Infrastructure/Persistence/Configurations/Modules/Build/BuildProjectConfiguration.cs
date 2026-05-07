using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nexo.Domain.Entities;
using Nexo.Domain.Modules.Build;

namespace Nexo.Infrastructure.Persistence.Configurations.Modules.Build;

public class BuildProjectConfiguration : IEntityTypeConfiguration<BuildProject>
{
    public void Configure(EntityTypeBuilder<BuildProject> builder)
    {
        builder.ToTable("bld_projects", "nexo");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
        builder.Property(x => x.ClientName).HasColumnName("client_name").HasMaxLength(200).IsRequired();
        builder.Property(x => x.Location).HasColumnName("location").HasMaxLength(500);
        builder.Property(x => x.Status).HasColumnName("status").HasConversion<int>().IsRequired();
        builder.Property(x => x.Type).HasColumnName("type").HasConversion<int>().IsRequired();
        builder.Property(x => x.StartDate).HasColumnName("start_date");
        builder.Property(x => x.ExpectedEndDate).HasColumnName("expected_end_date");
        builder.Property(x => x.ActualEndDate).HasColumnName("actual_end_date");
        builder.Property(x => x.BudgetEstimated).HasColumnName("budget_estimated").HasColumnType("numeric(18,2)");
        builder.Property(x => x.BudgetApproved).HasColumnName("budget_approved").HasColumnType("numeric(18,2)");
        builder.Property(x => x.CreatedBy).HasColumnName("created_by").IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamptz").IsRequired();

        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(x => x.TenantId)
            .HasConstraintName("fk_bld_projects_tenants")
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(x => x.Stages)
            .WithOne()
            .HasForeignKey(x => x.ProjectId)
            .HasConstraintName("fk_bld_stages_project")
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(x => x.DailyLogs)
            .WithOne()
            .HasForeignKey(x => x.ProjectId)
            .HasConstraintName("fk_bld_daily_logs_project")
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => x.TenantId).HasDatabaseName("ix_bld_projects_tenant_id");
        builder.HasIndex(x => new { x.TenantId, x.Status }).HasDatabaseName("ix_bld_projects_tenant_status");
    }
}
