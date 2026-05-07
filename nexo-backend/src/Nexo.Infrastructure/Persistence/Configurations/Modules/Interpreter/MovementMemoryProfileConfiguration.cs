using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nexo.Domain.Entities;
using Nexo.Domain.Modules.Interpreter;

namespace Nexo.Infrastructure.Persistence.Configurations.Modules.Interpreter;

public class MovementMemoryProfileConfiguration : IEntityTypeConfiguration<MovementMemoryProfile>
{
    public void Configure(EntityTypeBuilder<MovementMemoryProfile> builder)
    {
        builder.ToTable("int_memory_profiles", "nexo");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.UserId).HasColumnName("user_id");
        builder.Property(x => x.ProfileType).HasColumnName("profile_type").HasConversion<int>().IsRequired();
        builder.Property(x => x.ProfileVersion).HasColumnName("profile_version").IsRequired();
        // Compact LLM-ready summary — JSONB for schema evolution without migrations.
        builder.Property(x => x.Summary).HasColumnName("summary").HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.LastRebuildAt).HasColumnName("last_rebuild_at").HasColumnType("timestamptz").IsRequired();
        builder.Property(x => x.MovementsConsidered).HasColumnName("movements_considered").IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamptz").IsRequired();

        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(x => x.TenantId)
            .HasConstraintName("fk_int_memory_profiles_tenants")
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => x.TenantId).HasDatabaseName("ix_int_memory_profiles_tenant_id");
        // One profile per (tenant, user) — null userId = tenant-level profile.
        builder.HasIndex(x => new { x.TenantId, x.UserId }).HasDatabaseName("ix_int_memory_profiles_tenant_user").IsUnique();
    }
}
