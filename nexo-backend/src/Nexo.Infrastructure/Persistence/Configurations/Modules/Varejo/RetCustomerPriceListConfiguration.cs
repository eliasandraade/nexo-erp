using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nexo.Domain.Modules.Varejo;

namespace Nexo.Infrastructure.Persistence.Configurations.Modules.Varejo;

public class RetCustomerPriceListConfiguration : IEntityTypeConfiguration<RetCustomerPriceList>
{
    public void Configure(EntityTypeBuilder<RetCustomerPriceList> builder)
    {
        builder.ToTable("ret_customer_price_lists", "nexo");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");

        builder.Property(x => x.TenantId)
            .HasColumnName("tenant_id")
            .IsRequired();

        builder.Property(x => x.CustomerId)
            .HasColumnName("customer_id")
            .IsRequired();

        builder.Property(x => x.PriceListId)
            .HasColumnName("price_list_id")
            .IsRequired();

        builder.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamptz")
            .IsRequired();

        builder.Property(x => x.UpdatedAt)
            .HasColumnName("updated_at")
            .HasColumnType("timestamptz")
            .IsRequired();

        // Um cliente: no máximo uma lista de preço vinculada
        builder.HasIndex(x => new { x.TenantId, x.CustomerId })
            .IsUnique()
            .HasDatabaseName("ix_ret_customer_price_lists_tenant_customer");

        builder.HasOne<Nexo.Domain.Entities.Customer>()
            .WithMany()
            .HasForeignKey(x => x.CustomerId)
            .HasConstraintName("fk_ret_customer_price_lists_customers")
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.PriceList)
            .WithMany()
            .HasForeignKey(x => x.PriceListId)
            .HasConstraintName("fk_ret_customer_price_lists_price_lists")
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Nexo.Domain.Entities.Tenant>()
            .WithMany()
            .HasForeignKey(x => x.TenantId)
            .HasConstraintName("fk_ret_customer_price_lists_tenants")
            .OnDelete(DeleteBehavior.Cascade);
    }
}
