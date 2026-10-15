using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nexo.Domain.Entities;

namespace Nexo.Infrastructure.Persistence.Configurations;

/// <summary>error_events — platform-level (no tenant filter), one row per fingerprint.</summary>
public class ErrorEventConfiguration : IEntityTypeConfiguration<ErrorEvent>
{
    public void Configure(EntityTypeBuilder<ErrorEvent> b)
    {
        b.ToTable("error_events");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.Fingerprint).HasColumnName("fingerprint").HasMaxLength(64).IsRequired();
        b.HasIndex(x => x.Fingerprint).IsUnique().HasDatabaseName("ux_error_events_fingerprint");
        b.Property(x => x.ExceptionType).HasColumnName("exception_type").HasMaxLength(200).IsRequired();
        b.Property(x => x.Summary).HasColumnName("summary").HasMaxLength(500).IsRequired();
        b.Property(x => x.Route).HasColumnName("route").HasMaxLength(200).IsRequired();
        b.Property(x => x.Method).HasColumnName("method").HasMaxLength(10).IsRequired();
        b.Property(x => x.FirstSeenAt).HasColumnName("first_seen_at").HasColumnType("timestamptz").IsRequired();
        b.Property(x => x.LastSeenAt).HasColumnName("last_seen_at").HasColumnType("timestamptz").IsRequired();
        b.HasIndex(x => x.LastSeenAt).HasDatabaseName("ix_error_events_last_seen_at");
        b.Property(x => x.Count).HasColumnName("count").IsRequired();
        b.Property(x => x.LastRequestId).HasColumnName("last_request_id").HasMaxLength(64);
        b.Property(x => x.LastTenantId).HasColumnName("last_tenant_id");
        b.Property(x => x.StackTop).HasColumnName("stack_top").HasMaxLength(2000).IsRequired();
        b.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz").IsRequired();
        b.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamptz").IsRequired();
    }
}
