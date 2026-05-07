using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nexo.Domain.Entities;
using Nexo.Domain.Modules.Interpreter;

namespace Nexo.Infrastructure.Persistence.Configurations.Modules.Interpreter;

public class UserCorrectionConfiguration : IEntityTypeConfiguration<UserCorrection>
{
    public void Configure(EntityTypeBuilder<UserCorrection> builder)
    {
        builder.ToTable("int_user_corrections", "nexo");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.SuggestionId).HasColumnName("suggestion_id").IsRequired();
        builder.Property(x => x.MovementId).HasColumnName("movement_id").IsRequired();
        builder.Property(x => x.UserId).HasColumnName("user_id").IsRequired();
        builder.Property(x => x.CorrectionType).HasColumnName("correction_type").HasConversion<int>().IsRequired();
        builder.Property(x => x.OriginalValue).HasColumnName("original_value").HasMaxLength(500).IsRequired();
        builder.Property(x => x.CorrectedValue).HasColumnName("corrected_value").HasMaxLength(500).IsRequired();
        builder.Property(x => x.RawUserText).HasColumnName("raw_user_text").HasMaxLength(1000);
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamptz").IsRequired();

        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(x => x.TenantId)
            .HasConstraintName("fk_int_corrections_tenants")
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => x.TenantId).HasDatabaseName("ix_int_corrections_tenant_id");
        builder.HasIndex(x => x.MovementId).HasDatabaseName("ix_int_corrections_movement_id");
        builder.HasIndex(x => new { x.TenantId, x.CorrectionType }).HasDatabaseName("ix_int_corrections_tenant_type");
        builder.HasIndex(x => new { x.TenantId, x.CreatedAt }).HasDatabaseName("ix_int_corrections_tenant_created");
    }
}
