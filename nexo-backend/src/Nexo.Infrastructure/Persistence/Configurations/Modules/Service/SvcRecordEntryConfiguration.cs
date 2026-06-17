using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nexo.Domain.Modules.Service;

namespace Nexo.Infrastructure.Persistence.Configurations.Modules.Service;

public class SvcRecordEntryConfiguration : IEntityTypeConfiguration<SvcRecordEntry>
{
    public void Configure(EntityTypeBuilder<SvcRecordEntry> builder)
    {
        // Key, tenant/store columns + FKs, audit columns (no is_active — records are append-only).
        builder.ConfigureStoreScopedSvcEntityNoActive("svc_record_entries");

        builder.Property(x => x.ContextType)
            .HasColumnName("context_type").HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.ContextId).HasColumnName("context_id").IsRequired();
        builder.Property(x => x.AuthorUserId).HasColumnName("author_user_id").IsRequired();
        builder.Property(x => x.Text).HasColumnName("text").HasMaxLength(10000);
        builder.Property(x => x.AttachmentsJson).HasColumnName("attachments_json").HasColumnType("jsonb");

        // ContextId is polymorphic (customer or subject) — no DB FK; integrity is app-enforced.
        builder.HasIndex("TenantId", "StoreId", "ContextType", "ContextId")
            .HasDatabaseName("ix_svc_record_entries_context");
    }
}
