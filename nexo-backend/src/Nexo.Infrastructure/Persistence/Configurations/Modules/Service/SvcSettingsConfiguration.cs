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

        // ── Public booking portal (PR12) ─────────────────────────────────────────
        b.Property(x => x.PublicBookingEnabled)
            .HasColumnName("public_booking_enabled").HasDefaultValue(false).IsRequired();
        b.Property(x => x.BookingDaysAhead)
            .HasColumnName("booking_days_ahead").HasDefaultValue(14).IsRequired();
        b.Property(x => x.MinLeadMinutes)
            .HasColumnName("min_lead_minutes").HasDefaultValue(120).IsRequired();
        b.Property(x => x.SlotIntervalMinutes)
            .HasColumnName("slot_interval_minutes").HasDefaultValue(30).IsRequired();
        b.Property(x => x.ShowPrices)
            .HasColumnName("show_prices").HasDefaultValue(true).IsRequired();
        b.Property(x => x.AutoConfirmAppointments)
            .HasColumnName("auto_confirm_appointments").HasDefaultValue(false).IsRequired();
        b.Property(x => x.TimeZoneId)
            .HasColumnName("time_zone_id").HasMaxLength(64).HasDefaultValue("America/Sao_Paulo").IsRequired();

        b.HasIndex("TenantId", "StoreId")
            .IsUnique()
            .HasDatabaseName("ix_svc_settings_tenant_store_unique");
    }
}
