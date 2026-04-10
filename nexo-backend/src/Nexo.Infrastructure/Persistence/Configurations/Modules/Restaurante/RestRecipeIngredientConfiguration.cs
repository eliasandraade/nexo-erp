using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nexo.Domain.Modules.Restaurante;

namespace Nexo.Infrastructure.Persistence.Configurations.Modules.Restaurante;

public class RestRecipeIngredientConfiguration : IEntityTypeConfiguration<RestRecipeIngredient>
{
    public void Configure(EntityTypeBuilder<RestRecipeIngredient> builder)
    {
        builder.ToTable("rest_recipe_ingredients", "nexo");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.RecipeCardId).HasColumnName("recipe_card_id").IsRequired();
        builder.Property(x => x.IngredientProductId).HasColumnName("ingredient_product_id").IsRequired();
        builder.Property(x => x.Quantity).HasColumnName("quantity").HasColumnType("numeric(18,4)").IsRequired();
        builder.Property(x => x.Unit).HasColumnName("unit").HasMaxLength(50).IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamptz").IsRequired();

        // Um ingrediente aparece no máximo uma vez por ficha
        builder.HasIndex(x => new { x.RecipeCardId, x.IngredientProductId })
            .IsUnique()
            .HasDatabaseName("ix_rest_recipe_ingredients_card_product");

        builder.HasIndex(x => new { x.TenantId, x.RecipeCardId })
            .HasDatabaseName("ix_rest_recipe_ingredients_tenant_card");

        builder.HasOne<Nexo.Domain.Entities.Product>()
            .WithMany()
            .HasForeignKey(x => x.IngredientProductId)
            .HasConstraintName("fk_rest_recipe_ingredients_products")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Nexo.Domain.Entities.Tenant>()
            .WithMany()
            .HasForeignKey(x => x.TenantId)
            .HasConstraintName("fk_rest_recipe_ingredients_tenants")
            .OnDelete(DeleteBehavior.Cascade);
    }
}
