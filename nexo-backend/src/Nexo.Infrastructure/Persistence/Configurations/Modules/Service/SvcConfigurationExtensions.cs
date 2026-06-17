using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nexo.Domain.Common;
using Nexo.Domain.Entities;

namespace Nexo.Infrastructure.Persistence.Configurations.Modules.Service;

/// <summary>
/// Shared EF mapping for store-scoped Service entities: key, tenant/store columns + FKs,
/// is_active, audit columns, and the standard indexes. Constraint and index names derive
/// from <paramref name="table"/> so each entity keeps its own names. Entity-specific columns
/// are mapped by the calling configuration. Produces the same model as the per-entity mapping
/// it replaces (verified with `dotnet ef migrations has-pending-model-changes`).
/// </summary>
internal static class SvcConfigurationExtensions
{
    public static void ConfigureStoreScopedSvcEntity<T>(this EntityTypeBuilder<T> b, string table)
        where T : StoreEntity
    {
        b.ToTable(table, "nexo");
        b.HasKey(x => x.Id);

        b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        b.Property(x => x.StoreId).HasColumnName("store_id").IsRequired();
        b.Property<bool>("IsActive").HasColumnName("is_active").HasDefaultValue(true).IsRequired();
        b.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz").IsRequired();
        b.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamptz").IsRequired();

        b.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(x => x.TenantId)
            .HasConstraintName($"fk_{table}_tenants")
            .OnDelete(DeleteBehavior.Cascade);

        b.HasOne<Store>()
            .WithMany()
            .HasForeignKey(x => x.StoreId)
            .HasConstraintName($"fk_{table}_stores")
            .OnDelete(DeleteBehavior.Restrict);

        b.HasIndex("StoreId").HasDatabaseName($"ix_{table}_store_id");
        b.HasIndex("TenantId", "StoreId", "IsActive").HasDatabaseName($"ix_{table}_tenant_store_active");
    }
}
