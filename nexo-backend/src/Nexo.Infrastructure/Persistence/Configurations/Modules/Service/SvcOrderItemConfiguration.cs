using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nexo.Domain.Modules.Service;

namespace Nexo.Infrastructure.Persistence.Configurations.Modules.Service;

public class SvcOrderItemConfiguration : IEntityTypeConfiguration<SvcOrderItem>
{
    public void Configure(EntityTypeBuilder<SvcOrderItem> builder)
    {
        builder.ConfigureStoreScopedSvcEntityNoActive("svc_order_items");

        builder.Property(x => x.OrderId).HasColumnName("order_id").IsRequired();
        builder.Property(x => x.CatalogItemId).HasColumnName("catalog_item_id").IsRequired();
        builder.Property(x => x.ProfessionalId).HasColumnName("professional_id");
        builder.Property(x => x.NameSnapshot).HasColumnName("name_snapshot").HasMaxLength(200).IsRequired();
        builder.Property(x => x.DescriptionSnapshot).HasColumnName("description_snapshot").HasMaxLength(1000);
        builder.Property(x => x.Quantity).HasColumnName("quantity").HasColumnType("numeric(12,3)").IsRequired();
        builder.Property(x => x.UnitPriceSnapshot).HasColumnName("unit_price_snapshot").HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(x => x.CommissionPercentSnapshot).HasColumnName("commission_percent_snapshot").HasColumnType("numeric(5,2)");
        builder.Property(x => x.TotalAmount).HasColumnName("total_amount").HasColumnType("numeric(18,2)").IsRequired();

        builder.HasOne<SvcCatalogItem>().WithMany().HasForeignKey(x => x.CatalogItemId)
            .HasConstraintName("fk_svc_order_items_catalog_items").OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<SvcProfessional>().WithMany().HasForeignKey(x => x.ProfessionalId)
            .HasConstraintName("fk_svc_order_items_professionals").OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.OrderId).HasDatabaseName("ix_svc_order_items_order_id");
    }
}
