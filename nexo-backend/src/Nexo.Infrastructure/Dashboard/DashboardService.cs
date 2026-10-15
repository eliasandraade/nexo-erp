using Microsoft.EntityFrameworkCore;
using Nexo.Application.Common.Interfaces;
using Nexo.Application.Features.Dashboard;
using Nexo.Application.Features.Reports;
using Nexo.Domain.Enums;
using Nexo.Infrastructure.Persistence;
using Nexo.Infrastructure.Reports;

namespace Nexo.Infrastructure.Dashboard;

public class DashboardService
{
    private readonly NexoDbContext _db;
    private readonly ICurrentTenant _currentTenant;
    private readonly ICurrentStore _currentStore;
    private readonly ICacheService _cache;
    private readonly SalesAnalytics _sales;
    private readonly Nexo.Application.Features.Settings.SettingsService _settings;

    // The summary is built from ~11 sequential DB round-trips. A short Redis TTL
    // collapses that to a single cache GET on warm cache — for the same store,
    // across reloads, devices and users.
    //
    // Staleness budget (be precise — the two layers ADD UP, they don't cap each
    // other): a freshly fetched value can be up to 30s old (this Redis TTL), and
    // the client (TanStack Query staleTime 60s) may then hold that value for up
    // to 60s more before refetching. Worst case a user sees data ~90s old;
    // typical case ≤60s. Acceptable for an at-a-glance dashboard. To tighten,
    // lower this TTL and/or the client staleTime.
    private static readonly TimeSpan _cacheTtl = TimeSpan.FromSeconds(30);

    public DashboardService(
        NexoDbContext db,
        ICurrentTenant currentTenant,
        ICurrentStore currentStore,
        ICacheService cache,
        SalesAnalytics sales,
        Nexo.Application.Features.Settings.SettingsService settings)
    {
        _db            = db;
        _currentTenant = currentTenant;
        _currentStore  = currentStore;
        _cache         = cache;
        _sales         = sales;
        _settings      = settings;
    }

    public async Task<DashboardSummaryDto> GetSummaryAsync(CancellationToken ct = default)
    {
        // Scope the cache to tenant + store so isolation is never crossed; the day in the key makes a
        // new month (and the chart's 30 days) start at midnight in Brazil, not 30 s later by chance.
        var month    = SalesWindow.MonthToDate();
        var cacheKey = $"dashboard:summary:{_currentTenant.Id}:{_currentStore.Id}:{month.To:yyyyMMdd}";

        var cached = await _cache.GetAsync<DashboardSummaryDto>(cacheKey, ct);
        if (cached is not null) return cached;

        var fresh = await ComputeSummaryAsync(month, ct);
        await _cache.SetAsync(cacheKey, fresh, _cacheTtl, ct);
        return fresh;
    }

    /// <summary>
    /// Sales figures are the current month to date (Brazil) — the same definition as the sales
    /// report (<see cref="SalesAnalytics"/>); the chart shows the last 30 days.
    /// </summary>
    private async Task<DashboardSummaryDto> ComputeSummaryAsync(SalesWindow month, CancellationToken ct)
    {
        // Sequential — DbContext is not thread-safe for concurrent queries
        var kpis        = await _sales.KpisAsync(month, ct);
        var topProducts = await _sales.TopProductsAsync(month, 5, ct);
        var topSellers  = await _sales.SellersAsync(month, 4, ct);
        var salesByDay  = await _sales.ByDayAsync(SalesWindow.LastDays(30), ct);
        var (zeroCount, lowCount, alerts) = await GetStockAlertsAsync(ct);
        // Configurações → Estoque: alerts the company turned off are not raised
        var alertRules = await _settings.GetInventoryAsync(ct);
        if (!alertRules.EnableZeroStockAlerts)
        {
            zeroCount = 0;
            alerts = alerts.Where(a => a.Status != "zero").ToList();
        }
        if (!alertRules.EnableLowStockAlerts)
        {
            lowCount = 0;
            alerts = alerts.Where(a => a.Status != "low").ToList();
        }
        var hasOpenCash = await _db.CashSessions.AnyAsync(c => c.Status == CashSessionStatus.Open, ct);

        return new DashboardSummaryDto(
            TotalSales:        kpis.Sales,
            CancelledCount:    kpis.Cancelled,
            TotalRevenue:      kpis.Net,
            AverageTicket:     kpis.AverageTicket,
            TopProducts:       topProducts.Select(p => new TopProductDto(p.ProductId, p.ProductName, p.Quantity, p.Revenue)).ToList(),
            TopSellers:        topSellers.Select(x => new TopSellerDto(x.SellerName, x.Sales, x.Revenue)).ToList(),
            SalesByDay:        salesByDay.Select(d => new SalesByDayDto(d.Date, d.Revenue)).ToList(),
            ZeroStockCount:    zeroCount,
            LowStockCount:     lowCount,
            StockAlerts:       alerts,
            HasOpenCashSession: hasOpenCash,
            PeriodFrom:        month.From.ToString("yyyy-MM-dd"),
            PeriodTo:          month.To.ToString("yyyy-MM-dd"));
    }

    // ── Stock Alerts ──────────────────────────────────────────────────────────

    private async Task<(int zero, int low, IReadOnlyList<StockAlertDto> alerts)>
        GetStockAlertsAsync(CancellationToken ct)
    {
        // Each query uses its own IQueryable — avoid reusing the same variable across
        // multiple CountAsync/ToListAsync calls (EF expression tree reuse can fail).
        // .HasValue/.Value replaced with != null / direct nullable comparison (PostgreSQL-safe).
        // ?? 0m moved to in-memory mapping — null-coalescing inside SQL Select can fail.

        // AvailableQuantity is a computed C# property (CurrentQuantity - ReservedQuantity) — not a DB column.
        // Use the raw columns directly so EF Core can translate the expression to SQL.
        var zero = await _db.StockItems.AsNoTracking()
            .Where(s => s.Product != null && s.Product.IsActive
                     && (s.CurrentQuantity - s.ReservedQuantity) <= 0)
            .CountAsync(ct);

        var low = await _db.StockItems.AsNoTracking()
            .Where(s => s.Product != null && s.Product.IsActive
                     && (s.CurrentQuantity - s.ReservedQuantity) > 0
                     && s.Product.MinStockQuantity != null
                     && (s.CurrentQuantity - s.ReservedQuantity) < s.Product.MinStockQuantity)
            .CountAsync(ct);

        var zeroRows = await _db.StockItems.AsNoTracking()
            .Where(s => s.Product != null && s.Product.IsActive
                     && (s.CurrentQuantity - s.ReservedQuantity) <= 0)
            .OrderBy(s => s.Product!.Name)
            .Take(4)
            .Select(s => new { s.ProductId, Name = s.Product!.Name, s.CurrentQuantity, s.Product.MinStockQuantity })
            .ToListAsync(ct);

        var needed = 4 - zeroRows.Count;
        var lowRows = needed > 0
            ? await _db.StockItems.AsNoTracking()
                .Where(s => s.Product != null && s.Product.IsActive
                         && (s.CurrentQuantity - s.ReservedQuantity) > 0
                         && s.Product.MinStockQuantity != null
                         && (s.CurrentQuantity - s.ReservedQuantity) < s.Product.MinStockQuantity)
                .OrderBy(s => s.Product!.Name)
                .Take(needed)
                .Select(s => new { s.ProductId, Name = s.Product!.Name, s.CurrentQuantity, s.Product.MinStockQuantity })
                .ToListAsync(ct)
            : [];

        var alerts = zeroRows
            .Select(r => new StockAlertDto(r.ProductId.ToString(), r.Name, r.CurrentQuantity, r.MinStockQuantity ?? 0m, "zero"))
            .Concat(lowRows.Select(r => new StockAlertDto(r.ProductId.ToString(), r.Name, r.CurrentQuantity, r.MinStockQuantity ?? 0m, "low")))
            .ToList();

        return (zero, low, alerts);
    }
}
