using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nexo.Domain.Modules.Restaurante;

namespace Nexo.Infrastructure.Persistence.Configurations.Modules.Restaurante;

public class RestRecipeCardConfiguration : IEntityTypeConfiguration<RestRecipeCard>
{
    public void Configure(EntityTypeBuilder<RestRecipeCard> builder)
    {
        builder.ToTable("rest_recipe_cards", "nexo");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.StoreId).HasColumnName("store_id").IsRequired();
        builder.Property(x => x.ProductId).HasColumnName("product_id").IsRequired();
        builder.Property(x => x.Yield).HasColumnName("yield").HasColumnType("numeric(18,4)").IsRequired();
        builder.Property(x => x.YieldUnit).HasColumnName("yield_unit").HasMaxLength(50).IsRequired();
        builder.Property(x => x.Notes).HasColumnName("notes").HasMaxLength(1000);
        builder.Property(x => x.IsActive).HasColumnName("is_active").HasDefaultValue(true).IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamptz").IsRequired();

        builder.HasMany(x => x.Ingredients)
            .WithOne(x => x.RecipeCard)
            .HasForeignKey(x => x.RecipeCardId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Nexo.Domain.Entities.Product>()
            .WithMany()
            .HasForeignKey(x => x.ProductId)
            .HasConstraintName("fk_rest_recipe_cards_products")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Nexo.Domain.Entities.Tenant>()
            .WithMany()
            .HasForeignKey(x => x.TenantId)
            .HasConstraintName("fk_rest_recipe_cards_tenants")
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Nexo.Domain.Entities.Store>()
            .WithMany()
            .HasForeignKey(x => x.StoreId)
            .HasConstraintName("fk_rest_recipe_cards_stores")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.StoreId).HasDatabaseName("ix_rest_recipe_cards_store_id");

        // Uma ficha por produto por tenant + store
        builder.HasIndex(x => new { x.TenantId, x.StoreId, x.ProductId })
            .IsUnique()
            .HasDatabaseName("ix_rest_recipe_cards_tenant_store_product");
    }
}
