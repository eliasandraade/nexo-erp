using Nexo.Application.Common;
using Nexo.Application.Common.Interfaces;
using Nexo.Domain.Entities;
using Nexo.Domain.Enums;
using Nexo.Domain.Exceptions;

namespace Nexo.Application.Features.Products;

public class ProductService
{
    private readonly IProductRepository _products;
    private readonly IStockRepository _stock;
    private readonly ICurrentTenant _currentTenant;

    public ProductService(IProductRepository products, IStockRepository stock, ICurrentTenant currentTenant)
    {
        _products = products;
        _stock    = stock;
        _currentTenant = currentTenant;
    }

    public async Task<IReadOnlyList<ProductDto>> GetAllAsync(
        bool includeInactive = false,
        bool? isIngredient = null,
        CancellationToken ct = default)
    {
        var list = await _products.GetAllAsync(includeInactive, isIngredient, ct);
        return list.Select(MapToDto).ToList();
    }

    public Task<PagedResult<ProductDto>> GetPagedAsync(
        int page, int pageSize,
        string? search, bool includeInactive, bool? isIngredient,
        Guid? categoryId, string? unit,
        CancellationToken ct = default)
        => _products.GetPagedAsync(page, pageSize, search, includeInactive, isIngredient, categoryId, unit, ct);

    public async Task<ProductDto> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var product = await _products.GetByIdAsync(id, ct)
            ?? throw new NotFoundException("Product", id);
        return MapToDto(product);
    }

    public async Task<ProductDto> CreateAsync(CreateProductRequest request, CancellationToken ct = default)
    {
        if (await _products.CodeExistsAsync(request.Code, ct: ct))
            throw new ConflictException($"Product code '{request.Code.ToUpperInvariant()}' is already in use.");

        var unit = Enum.Parse<ProductUnit>(request.Unit, ignoreCase: true);

        var product = Product.Create(
            _currentTenant.Id,
            request.Code,
            request.Name,
            unit,
            request.SalePrice,
            request.CostPrice,
            request.Barcode,
            request.Description,
            request.CategoryId,
            request.TrackStock,
            request.MinStockQuantity,
            request.MaxStockQuantity,
            request.IsIngredient);

        await _products.AddAsync(product, ct);
        await _products.SaveChangesAsync(ct);

        if (product.TrackStock)
        {
            var stockItem = StockItem.Create(_currentTenant.Id, product.Id);
            await _stock.AddStockItemAsync(stockItem, ct);
            await _stock.SaveChangesAsync(ct);
        }

        return MapToDto(product);
    }

    public async Task<ProductDto> UpdateAsync(Guid id, UpdateProductRequest request, CancellationToken ct = default)
    {
        var product = await _products.GetByIdAsync(id, ct)
            ?? throw new NotFoundException("Product", id);

        var unit = Enum.Parse<ProductUnit>(request.Unit, ignoreCase: true);

        product.Update(
            request.Name,
            request.Barcode,
            request.Description,
            request.CategoryId,
            unit,
            request.CostPrice,
            request.SalePrice,
            request.TrackStock,
            request.MinStockQuantity,
            request.MaxStockQuantity);

        product.SetIsIngredient(request.IsIngredient);

        await _products.SaveChangesAsync(ct);
        return MapToDto(product);
    }

    public async Task<ProductDto> UpdatePricesAsync(Guid id, UpdateProductPricesRequest request, CancellationToken ct = default)
    {
        var product = await _products.GetByIdAsync(id, ct)
            ?? throw new NotFoundException("Product", id);

        product.UpdatePrices(request.CostPrice, request.SalePrice);
        await _products.SaveChangesAsync(ct);
        return MapToDto(product);
    }

    public async Task ActivateAsync(Guid id, CancellationToken ct = default)
    {
        var product = await _products.GetByIdAsync(id, ct)
            ?? throw new NotFoundException("Product", id);
        product.Activate();
        await _products.SaveChangesAsync(ct);
    }

    public async Task DeactivateAsync(Guid id, CancellationToken ct = default)
    {
        var product = await _products.GetByIdAsync(id, ct)
            ?? throw new NotFoundException("Product", id);
        product.Deactivate();
        await _products.SaveChangesAsync(ct);
    }

    private static ProductDto MapToDto(Product p) => new(
        p.Id, p.Code, p.Barcode, p.Name, p.Description, p.CategoryId,
        p.Unit.ToString(), p.CostPrice, p.SalePrice, p.TrackStock,
        p.MinStockQuantity, p.MaxStockQuantity, p.IsActive,
        p.IsMenuVisible, p.IsIngredient, p.ImageUrl,
        p.CreatedAt, p.UpdatedAt);
}
