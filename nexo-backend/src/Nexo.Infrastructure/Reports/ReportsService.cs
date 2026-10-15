using Microsoft.EntityFrameworkCore;
using Nexo.Application.Features.Reports;
using Nexo.Domain.Enums;
using Nexo.Infrastructure.Persistence;

namespace Nexo.Infrastructure.Reports;

public class ReportsService
{
    private readonly NexoDbContext _db;
    private readonly SalesAnalytics _sales;

    public ReportsService(NexoDbContext db, SalesAnalytics sales)
    {
        _db    = db;
        _sales = sales;
    }

    public async Task<SalesReportDto> GetSalesReportAsync(SalesWindow window, CancellationToken ct = default)
    {
        // Sequential — DbContext is not thread-safe for concurrent queries
        var k        = await _sales.KpisAsync(window, ct);
        var days     = await _sales.ByDayAsync(window, ct);
        var products = await _sales.TopProductsAsync(window, 10, ct);
        var sellers  = await _sales.SellersAsync(window, 10, ct);
        var payments = await _sales.PaymentsAsync(window, ct);

        return new SalesReportDto(
            TotalSales:     k.Sales,
            CancelledSales: k.Cancelled,
            TotalRevenue:   k.Net,
            AverageTicket:  k.AverageTicket,
            CancelledValue: k.CancelledValue,
            From:           window.From.ToString("yyyy-MM-dd"),
            To:             window.To.ToString("yyyy-MM-dd"),
            GrossRevenue:   k.Gross,
            ReturnedValue:  k.ReturnedValue,
            ReturnedSales:  k.Returned,
            DiscountValue:  k.Discounts,
            Days:           days,
            TopProducts:    products,
            Sellers:        sellers,
            Payments:       payments);
    }

    public Task ExportSalesAsync(SalesWindow window, Stream output, CancellationToken ct = default)
        => SalesCsv.WriteAsync(output, _sales.ExportRows(window), ct);

    /// <summary>
    /// Stock health of the current store. Alerts use the quantity available to sell (on hand minus
    /// reserved) — the same rule as the dashboard; the value is what is on hand, at cost.
    /// </summary>
    public async Task<InventoryReportDto> GetInventoryReportAsync(CancellationToken ct = default)
    {
        var items = await _db.StockItems
            .Where(s => s.Product != null && s.Product.IsActive)
            .AsNoTracking()
            .Select(s => new
            {
                Available = s.CurrentQuantity - s.ReservedQuantity,
                MinStock  = s.Product!.MinStockQuantity,
                CostValue = s.CurrentQuantity * s.Product!.CostPrice
            })
            .ToListAsync(ct);

        var zero   = items.Count(i => i.Available <= 0);
        var low    = items.Count(i => i.Available > 0
                                   && i.MinStock.HasValue
                                   && i.Available < i.MinStock.Value);
        var normal = items.Count - zero - low;
        var total  = items.Sum(i => i.CostValue);

        return new InventoryReportDto(
            TotalProducts:   items.Count,
            ZeroStockCount:  zero,
            LowStockCount:   low,
            NormalCount:     normal,
            TotalStockValue: Math.Round(total, 2),
            AlertCount:      zero + low);
    }

    /// <summary>
    /// Customers are shared by every store of the company; who bought (and how much) is this store's
    /// realized sales. "New this month" follows the Brazil calendar.
    /// </summary>
    public async Task<CustomerReportDto> GetCustomerReportAsync(CancellationToken ct = default)
    {
        var monthStart = SalesWindow.MonthToDate().FromUtc;

        var total = await _db.Customers
            .CountAsync(c => c.IsActive, ct);

        var newThisMonth = await _db.Customers
            .CountAsync(c => c.IsActive && c.CreatedAt >= monthStart, ct);

        var realized = _db.Sales.AsNoTracking()
            .Where(s => (s.Status == SaleStatus.Confirmed || s.Status == SaleStatus.Paid || s.Status == SaleStatus.PartiallyReturned)
                     && s.CustomerId != null);

        var withPurchases = await realized
            .Select(s => s.CustomerId)
            .Distinct()
            .CountAsync(ct);

        var avgPurchase = await realized
            .Select(s => (decimal?)(s.Total - s.ReturnedAmount))
            .AverageAsync(ct) ?? 0m;

        return new CustomerReportDto(
            TotalCustomers:       total,
            NewThisMonth:         newThisMonth,
            WithPurchases:        withPurchases,
            AveragePurchaseValue: Math.Round(avgPurchase, 2));
    }
}
