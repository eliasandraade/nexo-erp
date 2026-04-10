using Nexo.Application.Common.Interfaces;
using Nexo.Application.Modules.Restaurante.Interfaces;
using Nexo.Domain.Exceptions;
using Nexo.Domain.Modules.Restaurante;

namespace Nexo.Application.Modules.Restaurante;

public class RecipeCardService
{
    private readonly IRecipeCardRepository _recipes;
    private readonly IProductRepository    _products;
    private readonly ICurrentTenant        _currentTenant;

    public RecipeCardService(
        IRecipeCardRepository recipes,
        IProductRepository    products,
        ICurrentTenant        currentTenant)
    {
        _recipes       = recipes;
        _products      = products;
        _currentTenant = currentTenant;
    }

    // ── Queries ───────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<RecipeCardDto>> GetAllAsync(bool includeInactive = false, CancellationToken ct = default)
    {
        var cards = await _recipes.GetAllAsync(includeInactive, ct);
        var result = new List<RecipeCardDto>();
        foreach (var card in cards)
            result.Add(await MapAsync(card, ct));
        return result;
    }

    public async Task<RecipeCardDto> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var card = await _recipes.GetByIdWithIngredientsAsync(id, ct)
            ?? throw new NotFoundException("RecipeCard", id);
        return await MapAsync(card, ct);
    }

    public async Task<RecipeCardDto> GetByProductIdAsync(Guid productId, CancellationToken ct = default)
    {
        var card = await _recipes.GetByProductIdWithIngredientsAsync(productId, ct)
            ?? throw new NotFoundException("RecipeCard for product", productId);
        return await MapAsync(card, ct);
    }

    // ── Commands ──────────────────────────────────────────────────────────────

    public async Task<RecipeCardDto> CreateAsync(CreateRecipeCardRequest request, CancellationToken ct = default)
    {
        _ = await _products.GetByIdAsync(request.ProductId, ct)
            ?? throw new NotFoundException("Product", request.ProductId);

        var existing = await _recipes.GetByProductIdAsync(request.ProductId, ct);
        if (existing is not null)
            throw new ConflictException($"A recipe card already exists for this product.");

        var card = RestRecipeCard.Create(
            _currentTenant.Id, request.ProductId,
            request.Yield, request.YieldUnit, request.Notes);

        await _recipes.AddAsync(card, ct);
        await _recipes.SaveChangesAsync(ct);
        return await MapAsync(card, ct);
    }

    public async Task<RecipeCardDto> UpdateAsync(Guid id, UpdateRecipeCardRequest request, CancellationToken ct = default)
    {
        var card = await _recipes.GetByIdWithIngredientsAsync(id, ct)
            ?? throw new NotFoundException("RecipeCard", id);
        card.Update(request.Yield, request.YieldUnit, request.Notes);
        await _recipes.SaveChangesAsync(ct);
        return await MapAsync(card, ct);
    }

    public async Task<RecipeCardDto> AddIngredientAsync(Guid id, AddIngredientRequest request, CancellationToken ct = default)
    {
        var card = await _recipes.GetByIdWithIngredientsAsync(id, ct)
            ?? throw new NotFoundException("RecipeCard", id);

        _ = await _products.GetByIdAsync(request.IngredientProductId, ct)
            ?? throw new NotFoundException("Ingredient product", request.IngredientProductId);

        var ingredient = card.AddIngredient(
            _currentTenant.Id,
            request.IngredientProductId,
            request.Quantity,
            request.Unit);

        _recipes.TrackIngredient(ingredient);
        await _recipes.SaveChangesAsync(ct);
        return await MapAsync(card, ct);
    }

    public async Task<RecipeCardDto> RemoveIngredientAsync(Guid id, Guid ingredientId, CancellationToken ct = default)
    {
        var card = await _recipes.GetByIdWithIngredientsAsync(id, ct)
            ?? throw new NotFoundException("RecipeCard", id);
        card.RemoveIngredient(ingredientId);
        await _recipes.SaveChangesAsync(ct);
        return await MapAsync(card, ct);
    }

    // ── Mapping + cost calculation ────────────────────────────────────────────

    private async Task<RecipeCardDto> MapAsync(RestRecipeCard card, CancellationToken ct)
    {
        var product = await _products.GetByIdAsync(card.ProductId, ct);
        var ingredientDtos = new List<RecipeIngredientDto>();
        decimal totalIngredientCost = 0m;

        foreach (var ing in card.Ingredients)
        {
            var ingProduct = await _products.GetByIdAsync(ing.IngredientProductId, ct);
            var lineCost   = ing.Quantity * (ingProduct?.CostPrice ?? 0m);
            totalIngredientCost += lineCost;

            ingredientDtos.Add(new RecipeIngredientDto(
                Id:                   ing.Id,
                IngredientProductId:  ing.IngredientProductId,
                IngredientName:       ingProduct?.Name ?? string.Empty,
                IngredientCode:       ingProduct?.Code ?? string.Empty,
                Quantity:             ing.Quantity,
                Unit:                 ing.Unit,
                CurrentCostPrice:     ingProduct?.CostPrice ?? 0m,
                LineCost:             lineCost));
        }

        // custo por unidade vendida = custo total dos ingredientes / rendimento
        var calculatedCost = card.Yield > 0 ? totalIngredientCost / card.Yield : 0m;
        var salePrice      = product?.SalePrice ?? 0m;
        var cmvPercent     = salePrice > 0 ? (calculatedCost / salePrice) * 100m : 0m;

        return new RecipeCardDto(
            Id:             card.Id,
            ProductId:      card.ProductId,
            ProductName:    product?.Name ?? string.Empty,
            ProductCode:    product?.Code ?? string.Empty,
            Yield:          card.Yield,
            YieldUnit:      card.YieldUnit,
            IsActive:       card.IsActive,
            Notes:          card.Notes,
            CalculatedCost: Math.Round(calculatedCost, 4),
            CmvPercent:     Math.Round(cmvPercent, 2),
            Ingredients:    ingredientDtos,
            CreatedAt:      card.CreatedAt);
    }
}
