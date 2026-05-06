namespace Nexo.Application.Features.Products;

public record AddPurchasePriceRequest(decimal Price, DateOnly PurchasedAt);

public record PurchasePriceEntryDto(Guid Id, decimal Price, DateOnly PurchasedAt);

public record PurchasePriceHistoryDto(
    decimal? LastPrice,
    decimal? AveragePrice,
    IReadOnlyList<PurchasePriceEntryDto> History);
