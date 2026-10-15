namespace Nexo.Application.Features.Dashboard;

public record DashboardSummaryDto(
    // Sales KPIs — current month to date (Brazil days): realized sales, net of returns
    int     TotalSales,
    int     CancelledCount,
    decimal TotalRevenue,
    decimal AverageTicket,

    // Top performers (aggregated server-side — no raw list sent to client)
    IReadOnlyList<TopProductDto> TopProducts,
    IReadOnlyList<TopSellerDto>  TopSellers,

    // Chart: daily revenue for last 30 days
    IReadOnlyList<SalesByDayDto> SalesByDay,

    // Stock
    int ZeroStockCount,
    int LowStockCount,
    IReadOnlyList<StockAlertDto> StockAlerts,

    // Cash
    bool HasOpenCashSession,

    // The period of the sales figures (yyyy-MM-dd, Brazil): the 1st of the month to today
    string PeriodFrom,
    string PeriodTo
);

public record TopProductDto(
    string  ProductId,
    string  ProductName,
    decimal QuantitySold,
    decimal Revenue);

public record TopSellerDto(
    string  SellerName,
    int     SalesCount,
    decimal Revenue);

public record SalesByDayDto(
    string  Date,     // yyyy-MM-dd
    decimal Revenue);

public record StockAlertDto(
    string  ProductId,
    string  ProductName,
    decimal CurrentStock,
    decimal MinStock,
    string  Status);  // "low" | "zero"
