using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nexo.Domain.Entities;

namespace Nexo.Infrastructure.Persistence.Configurations;

public class AuditRecordConfiguration : IEntityTypeConfiguration<AuditRecord>
{
    public void Configure(EntityTypeBuilder<AuditRecord> builder)
    {
        builder.ToTable("audit_records");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");

        // Nullable: null = platform-level action, non-null = tenant action
        builder.Property(x => x.TenantId)
            .HasColumnName("tenant_id");

        builder.Property(x => x.ActionType)
            .HasColumnName("action_type")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(x => x.Severity)
            .HasColumnName("severity")
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(x => x.ActorId)
            .HasColumnName("actor_id");

        builder.Property(x => x.ActorName)
            .HasColumnName("actor_name")
            .HasMaxLength(150);

        builder.Property(x => x.ActorType)
            .HasColumnName("actor_type")
            .HasMaxLength(30)
            .HasDefaultValue("user")
            .IsRequired();

        builder.Property(x => x.EntityType)
            .HasColumnName("entity_type")
            .HasMaxLength(80)
            .IsRequired();

        builder.Property(x => x.EntityId)
            .HasColumnName("entity_id")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(x => x.Description)
            .HasColumnName("description")
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(x => x.MetadataJson)
            .HasColumnName("metadata_json")
            .HasColumnType("jsonb");

        builder.Property(x => x.IpAddress)
            .HasColumnName("ip_address")
            .HasMaxLength(45);

        builder.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamptz")
            .IsRequired();

        // AuditRecords are append-only — UpdatedAt not mapped
        builder.Ignore(x => x.UpdatedAt);

        // Indexes optimized for audit listing queries
        builder.HasIndex(x => new { x.TenantId, x.CreatedAt });
        builder.HasIndex(x => x.ActionType);
        builder.HasIndex(x => x.ActorId);
        builder.HasIndex(x => new { x.EntityType, x.EntityId });
    }
}
