using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Nexo.Api.Filters;
using Nexo.Domain.Modules.Restaurante;
using Nexo.Infrastructure.Persistence;

namespace Nexo.Api.Controllers.Modules.Restaurante;

/// <summary>
/// Relatórios operacionais básicos do restaurante.
/// Leitura pura — nunca altera estado.
/// </summary>
[ApiController]
[Route("api/restaurante/reports")]
[Authorize]
[RequireModule("restaurante")]
public class ReportsController : ControllerBase
{
    private readonly NexoDbContext _db;

    public ReportsController(NexoDbContext db) => _db = db;

    /// <summary>
    /// Resumo operacional do período.
    ///
    /// Query params:
    ///   from  — data inicial (yyyy-MM-dd). Default: 30 dias atrás.
    ///   to    — data final   (yyyy-MM-dd). Default: hoje.
    ///
    /// Filtra comandas com Status == Paid e ClosedAt dentro do período.
    /// Revenue é a soma de (itens activos + couvert + taxa de serviço) por comanda.
    /// Tempo médio de mesa é calculado como ClosedAt - OpenedAt (em minutos).
    /// </summary>
    [HttpGet("summary")]
    public async Task<ActionResult<RestauranteSummaryDto>> GetSummary(
        [FromQuery] string? from,
        [FromQuery] string? to,
        CancellationToken ct)
    {
        // ── Period ────────────────────────────────────────────────────────────
        var today   = DateTime.UtcNow.Date;
        var fromUtc = TryParseDate(from) ?? today.AddDays(-30);
        var toUtc   = (TryParseDate(to) ?? today).AddDays(1); // exclusive upper bound

        // ── Query — projection avoids loading full entity graph ───────────────
        var rows = await _db.RestOrders
            .Where(o => o.Status == RestOrderStatus.Paid
                     && o.ClosedAt >= fromUtc
                     && o.ClosedAt <  toUtc)
            .Select(o => new
            {
                o.OpenedAt,
                o.ClosedAt,
                o.CouvertAmount,
                o.ServiceFeeAmount,
                ItemsTotal = o.Items
                    .Where(i => i.Status != RestOrderItemStatus.Cancelled)
                    .Sum(i => (decimal?)i.Total) ?? 0m,
            })
            .ToListAsync(ct);

        // ── Aggregations ──────────────────────────────────────────────────────
        int     ordersCount        = rows.Count;
        decimal revenue            = rows.Sum(r => r.ItemsTotal + r.CouvertAmount + r.ServiceFeeAmount);
        decimal averageTicket      = ordersCount > 0 ? Math.Round(revenue / ordersCount, 2) : 0m;

        // Average table time in minutes (only orders that have a ClosedAt)
        double averageTableMinutes = rows
            .Where(r => r.ClosedAt.HasValue)
            .Select(r => (r.ClosedAt!.Value - r.OpenedAt).TotalMinutes)
            .DefaultIfEmpty(0)
            .Average();

        return Ok(new RestauranteSummaryDto(
            OrdersCount:           ordersCount,
            Revenue:               Math.Round(revenue, 2),
            AverageTicket:         averageTicket,
            AverageTableMinutes:   Math.Round(averageTableMinutes, 1),
            From:                  fromUtc.ToString("yyyy-MM-dd"),
            To:                    toUtc.AddDays(-1).ToString("yyyy-MM-dd")
        ));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static DateTime? TryParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return DateTime.TryParseExact(value, "yyyy-MM-dd",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal |
            System.Globalization.DateTimeStyles.AdjustToUniversal,
            out var dt)
            ? dt
            : null;
    }
}

// ── Response DTO ──────────────────────────────────────────────────────────────

public record RestauranteSummaryDto(
    int     OrdersCount,
    decimal Revenue,
    decimal AverageTicket,
    double  AverageTableMinutes,
    string  From,
    string  To
);
