using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nexo.Domain.Modules.Service;

namespace Nexo.Infrastructure.Persistence.Configurations.Modules.Service;

public class SvcTimeBlockConfiguration : IEntityTypeConfiguration<SvcTimeBlock>
{
    public void Configure(EntityTypeBuilder<SvcTimeBlock> builder)
    {
        builder.ConfigureStoreScopedSvcEntityNoActive("svc_time_blocks");

        builder.Property(x => x.ProfessionalId).HasColumnName("professional_id");
        builder.Property(x => x.StartsAt).HasColumnName("starts_at").HasColumnType("timestamptz").IsRequired();
        builder.Property(x => x.EndsAt).HasColumnName("ends_at").HasColumnType("timestamptz").IsRequired();
        builder.Property(x => x.Reason).HasColumnName("reason").HasMaxLength(200);
        builder.Property(x => x.CreatedBy).HasColumnName("created_by").IsRequired();

        builder.HasOne<SvcProfessional>().WithMany().HasForeignKey(x => x.ProfessionalId)
            .HasConstraintName("fk_svc_time_blocks_professionals").OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex("TenantId", "StoreId", "StartsAt").HasDatabaseName("ix_svc_time_blocks_store_starts");
    }
}
