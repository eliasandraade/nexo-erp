using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nexo.Domain.Modules.Restaurante;

namespace Nexo.Infrastructure.Persistence.Configurations.Modules.Restaurante;

public class FoodServiceSettingsConfiguration : IEntityTypeConfiguration<FoodServiceSettings>
{
    public void Configure(EntityTypeBuilder<FoodServiceSettings> builder)
    {
        builder.ToTable("food_service_settings", "nexo");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.StoreId).HasColumnName("store_id").IsRequired();
        builder.Property(x => x.StoreType).HasColumnName("store_type").HasMaxLength(20)
            .HasDefaultValue("restaurant").IsRequired();
        builder.Property(x => x.CouvertEnabled).HasColumnName("couvert_enabled")
            .HasDefaultValue(false).IsRequired();
        builder.Property(x => x.CouvertPricePerPerson).HasColumnName("couvert_price_per_person")
            .HasColumnType("numeric(18,2)");
        builder.Property(x => x.CouvertAutomatic).HasColumnName("couvert_automatic")
            .HasDefaultValue(false).IsRequired();
        builder.Property(x => x.ServiceFeeEnabled).HasColumnName("service_fee_enabled")
            .HasDefaultValue(false).IsRequired();
        builder.Property(x => x.ServiceFeePercent).HasColumnName("service_fee_percent")
            .HasColumnType("numeric(5,2)");
        builder.Property(x => x.OrderTypesEnabled).HasColumnName("order_types_enabled")
            .HasMaxLength(100).HasDefaultValue("DineIn,Counter,Takeaway").IsRequired();

        builder.Property(x => x.DisplayName).HasColumnName("display_name").HasMaxLength(200);
        builder.Property(x => x.LogoUrl).HasColumnName("logo_url").HasMaxLength(2000);
        builder.Property(x => x.CoverImageUrl).HasColumnName("cover_image_url").HasMaxLength(2000);
        builder.Property(x => x.Description).HasColumnName("description").HasMaxLength(1000);
        builder.Property(x => x.WhatsAppPhone).HasColumnName("whatsapp_phone").HasMaxLength(30);
        builder.Property(x => x.BusinessHoursJson).HasColumnName("business_hours_json")
            .HasColumnType("jsonb");

        builder.Property(x => x.CostPerMinuteGas)
            .HasColumnName("cost_per_minute_gas")
            .HasColumnType("numeric(18,4)").HasDefaultValue(0m).IsRequired();
        builder.Property(x => x.CostPerMinuteLaborRate)
            .HasColumnName("cost_per_minute_labor")
            .HasColumnType("numeric(18,4)").HasDefaultValue(0m).IsRequired();

        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamptz").IsRequired();

        builder.HasOne<Nexo.Domain.Entities.Tenant>()
            .WithMany()
            .HasForeignKey(x => x.TenantId)
            .HasConstraintName("fk_food_service_settings_tenants")
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Nexo.Domain.Entities.Store>()
            .WithMany()
            .HasForeignKey(x => x.StoreId)
            .HasConstraintName("fk_food_service_settings_stores")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.TenantId).HasDatabaseName("ix_food_service_settings_tenant_id");
        builder.HasIndex(x => x.StoreId).HasDatabaseName("ix_food_service_settings_store_id");

        // One settings record per store
        builder.HasIndex(x => new { x.TenantId, x.StoreId })
            .IsUnique()
            .HasDatabaseName("ix_food_service_settings_tenant_store");
    }
}
