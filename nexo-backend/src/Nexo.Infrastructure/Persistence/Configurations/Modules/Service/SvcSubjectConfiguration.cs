using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nexo.Domain.Entities;
using Nexo.Domain.Modules.Service;

namespace Nexo.Infrastructure.Persistence.Configurations.Modules.Service;

public class SvcSubjectConfiguration : IEntityTypeConfiguration<SvcSubject>
{
    public void Configure(EntityTypeBuilder<SvcSubject> builder)
    {
        // Key, tenant column + FK, is_active, audit columns.
        builder.ConfigureTenantScopedSvcEntity("svc_subjects");

        builder.Property(x => x.CustomerId).HasColumnName("customer_id").IsRequired();
        builder.Property(x => x.Kind)
            .HasColumnName("kind").HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.DisplayName).HasColumnName("display_name").HasMaxLength(200).IsRequired();
        builder.Property(x => x.MetadataJson).HasColumnName("metadata_json").HasColumnType("jsonb");
        builder.Property(x => x.Notes).HasColumnName("notes").HasMaxLength(2000);

        builder.HasOne<Customer>()
            .WithMany()
            .HasForeignKey(x => x.CustomerId)
            .HasConstraintName("fk_svc_subjects_customers")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.CustomerId).HasDatabaseName("ix_svc_subjects_customer_id");
        builder.HasIndex("TenantId", "IsActive").HasDatabaseName("ix_svc_subjects_tenant_active");
    }
}
