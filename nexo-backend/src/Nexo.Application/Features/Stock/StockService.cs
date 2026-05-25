using Nexo.Application.Common.Interfaces;
using Nexo.Domain.Entities;
using Nexo.Domain.Enums;
using Nexo.Domain.Exceptions;

namespace Nexo.Application.Features.Stock;

public class StockService
{
    private readonly IStockRepository _stock;
    private readonly IProductRepository _products;
    private readonly ICurrentTenant _currentTenant;
    private readonly ICurrentUser _currentUser;

    public StockService(
        IStockRepository stock,
        IProductRepository products,
        ICurrentTenant currentTenant,
        ICurrentUser currentUser)
    {
        _stock         = stock;
        _products      = products;
        _currentTenant = currentTenant;
        _currentUser   = currentUser;
    }

    public async Task<IReadOnlyList<StockItemDto>> GetAllAsync(CancellationToken ct = default)
    {
        var items = await _stock.GetAllAsync(ct);
        return items.Select(MapToDto).ToList();
    }

    public Task<StockPagedResponse> GetPagedAsync(
        int page, int pageSize, string? search, string? status,
        CancellationToken ct = default)
        => _stock.GetPagedAsync(page, pageSize, search, status, ct);

    public async Task<StockItemDto> GetByProductIdAsync(Guid productId, CancellationToken ct = default)
    {
        var item = await _stock.GetByProductIdAsync(productId, ct)
            ?? throw new NotFoundException("StockItem", productId);
        return MapToDto(item);
    }

    public async Task<IReadOnlyList<StockMovementDto>> GetMovementsAsync(Guid productId, CancellationToken ct = default)
    {
        var movements = await _stock.GetMovementsByProductAsync(productId, ct);
        return movements.Select(MapMovementToDto).ToList();
    }

    public async Task<StockItemDto> AdjustAsync(AdjustStockRequest request, CancellationToken ct = default)
    {
        var product = await _products.GetByIdAsync(request.ProductId, ct)
            ?? throw new NotFoundException("Product", request.ProductId);

        if (!product.TrackStock)
            throw new DomainException("Stock tracking is disabled for this product.");

        var stockItem = await _stock.GetByProductIdAsync(request.ProductId, ct);
        if (stockItem is null)
        {
            stockItem = StockItem.Create(_currentTenant.Id, request.ProductId);
            await _stock.AddStockItemAsync(stockItem, ct);
        }

        var movementType = Enum.Parse<StockMovementType>(request.MovementType, ignoreCase: true);
        var quantityBefore = stockItem.CurrentQuantity;
        stockItem.ApplyMovement(request.Quantity);

        var movement = StockMovement.Create(
            _currentTenant.Id,
            request.ProductId,
            movementType,
            request.Quantity,
            quantityBefore,
            stockItem.CurrentQuantity,
            _currentUser.UserId,
            notes: request.Notes);

        await _stock.AddMovementAsync(movement, ct);
        await _stock.SaveChangesAsync(ct);
        return MapToDto(stockItem);
    }

    private static StockItemDto MapToDto(StockItem s) => new(
        s.Id,
        s.ProductId,
        s.Product?.Name ?? string.Empty,
        s.Product?.Code ?? string.Empty,
        s.CurrentQuantity,
        s.ReservedQuantity,
        s.AvailableQuantity,
        s.LastMovementAt);

    private static StockMovementDto MapMovementToDto(StockMovement m) => new(
        m.Id, m.ProductId, m.MovementType.ToString(), m.Quantity,
        m.QuantityBefore, m.QuantityAfter, m.ReferenceType, m.ReferenceId,
        m.Notes, m.CreatedByUserId, m.CreatedAt);
}
