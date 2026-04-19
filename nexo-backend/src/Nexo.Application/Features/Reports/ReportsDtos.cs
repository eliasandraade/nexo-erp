namespace Nexo.Application.Features.Reports;

public record SalesReportDto(
    int TotalSales,
    int CancelledSales,
    decimal TotalRevenue,
    decimal AverageTicket,
    decimal CancelledValue,
    string From,
    string To);

public record InventoryReportDto(
    int TotalProducts,
    int ZeroStockCount,
    int LowStockCount,
    int NormalCount,
    decimal TotalStockValue,
    int AlertCount);

public record CustomerReportDto(
    int TotalCustomers,
    int NewThisMonth,
    int WithPurchases,
    decimal AveragePurchaseValue);
