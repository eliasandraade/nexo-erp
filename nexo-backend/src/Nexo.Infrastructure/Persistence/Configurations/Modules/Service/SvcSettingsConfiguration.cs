using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nexo.Domain.Modules.Service;

namespace Nexo.Infrastructure.Persistence.Configurations.Modules.Service;

/// <summary>
/// EF mapping for svc_settings — the per-store Service configuration (chosen internal preset).
/// Store-scoped, no is_active. Unique on (tenant_id, store_id): one settings row per store.
/// </summary>
internal sealed class SvcSettingsConfiguration : IEntityTypeConfiguration<SvcSettings>
{
    public void Configure(EntityTypeBuilder<SvcSettings> b)
    {
        b.ConfigureStoreScopedSvcEntityNoActive("svc_settings");

        b.Property(x => x.PresetKey).HasColumnName("preset_key").HasMaxLength(50).IsRequired();

        b.HasIndex("TenantId", "StoreId")
            .IsUnique()
            .HasDatabaseName("ix_svc_settings_tenant_store_unique");
    }
}
