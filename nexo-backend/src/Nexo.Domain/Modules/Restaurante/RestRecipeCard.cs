using Nexo.Domain.Common;
using Nexo.Domain.Exceptions;

namespace Nexo.Domain.Modules.Restaurante;

/// <summary>
/// Ficha técnica de um produto vendável.
/// Uma ficha por (tenant_id, product_id) — garantido por índice UNIQUE.
///
/// Custo calculado:
///   custo_total = Σ(ingredient.Qty × ingredient.Product.CostPrice)
///   custo_unitário = custo_total / Yield
///
/// CMV%:
///   cmv = (custo_unitário / product.SalePrice) × 100
///
/// Cálculo é feito no RecipeCardService — entidade só guarda os dados.
/// </summary>
public class RestRecipeCard : TenantEntity
{
    private RestRecipeCard() { }
    private RestRecipeCard(Guid tenantId) : base(tenantId) { }

    public Guid    ProductId  { get; private set; }
    public decimal Yield      { get; private set; }    // rendimento (ex: 10 porções)
    public string  YieldUnit  { get; private set; } = string.Empty;  // ex: "porções", "kg"
    public string? Notes      { get; private set; }
    public bool    IsActive   { get; private set; }

    // Navigation
    private readonly List<RestRecipeIngredient> _ingredients = [];
    public IReadOnlyList<RestRecipeIngredient> Ingredients => _ingredients.AsReadOnly();

    // ── Factory ───────────────────────────────────────────────────────────────

    public static RestRecipeCard Create(
        Guid tenantId, Guid productId,
        decimal yield, string yieldUnit, string? notes = null)
    {
        if (yield <= 0)
            throw new DomainException("Recipe yield must be greater than zero.");

        return new RestRecipeCard(tenantId)
        {
            ProductId = productId,
            Yield     = yield,
            YieldUnit = yieldUnit.Trim(),
            Notes     = notes?.Trim(),
            IsActive  = true,
        };
    }

    public void Update(decimal yield, string yieldUnit, string? notes)
    {
        if (yield <= 0)
            throw new DomainException("Recipe yield must be greater than zero.");
        Yield     = yield;
        YieldUnit = yieldUnit.Trim();
        Notes     = notes?.Trim();
        SetUpdatedAt();
    }

    public void Activate()   { IsActive = true;  SetUpdatedAt(); }
    public void Deactivate() { IsActive = false; SetUpdatedAt(); }

    // ── Ingredients ───────────────────────────────────────────────────────────

    public RestRecipeIngredient AddIngredient(
        Guid tenantId, Guid ingredientProductId, decimal quantity, string unit)
    {
        if (_ingredients.Any(i => i.IngredientProductId == ingredientProductId))
            throw new DomainException("This ingredient is already in the recipe. Update it instead.");

        var ingredient = RestRecipeIngredient.Create(
            tenantId, Id, ingredientProductId, quantity, unit);
        _ingredients.Add(ingredient);
        SetUpdatedAt();
        return ingredient;
    }

    public void RemoveIngredient(Guid ingredientId)
    {
        var ingredient = _ingredients.FirstOrDefault(i => i.Id == ingredientId)
            ?? throw new NotFoundException("RecipeIngredient", ingredientId);
        _ingredients.Remove(ingredient);
        SetUpdatedAt();
    }
}
