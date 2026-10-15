using Microsoft.EntityFrameworkCore;
using Nexo.Application.Common;
using Nexo.Application.Features.Reports;
using Nexo.Domain.Entities;
using Nexo.Domain.Enums;
using Nexo.Infrastructure.Persistence;

namespace Nexo.Infrastructure.Reports;

/// <summary>
/// The single definition of "sales of a period", used by the dashboard and the sales report so the two
/// never disagree. A sale belongs to the Brazil day it was confirmed (created, if never confirmed).
/// Booked = confirmed, paid, partially or fully returned (drafts are abandoned checkouts; cancelled
/// sales are counted apart — only those that had been confirmed; a cancelled draft is an abandoned
/// checkout). Realized = booked minus the fully returned. Returns are subtracted from the sale they
/// belong to, in the sale's period: a closed month changes if one of its sales is returned later
/// (the ledger books the refund on the refund day instead). Store scoping comes from the global
/// query filters.
/// </summary>
public class SalesAnalytics
{
    private readonly NexoDbContext _db;

    public SalesAnalytics(NexoDbContext db) => _db = db;

    private IQueryable<Sale> InWindow(SalesWindow w)
    {
        var from = w.FromUtc;
        var to = w.ToUtcExclusive;
        return _db.Sales.AsNoTracking().Where(s => (s.ConfirmedAt ?? s.CreatedAt) >= from && (s.ConfirmedAt ?? s.CreatedAt) < to);
    }

    private IQueryable<Sale> Booked(SalesWindow w) => InWindow(w).Where(s =>
        s.Status == SaleStatus.Confirmed || s.Status == SaleStatus.Paid ||
        s.Status == SaleStatus.PartiallyReturned || s.Status == SaleStatus.Returned);

    public async Task<SalesKpis> KpisAsync(SalesWindow w, CancellationToken ct)
    {
        var byStatus = await InWindow(w)
            .Where(s => s.Status != SaleStatus.Draft && (s.Status != SaleStatus.Cancelled || s.ConfirmedAt != null))
            .GroupBy(s => s.Status)
            .Select(g => new
            {
                Status = g.Key,
                Count = g.Count(),
                Total = g.Sum(s => s.Total),
                Returned = g.Sum(s => s.ReturnedAmount),
                Discount = g.Sum(s => s.DiscountAmount),
            })
            .ToListAsync(ct);
        var itemDiscounts = await _db.SaleItems.AsNoTracking()
            .Join(Booked(w), i => i.SaleId, s => s.Id, (i, _) => i.DiscountAmount)
            .SumAsync(d => (decimal?)d, ct) ?? 0m;

        var booked = byStatus.Where(x => x.Status != SaleStatus.Cancelled).ToList();
        var realized = booked.Where(x => x.Status != SaleStatus.Returned).Sum(x => x.Count);
        var gross = booked.Sum(x => x.Total);
        var returned = booked.Sum(x => x.Returned);
        var net = gross - returned;
        var cancelled = byStatus.Where(x => x.Status == SaleStatus.Cancelled).ToList();

        return new SalesKpis(
            Sales: realized,
            Cancelled: cancelled.Sum(x => x.Count),
            Returned: booked.Where(x => x.Status == SaleStatus.Returned).Sum(x => x.Count),
            Gross: Math.Round(gross, 2),
            ReturnedValue: Math.Round(returned, 2),
            Net: Math.Round(net, 2),
            AverageTicket: realized > 0 ? Math.Round(net / realized, 2) : 0m,
            CancelledValue: Math.Round(cancelled.Sum(x => x.Total), 2),
            Discounts: Math.Round(booked.Sum(x => x.Discount) + itemDiscounts, 2));
    }

    /// <summary>
    /// Best sellers by net revenue (what was sold minus what came back). Adds up to the net revenue
    /// except for sales whose items total zero but that still charge a fee (couvert, service).
    /// </summary>
    public async Task<IReadOnlyList<SalesReportProductDto>> TopProductsAsync(SalesWindow w, int take, CancellationToken ct)
    {
        // The sale-level discount (and any surcharge) is spread over the items by their share of the
        // subtotal — the same way a devolução refunds.
        var sold = await _db.SaleItems.AsNoTracking()
            .Join(Booked(w), i => i.SaleId, s => s.Id, (i, s) => new
            {
                i.ProductId, Name = i.Product!.Name, i.Quantity,
                Share = s.Subtotal > 0 ? i.Total * s.Total / s.Subtotal : 0m,
            })
            .GroupBy(x => new { x.ProductId, x.Name })
            .Select(g => new { g.Key.ProductId, g.Key.Name, Quantity = g.Sum(x => x.Quantity), Revenue = g.Sum(x => x.Share) })
            .ToListAsync(ct);

        var back = await _db.SaleReturnItems.AsNoTracking()
            .Join(_db.SaleReturns, i => i.SaleReturnId, r => r.Id, (i, r) => new { i, r.SaleId })
            .Join(Booked(w), x => x.SaleId, s => s.Id, (x, _) => x.i)
            .GroupBy(i => i.ProductId)
            .Select(g => new { ProductId = g.Key, Quantity = g.Sum(i => i.Quantity), Amount = g.Sum(i => i.Amount) })
            .ToDictionaryAsync(x => x.ProductId, ct);

        return sold
            .Select(r =>
            {
                var b = back.GetValueOrDefault(r.ProductId);
                return new SalesReportProductDto(r.ProductId.ToString(), r.Name,
                    r.Quantity - (b?.Quantity ?? 0), Math.Round(r.Revenue - (b?.Amount ?? 0), 2));
            })
            .Where(r => r.Quantity > 0)
            .OrderByDescending(r => r.Revenue).ThenBy(r => r.ProductName)
            .Take(take)
            .ToList();
    }

    public async Task<IReadOnlyList<SalesReportSellerDto>> SellersAsync(SalesWindow w, int take, CancellationToken ct)
    {
        var rows = await Booked(w)
            .Join(_db.Users, s => s.SoldByUserId, u => u.Id, (s, u) => new { u.Id, u.FullName, s.Status, Net = s.Total - s.ReturnedAmount })
            .GroupBy(x => new { x.Id, x.FullName })
            .Select(g => new { Name = g.Key.FullName, Sales = g.Count(x => x.Status != SaleStatus.Returned), Revenue = g.Sum(x => x.Net) })
            .OrderByDescending(g => g.Revenue)
            .Take(take)
            .ToListAsync(ct);
        return rows.Select(r => new SalesReportSellerDto(r.Name, r.Sales, Math.Round(r.Revenue, 2))).ToList();
    }

    /// <summary>Every day of the window (Brazil), including days without sales.</summary>
    public async Task<IReadOnlyList<SalesReportDayDto>> ByDayAsync(SalesWindow w, CancellationToken ct)
    {
        var rows = await Booked(w)
            .Select(s => new { At = s.ConfirmedAt ?? s.CreatedAt, s.Status, Net = s.Total - s.ReturnedAmount })
            .ToListAsync(ct);
        var byDay = rows
            .GroupBy(r => DateOnly.FromDateTime(BusinessClock.ToBrazil(r.At)))
            .ToDictionary(g => g.Key, g => (Sales: g.Count(x => x.Status != SaleStatus.Returned), Revenue: g.Sum(x => x.Net)));

        var days = new List<SalesReportDayDto>(w.Days);
        for (var d = w.From; d <= w.To; d = d.AddDays(1))
        {
            var (sales, revenue) = byDay.GetValueOrDefault(d);
            days.Add(new SalesReportDayDto(d.ToString("yyyy-MM-dd"), sales, Math.Round(revenue, 2)));
        }
        return days;
    }

    public async Task<IReadOnlyList<SalesReportPaymentDto>> PaymentsAsync(SalesWindow w, CancellationToken ct)
    {
        var rows = await _db.SalePayments.AsNoTracking()
            .Join(Booked(w), p => p.SaleId, s => s.Id, (p, _) => p)
            .GroupBy(p => p.Method)
            .Select(g => new { Method = g.Key, Sales = g.Select(p => p.SaleId).Distinct().Count(), Amount = g.Sum(p => p.Amount) })
            .ToListAsync(ct);
        return rows
            .OrderByDescending(r => r.Amount)
            .Select(r => new SalesReportPaymentDto(r.Method.ToString(), r.Sales, Math.Round(r.Amount, 2)))
            .ToList();
    }

    /// <summary>The sales of the window, oldest first, for the spreadsheet export (drafts left out).</summary>
    public IAsyncEnumerable<SaleExportRow> ExportRows(SalesWindow w)
        => InWindow(w)
            .Where(s => s.Status != SaleStatus.Draft && (s.Status != SaleStatus.Cancelled || s.ConfirmedAt != null))
            .OrderBy(s => s.ConfirmedAt ?? s.CreatedAt).ThenBy(s => s.Number)
            .Select(s => new SaleExportRow(
                s.Number,
                s.ConfirmedAt ?? s.CreatedAt,
                s.Status,
                s.Customer != null ? s.Customer.Name : null,
                s.SoldBy != null ? s.SoldBy.FullName : null,
                s.Subtotal,
                s.DiscountAmount,
                s.Total,
                s.ReturnedAmount,
                s.Payments.Select(p => p.Method).ToList()))
            .AsAsyncEnumerable();
}

public record SaleExportRow(
    int Number,
    DateTime At,
    SaleStatus Status,
    string? Customer,
    string? Seller,
    decimal Subtotal,
    decimal Discount,
    decimal Total,
    decimal Returned,
    List<PaymentMethod> Methods);
