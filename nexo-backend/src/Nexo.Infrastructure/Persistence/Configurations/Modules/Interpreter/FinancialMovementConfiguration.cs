using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nexo.Domain.Entities;
using Nexo.Domain.Modules.Interpreter;

namespace Nexo.Infrastructure.Persistence.Configurations.Modules.Interpreter;

public class FinancialMovementConfiguration : IEntityTypeConfiguration<FinancialMovement>
{
    public void Configure(EntityTypeBuilder<FinancialMovement> builder)
    {
        builder.ToTable("int_movements", "nexo");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.Direction).HasColumnName("direction").HasConversion<int>().IsRequired();
        builder.Property(x => x.Nature).HasColumnName("nature").HasConversion<int>().IsRequired();
        builder.Property(x => x.Amount).HasColumnName("amount").HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(x => x.Date).HasColumnName("date").IsRequired();
        builder.Property(x => x.Description).HasColumnName("description").HasMaxLength(500).IsRequired();
        builder.Property(x => x.NormalizedDescription).HasColumnName("normalized_description").HasMaxLength(500).IsRequired();
        builder.Property(x => x.CategoryId).HasColumnName("category_id");
        builder.Property(x => x.ContextType).HasColumnName("context_type").HasConversion<int>().IsRequired();
        builder.Property(x => x.ContextId).HasColumnName("context_id");
        builder.Property(x => x.AccountId).HasColumnName("account_id");
        builder.Property(x => x.Status).HasColumnName("status").HasConversion<int>().IsRequired();
        builder.Property(x => x.CreatedBy).HasColumnName("created_by").IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamptz").IsRequired();

        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(x => x.TenantId)
            .HasConstraintName("fk_int_movements_tenants")
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => x.TenantId).HasDatabaseName("ix_int_movements_tenant_id");
        builder.HasIndex(x => new { x.ContextType, x.ContextId }).HasDatabaseName("ix_int_movements_context");
        builder.HasIndex(x => new { x.TenantId, x.Date }).HasDatabaseName("ix_int_movements_tenant_date");
        builder.HasIndex(x => new { x.TenantId, x.Status }).HasDatabaseName("ix_int_movements_tenant_status");
        builder.HasIndex(x => x.CreatedBy).HasDatabaseName("ix_int_movements_created_by");
    }
}
