using Nexo.Domain.Common;
using Nexo.Domain.Exceptions;

namespace Nexo.Domain.Modules.Restaurante;

/// <summary>
/// Ingrediente de uma ficha técnica.
/// Quantidade refere-se ao rendimento total da ficha (não por porção).
/// Consumo por item vendido = (orderItem.Qty / recipeCard.Yield) × ingredient.Quantity
/// </summary>
public class RestRecipeIngredient : TenantEntity
{
    private RestRecipeIngredient() { }
    private RestRecipeIngredient(Guid tenantId) : base(tenantId) { }

    public Guid    RecipeCardId         { get; private set; }
    public Guid    IngredientProductId  { get; private set; }
    public decimal Quantity             { get; private set; }  // quantidade para o rendimento total
    public string  Unit                 { get; private set; } = string.Empty;

    // Navigation
    public RestRecipeCard? RecipeCard { get; private set; }

    public static RestRecipeIngredient Create(
        Guid tenantId, Guid recipeCardId, Guid ingredientProductId,
        decimal quantity, string unit)
    {
        if (quantity <= 0)
            throw new DomainException("Ingredient quantity must be greater than zero.");

        return new RestRecipeIngredient(tenantId)
        {
            RecipeCardId        = recipeCardId,
            IngredientProductId = ingredientProductId,
            Quantity            = quantity,
            Unit                = unit.Trim(),
        };
    }

    public void Update(decimal quantity, string unit)
    {
        if (quantity <= 0)
            throw new DomainException("Ingredient quantity must be greater than zero.");
        Quantity = quantity;
        Unit     = unit.Trim();
        SetUpdatedAt();
    }
}
