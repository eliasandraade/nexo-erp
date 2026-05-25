using Microsoft.EntityFrameworkCore;
using Nexo.Application.Common.Interfaces;
using Nexo.Application.Features.Dashboard;
using Nexo.Domain.Enums;
using Nexo.Infrastructure.Persistence;

namespace Nexo.Infrastructure.Dashboard;

public class DashboardService
{
    private readonly NexoDbContext _db;
    private readonly ICurrentTenant _currentTenant;

    public DashboardService(NexoDbContext db, ICurrentTenant currentTenant)
    {
        _db            = db;
        _currentTenant = currentTenant;
    }

    public async Task<DashboardSummaryDto> GetSummaryAsync(CancellationToken ct = default)
    {
        // Sequential — DbContext is not thread-safe for concurrent queries
        var (totalSales, cancelledCount, totalRevenue, averageTicket) = await GetSalesKpisAsync(ct);
        var topProducts    = await GetTopProductsAsync(ct);
        var topSellers     = await GetTopSellersAsync(ct);
        var salesByDay     = await GetSalesByDayAsync(ct);
        var (zeroCount, lowCount, alerts) = await GetStockAlertsAsync(ct);
        var hasOpenCash    = await _db.CashSessions.AnyAsync(c => c.Status == CashSessionStatus.Open, ct);

        return new DashboardSummaryDto(
            TotalSales:        totalSales,
            CancelledCount:    cancelledCount,
            TotalRevenue:      totalRevenue,
            AverageTicket:     averageTicket,
            TopProducts:       topProducts,
            TopSellers:        topSellers,
            SalesByDay:        salesByDay,
            ZeroStockCount:    zeroCount,
            LowStockCount:     lowCount,
            StockAlerts:       alerts,
            HasOpenCashSession: hasOpenCash);
    }

    // ── Sales KPIs ────────────────────────────────────────────────────────────

    private async Task<(int total, int cancelled, decimal revenue, decimal avgTicket)>
        GetSalesKpisAsync(CancellationToken ct)
    {
        var rows = await _db.Sales
            .AsNoTracking()
            .Select(s => new { s.Total, s.Status })
            .ToListAsync(ct);

        var active    = rows.Where(s => s.Status != SaleStatus.Cancelled).ToList();
        var cancelled = rows.Count(s => s.Status == SaleStatus.Cancelled);
        var revenue   = active.Sum(s => s.Total);
        var avg       = active.Count > 0 ? Math.Round(revenue / active.Count, 2) : 0m;

        return (rows.Count, cancelled, Math.Round(revenue, 2), avg);
    }

    // ── Top Products ──────────────────────────────────────────────────────────

    private async Task<IReadOnlyList<TopProductDto>> GetTopProductsAsync(CancellationToken ct)
    {
        // Project to anonymous type so EF can translate; map to DTO in memory.
        // Guid.ToString() and Math.Round can't be translated inside GroupBy Select.
        var rows = await _db.SaleItems
            .AsNoTracking()
            .Where(i => i.Sale!.Status != SaleStatus.Cancelled)
            .GroupBy(i => new { i.ProductId, Name = i.Product!.Name })
            .Select(g => new
            {
                g.Key.ProductId,
                g.Key.Name,
                Quantity = g.Sum(i => i.Quantity),
                Revenue  = g.Sum(i => i.Total),
            })
            .OrderByDescending(g => g.Revenue)
            .Take(5)
            .ToListAsync(ct);

        return rows
            .Select(r => new TopProductDto(
                r.ProductId.ToString(),
                r.Name,
                r.Quantity,
                Math.Round(r.Revenue, 2)))
            .ToList();
    }

    // ── Top Sellers ───────────────────────────────────────────────────────────

    private async Task<IReadOnlyList<TopSellerDto>> GetTopSellersAsync(CancellationToken ct)
    {
        // Project to anonymous type; OrderByDescending on DTO constructor property
        // can't be translated by EF Core. Map to DTO in memory after query.
        var rows = await _db.Sales
            .AsNoTracking()
            .Where(s => s.Status != SaleStatus.Cancelled)
            .Join(_db.Users,
                  s => s.SoldByUserId,
                  u => u.Id,
                  (s, u) => new { u.FullName, s.Total })
            .GroupBy(x => x.FullName)
            .Select(g => new
            {
                Name    = g.Key,
                Count   = g.Count(),
                Revenue = g.Sum(x => x.Total),
            })
            .OrderByDescending(g => g.Revenue)
            .Take(4)
            .ToListAsync(ct);

        return rows
            .Select(r => new TopSellerDto(r.Name, r.Count, Math.Round(r.Revenue, 2)))
            .ToList();
    }

    // ── Sales by Day (last 30 days) ────────────────────────────────────────────

    private async Task<IReadOnlyList<SalesByDayDto>> GetSalesByDayAsync(CancellationToken ct)
    {
        var cutoff = DateTime.UtcNow.AddDays(-30);

        var rows = await _db.Sales
            .AsNoTracking()
            .Where(s => s.Status != SaleStatus.Cancelled
                     && (s.ConfirmedAt ?? s.CreatedAt) >= cutoff)
            .Select(s => new
            {
                Date  = (s.ConfirmedAt ?? s.CreatedAt).Date,
                s.Total,
            })
            .ToListAsync(ct);

        return rows
            .GroupBy(r => r.Date)
            .Select(g => new SalesByDayDto(
                g.Key.ToString("yyyy-MM-dd"),
                Math.Round(g.Sum(r => r.Total), 2)))
            .OrderBy(x => x.Date)
            .ToList();
    }

    // ── Stock Alerts ──────────────────────────────────────────────────────────

    private async Task<(int zero, int low, IReadOnlyList<StockAlertDto> alerts)>
        GetStockAlertsAsync(CancellationToken ct)
    {
        var items = await _db.StockItems
            .AsNoTracking()
            .Where(s => s.Product != null && s.Product.IsActive)
            .Select(s => new
            {
                s.ProductId,
                ProductName      = s.Product!.Name,
                s.CurrentQuantity,
                s.AvailableQuantity,
                MinStock         = s.Product!.MinStockQuantity,
            })
            .ToListAsync(ct);

        var alertItems = items
            .Select(s =>
            {
                var status = s.AvailableQuantity <= 0
                    ? "zero"
                    : s.MinStock.HasValue && s.AvailableQuantity < s.MinStock.Value
                        ? "low"
                        : "ok";
                return (s.ProductId, s.ProductName, s.CurrentQuantity, MinStock: s.MinStock ?? 0m, status);
            })
            .Where(s => s.status != "ok")
            .ToList();

        var zero   = alertItems.Count(s => s.status == "zero");
        var low    = alertItems.Count(s => s.status == "low");
        var alerts = alertItems
            .OrderByDescending(s => s.status == "zero")
            .Take(4)
            .Select(s => new StockAlertDto(
                s.ProductId.ToString(),
                s.ProductName,
                s.CurrentQuantity,
                s.MinStock,
                s.status))
            .ToList();

        return (zero, low, alerts);
    }
}
