using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nexo.Domain.Entities;
using Nexo.Domain.Modules.Interpreter;

namespace Nexo.Infrastructure.Persistence.Configurations.Modules.Interpreter;

public class MovementAuditLogConfiguration : IEntityTypeConfiguration<MovementAuditLog>
{
    public void Configure(EntityTypeBuilder<MovementAuditLog> builder)
    {
        builder.ToTable("int_audit_logs", "nexo");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.MovementId).HasColumnName("movement_id").IsRequired();
        builder.Property(x => x.Action).HasColumnName("action").HasMaxLength(50).IsRequired();
        builder.Property(x => x.ChangedBy).HasColumnName("changed_by").IsRequired();
        // Full state snapshots — JSONB for complete before/after traceability.
        builder.Property(x => x.PreviousState).HasColumnName("previous_state").HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.NewState).HasColumnName("new_state").HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamptz").IsRequired();

        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(x => x.TenantId)
            .HasConstraintName("fk_int_audit_logs_tenants")
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => x.MovementId).HasDatabaseName("ix_int_audit_logs_movement_id");
        builder.HasIndex(x => x.TenantId).HasDatabaseName("ix_int_audit_logs_tenant_id");
        builder.HasIndex(x => new { x.TenantId, x.CreatedAt }).HasDatabaseName("ix_int_audit_logs_tenant_created");
    }
}
