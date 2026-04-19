using Microsoft.EntityFrameworkCore;
using Nexo.Application.Common.Interfaces;
using Nexo.Application.Features.Reports;
using Nexo.Domain.Enums;
using Nexo.Infrastructure.Persistence;

namespace Nexo.Infrastructure.Reports;

public class ReportsService
{
    private readonly NexoDbContext _db;
    private readonly ICurrentTenant _currentTenant;

    public ReportsService(NexoDbContext db, ICurrentTenant currentTenant)
    {
        _db            = db;
        _currentTenant = currentTenant;
    }

    public async Task<SalesReportDto> GetSalesReportAsync(
        DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var fromUtc = from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var toUtc   = to.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);

        var sales = await _db.Sales
            .Where(s => s.CreatedAt >= fromUtc && s.CreatedAt <= toUtc)
            .AsNoTracking()
            .Select(s => new { s.Total, s.Status })
            .ToListAsync(ct);

        var active    = sales.Where(s => s.Status != SaleStatus.Cancelled).ToList();
        var cancelled = sales.Where(s => s.Status == SaleStatus.Cancelled).ToList();
        var revenue   = active.Sum(s => s.Total);

        return new SalesReportDto(
            TotalSales:     sales.Count,
            CancelledSales: cancelled.Count,
            TotalRevenue:   Math.Round(revenue, 2),
            AverageTicket:  active.Count > 0 ? Math.Round(revenue / active.Count, 2) : 0,
            CancelledValue: Math.Round(cancelled.Sum(s => s.Total), 2),
            From:           from.ToString("yyyy-MM-dd"),
            To:             to.ToString("yyyy-MM-dd"));
    }

    public async Task<InventoryReportDto> GetInventoryReportAsync(CancellationToken ct = default)
    {
        var items = await _db.StockItems
            .Include(s => s.Product)
            .Where(s => s.Product != null && s.Product.IsActive)
            .AsNoTracking()
            .Select(s => new
            {
                s.CurrentQuantity,
                MinStock  = s.Product!.MinStockQuantity,
                CostValue = s.CurrentQuantity * s.Product!.CostPrice
            })
            .ToListAsync(ct);

        var zero   = items.Count(i => i.CurrentQuantity == 0);
        var low    = items.Count(i => i.CurrentQuantity > 0
                                   && i.MinStock.HasValue
                                   && i.CurrentQuantity < i.MinStock.Value);
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

    public async Task<CustomerReportDto> GetCustomerReportAsync(CancellationToken ct = default)
    {
        var now        = DateTime.UtcNow;
        var monthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        var total = await _db.Customers
            .CountAsync(c => c.IsActive, ct);

        var newThisMonth = await _db.Customers
            .CountAsync(c => c.IsActive && c.CreatedAt >= monthStart, ct);

        var withPurchases = await _db.Sales
            .Where(s => s.Status == SaleStatus.Paid && s.CustomerId != null)
            .Select(s => s.CustomerId)
            .Distinct()
            .CountAsync(ct);

        var avgPurchase = await _db.Sales
            .Where(s => s.Status == SaleStatus.Paid && s.CustomerId != null)
            .Select(s => (decimal?)s.Total)
            .AverageAsync(ct) ?? 0m;

        return new CustomerReportDto(
            TotalCustomers:       total,
            NewThisMonth:         newThisMonth,
            WithPurchases:        withPurchases,
            AveragePurchaseValue: Math.Round(avgPurchase, 2));
    }
}
