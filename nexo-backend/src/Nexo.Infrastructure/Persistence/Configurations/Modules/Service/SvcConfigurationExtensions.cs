using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nexo.Domain.Common;
using Nexo.Domain.Entities;

namespace Nexo.Infrastructure.Persistence.Configurations.Modules.Service;

/// <summary>
/// Shared EF mapping for the Service (svc_*) tables: key, tenant/store columns + FKs, optional
/// is_active, audit columns, and the standard indexes. Constraint and index names derive from
/// the table name so each entity keeps its own. Entity-specific columns are mapped by the
/// calling configuration. The store-scoped + is_active helper produces the same model as the
/// PR1 mapping it replaces — verified with `dotnet ef migrations has-pending-model-changes`.
/// </summary>
internal static class SvcConfigurationExtensions
{
    // Common to every svc_* table: table+schema, PK, tenant column + FK, audit columns.
    private static void ConfigureKeyTenantAudit<T>(this EntityTypeBuilder<T> b, string table)
        where T : TenantEntity
    {
        b.ToTable(table, "nexo");
        b.HasKey(x => x.Id);

        b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        b.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz").IsRequired();
        b.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamptz").IsRequired();

        b.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(x => x.TenantId)
            .HasConstraintName($"fk_{table}_tenants")
            .OnDelete(DeleteBehavior.Cascade);
    }

    // store_id column + FK + its index, for StoreEntity tables.
    private static void ConfigureStore<T>(this EntityTypeBuilder<T> b, string table)
        where T : StoreEntity
    {
        b.Property(x => x.StoreId).HasColumnName("store_id").IsRequired();

        b.HasOne<Store>()
            .WithMany()
            .HasForeignKey(x => x.StoreId)
            .HasConstraintName($"fk_{table}_stores")
            .OnDelete(DeleteBehavior.Restrict);

        b.HasIndex("StoreId").HasDatabaseName($"ix_{table}_store_id");
    }

    private static void ConfigureIsActive<T>(this EntityTypeBuilder<T> b) where T : class
        => b.Property<bool>("IsActive").HasColumnName("is_active").HasDefaultValue(true).IsRequired();

    /// <summary>Store-scoped + is_active (svc_professionals, svc_catalog_items). Model unchanged from PR1.</summary>
    public static void ConfigureStoreScopedSvcEntity<T>(this EntityTypeBuilder<T> b, string table)
        where T : StoreEntity
    {
        b.ConfigureKeyTenantAudit(table);
        b.ConfigureStore(table);
        b.ConfigureIsActive();
        b.HasIndex("TenantId", "StoreId", "IsActive").HasDatabaseName($"ix_{table}_tenant_store_active");
    }

    /// <summary>Tenant-scoped + is_active (svc_subjects). Caller adds the customer_id index.</summary>
    public static void ConfigureTenantScopedSvcEntity<T>(this EntityTypeBuilder<T> b, string table)
        where T : TenantEntity
    {
        b.ConfigureKeyTenantAudit(table);
        b.ConfigureIsActive();
    }

    /// <summary>Store-scoped, NO is_active (svc_record_entries — append-only annotations).</summary>
    public static void ConfigureStoreScopedSvcEntityNoActive<T>(this EntityTypeBuilder<T> b, string table)
        where T : StoreEntity
    {
        b.ConfigureKeyTenantAudit(table);
        b.ConfigureStore(table);
    }
}
