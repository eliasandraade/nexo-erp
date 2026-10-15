namespace Nexo.Application.Features.Reports;

/// <summary>
/// Sales of a period (Brazil days, by the day the sale was confirmed). Realized = confirmed, paid or
/// partially returned; fully returned sales count as returns, cancelled ones apart. Returns are
/// subtracted from the sale they belong to, so <c>NetRevenue = GrossRevenue - ReturnedValue</c>.
/// </summary>
public record SalesReportDto(
    int TotalSales,
    int CancelledSales,
    decimal TotalRevenue,
    decimal AverageTicket,
    decimal CancelledValue,
    string From,
    string To,
    decimal GrossRevenue,
    decimal ReturnedValue,
    int ReturnedSales,
    decimal DiscountValue,
    IReadOnlyList<SalesReportDayDto> Days,
    IReadOnlyList<SalesReportProductDto> TopProducts,
    IReadOnlyList<SalesReportSellerDto> Sellers,
    IReadOnlyList<SalesReportPaymentDto> Payments);

public record SalesReportDayDto(string Date, int Sales, decimal Revenue);

public record SalesReportProductDto(string ProductId, string ProductName, decimal Quantity, decimal Revenue);

public record SalesReportSellerDto(string SellerName, int Sales, decimal Revenue);

/// <summary>What the sales of the period were paid with (value applied to the sale, before returns).</summary>
public record SalesReportPaymentDto(string Method, int Sales, decimal Amount);

/// <summary>Sales KPIs of a period — shared by the dashboard and the sales report.</summary>
public record SalesKpis(
    int Sales,
    int Cancelled,
    int Returned,
    decimal Gross,
    decimal ReturnedValue,
    decimal Net,
    decimal AverageTicket,
    decimal CancelledValue,
    decimal Discounts);

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
