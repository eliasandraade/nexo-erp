using Nexo.Application.Common.Interfaces;
using Nexo.Domain.Entities;
using Nexo.Domain.Exceptions;

namespace Nexo.Application.Features.Products;

public class ProductPurchasePriceService
{
    private readonly IProductPurchasePriceRepository _repo;
    private readonly IProductRepository              _products;
    private readonly ICurrentTenant                  _currentTenant;

    public ProductPurchasePriceService(
        IProductPurchasePriceRepository repo,
        IProductRepository products,
        ICurrentTenant currentTenant)
    {
        _repo          = repo;
        _products      = products;
        _currentTenant = currentTenant;
    }

    public async Task<PurchasePriceHistoryDto> GetHistoryAsync(Guid productId, CancellationToken ct = default)
    {
        _ = await _products.GetByIdAsync(productId, ct)
            ?? throw new NotFoundException("Product", productId);

        var entries = await _repo.GetLastFiveAsync(productId, ct);
        if (!entries.Any())
            return new PurchasePriceHistoryDto(null, null, []);

        var avg = Math.Round(entries.Average(e => e.Price), 4);
        return new PurchasePriceHistoryDto(
            LastPrice:    entries[0].Price,
            AveragePrice: avg,
            History:      entries.Select(e => new PurchasePriceEntryDto(e.Id, e.Price, e.PurchasedAt)).ToList());
    }

    public async Task<PurchasePriceEntryDto> AddAsync(Guid productId, AddPurchasePriceRequest request, CancellationToken ct = default)
    {
        _ = await _products.GetByIdAsync(productId, ct)
            ?? throw new NotFoundException("Product", productId);

        var entry = ProductPurchasePrice.Create(_currentTenant.Id, productId, request.Price, request.PurchasedAt);
        await _repo.AddAsync(entry, ct);
        await _repo.SaveChangesAsync(ct);
        return new PurchasePriceEntryDto(entry.Id, entry.Price, entry.PurchasedAt);
    }
}
