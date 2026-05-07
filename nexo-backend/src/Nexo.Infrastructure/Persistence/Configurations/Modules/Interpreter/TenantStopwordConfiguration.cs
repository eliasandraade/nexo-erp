using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nexo.Domain.Entities;
using Nexo.Domain.Modules.Interpreter;

namespace Nexo.Infrastructure.Persistence.Configurations.Modules.Interpreter;

public class TenantStopwordConfiguration : IEntityTypeConfiguration<TenantStopword>
{
    public void Configure(EntityTypeBuilder<TenantStopword> builder)
    {
        builder.ToTable("int_stopwords", "nexo");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        // Stored already normalized (lowercase, no accents) by TenantStopword.Create().
        builder.Property(x => x.Word).HasColumnName("word").HasMaxLength(100).IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamptz").IsRequired();

        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(x => x.TenantId)
            .HasConstraintName("fk_int_stopwords_tenants")
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => x.TenantId).HasDatabaseName("ix_int_stopwords_tenant_id");
        builder.HasIndex(x => new { x.TenantId, x.Word }).HasDatabaseName("ix_int_stopwords_tenant_word").IsUnique();
    }
}
