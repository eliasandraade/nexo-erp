using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Nexo.Api.Filters;
using Nexo.Domain.Modules.Restaurante;
using Nexo.Infrastructure.Persistence;

namespace Nexo.Api.Controllers.Modules.Restaurante;

/// <summary>
/// Relatórios financeiros do restaurante: CMV por prato e resumo de KPIs por período.
/// Leitura pura — nunca altera estado.
/// </summary>
[ApiController]
[Route("api/restaurante/financeiro")]
[Authorize]
[RequireModule("restaurante")]
public class FinanceiroController : ControllerBase
{
    private readonly NexoDbContext _db;

    public FinanceiroController(NexoDbContext db) => _db = db;

    // ─────────────────────────────────────────────────────────────────────────
    // GET /api/restaurante/financeiro/cmv-report
    // CMV metrics for every active recipe card using current ingredient costs.
    // ─────────────────────────────────────────────────────────────────────────
    [HttpGet("cmv-report")]
    public async Task<ActionResult<CmvReportDto>> GetCmvReport(CancellationToken ct)
    {
        var settings  = await _db.FoodServiceSettings.FirstOrDefaultAsync(ct);
        var gasRate   = settings?.CostPerMinuteGas      ?? 0m;
        var laborRate = settings?.CostPerMinuteLaborRate ?? 0m;

        var cards = await _db.RestRecipeCards
            .Where(rc => rc.IsActive)
            .ToListAsync(ct);

        if (cards.Count == 0)
            return Ok(new CmvReportDto([]));

        var cardIds = cards.Select(c => c.Id).ToList();

        var allIngredients = await _db.RestRecipeIngredients
            .Where(i => cardIds.Contains(i.RecipeCardId))
            .ToListAsync(ct);

        var ingredientsByCard = allIngredients
            .GroupBy(i => i.RecipeCardId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var productIds = cards.Select(c => c.ProductId)
            .Concat(allIngredients.Select(i => i.IngredientProductId))
            .Distinct()
            .ToList();

        var products = await _db.Products
            .Where(p => productIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, ct);

        var items = new List<CmvReportItemDto>();
        foreach (var card in cards)
        {
            products.TryGetValue(card.ProductId, out var product);
            var salePrice = product?.SalePrice ?? 0m;

            var cardIngs     = ingredientsByCard.GetValueOrDefault(card.Id, []);
            var totalIngCost = cardIngs.Sum(ing =>
            {
                products.TryGetValue(ing.IngredientProductId, out var ingProd);
                return ing.Quantity * (ingProd?.CostPrice ?? 0m);
            });

            var prepMin     = (decimal)(card.TotalPrepTimeMin ?? 0);
            var unitIngCost = card.Yield > 0 ? totalIngCost / card.Yield : 0m;
            var gasCost     = prepMin * gasRate;
            var laborCost   = prepMin * laborRate;
            var totalCost   = unitIngCost + gasCost + laborCost;
            var cmvPct      = salePrice > 0 ? totalCost / salePrice * 100m : 0m;
            var margin      = salePrice - totalCost;
            var marginPct   = salePrice > 0 ? margin / salePrice * 100m : 0m;

            items.Add(new CmvReportItemDto(
                ProductId:          card.ProductId,
                ProductName:        product?.Name ?? string.Empty,
                ProductCode:        product?.Code ?? string.Empty,
                SalePrice:          Math.Round(salePrice, 2),
                UnitIngredientCost: Math.Round(unitIngCost, 4),
                GasCost:            Math.Round(gasCost, 4),
                LaborCost:          Math.Round(laborCost, 4),
                UnitCost:           Math.Round(totalCost, 4),
                CmvPercent:         Math.Round(cmvPct, 2),
                Margin:             Math.Round(margin, 2),
                MarginPercent:      Math.Round(marginPct, 2)));
        }

        var sorted = items.OrderByDescending(i => i.CmvPercent).ToList();
        return Ok(new CmvReportDto(sorted));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET /api/restaurante/financeiro/summary?from=yyyy-MM-dd&to=yyyy-MM-dd
    // KPIs from actual paid orders in the period.
    // ─────────────────────────────────────────────────────────────────────────
    [HttpGet("summary")]
    public async Task<ActionResult<FinanceiroSummaryDto>> GetSummary(
        [FromQuery] string? from,
        [FromQuery] string? to,
        CancellationToken ct)
    {
        var today   = DateTime.UtcNow.Date;
        var fromUtc = TryParseDate(from) ?? new DateTime(today.Year, today.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var toUtc   = (TryParseDate(to) ?? today).AddDays(1);

        var orderRows = await _db.RestOrders
            .Where(o => o.Status == RestOrderStatus.Paid
                     && o.ClosedAt >= fromUtc
                     && o.ClosedAt <  toUtc)
            .Select(o => new
            {
                o.Id,
                o.CouvertAmount,
                o.ServiceFeeAmount,
                ItemsTotal = o.Items
                    .Where(i => i.Status != RestOrderItemStatus.Cancelled)
                    .Sum(i => (decimal?)i.Total) ?? 0m,
            })
            .ToListAsync(ct);

        var ordersCount = orderRows.Count;
        var revenue     = orderRows.Sum(r => r.ItemsTotal + r.CouvertAmount + r.ServiceFeeAmount);

        if (ordersCount == 0)
            return Ok(new FinanceiroSummaryDto(
                OrdersCount:          0,
                Revenue:              0m,
                TotalCostOfGoodsSold: 0m,
                WeightedCmvPercent:   0m,
                GrossMargin:          0m,
                From:                 fromUtc.ToString("yyyy-MM-dd"),
                To:                   toUtc.AddDays(-1).ToString("yyyy-MM-dd")));

        var orderIds = orderRows.Select(r => r.Id).ToList();

        var itemsGrouped = await _db.RestOrderItems
            .Where(oi => orderIds.Contains(oi.OrderId)
                      && oi.Status != RestOrderItemStatus.Cancelled)
            .GroupBy(oi => oi.ProductId)
            .Select(g => new { ProductId = g.Key, TotalQty = g.Sum(i => i.Quantity) })
            .ToListAsync(ct);

        var soldProductIds = itemsGrouped.Select(g => g.ProductId).ToList();

        var recipeCards = await _db.RestRecipeCards
            .Where(rc => soldProductIds.Contains(rc.ProductId))
            .ToListAsync(ct);

        var rcIds = recipeCards.Select(rc => rc.Id).ToList();
        var allIngredients = await _db.RestRecipeIngredients
            .Where(i => rcIds.Contains(i.RecipeCardId))
            .ToListAsync(ct);

        var ingredientsByCard = allIngredients
            .GroupBy(i => i.RecipeCardId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var cardsByProduct = recipeCards.ToDictionary(rc => rc.ProductId);

        var allProductIds = recipeCards.Select(rc => rc.ProductId)
            .Concat(allIngredients.Select(i => i.IngredientProductId))
            .Distinct().ToList();

        var products = await _db.Products
            .Where(p => allProductIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, ct);

        var settings  = await _db.FoodServiceSettings.FirstOrDefaultAsync(ct);
        var gasRate   = settings?.CostPerMinuteGas      ?? 0m;
        var laborRate = settings?.CostPerMinuteLaborRate ?? 0m;

        decimal totalCogs = 0m;
        foreach (var row in itemsGrouped)
        {
            if (!cardsByProduct.TryGetValue(row.ProductId, out var card)) continue;

            var cardIngs     = ingredientsByCard.GetValueOrDefault(card.Id, []);
            var totalIngCost = cardIngs.Sum(ing =>
            {
                products.TryGetValue(ing.IngredientProductId, out var ingProd);
                return ing.Quantity * (ingProd?.CostPrice ?? 0m);
            });

            var prepMin     = (decimal)(card.TotalPrepTimeMin ?? 0);
            var unitIngCost = card.Yield > 0 ? totalIngCost / card.Yield : 0m;
            var gasCost     = prepMin * gasRate;
            var laborCost   = prepMin * laborRate;
            var unitCost    = unitIngCost + gasCost + laborCost;

            totalCogs += row.TotalQty * unitCost;
        }

        var weightedCmv = revenue > 0 ? totalCogs / revenue * 100m : 0m;
        var grossMargin = revenue - totalCogs;

        return Ok(new FinanceiroSummaryDto(
            OrdersCount:          ordersCount,
            Revenue:              Math.Round(revenue, 2),
            TotalCostOfGoodsSold: Math.Round(totalCogs, 2),
            WeightedCmvPercent:   Math.Round(weightedCmv, 2),
            GrossMargin:          Math.Round(grossMargin, 2),
            From:                 fromUtc.ToString("yyyy-MM-dd"),
            To:                   toUtc.AddDays(-1).ToString("yyyy-MM-dd")));
    }

    private static DateTime? TryParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return DateTime.TryParseExact(value, "yyyy-MM-dd",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal |
            System.Globalization.DateTimeStyles.AdjustToUniversal,
            out var dt) ? dt : null;
    }
}

// ── Response records ──────────────────────────────────────────────────────────

public record CmvReportItemDto(
    Guid    ProductId,
    string  ProductName,
    string  ProductCode,
    decimal SalePrice,
    decimal UnitIngredientCost,
    decimal GasCost,
    decimal LaborCost,
    decimal UnitCost,
    decimal CmvPercent,
    decimal Margin,
    decimal MarginPercent);

public record CmvReportDto(
    IReadOnlyList<CmvReportItemDto> Items);

public record FinanceiroSummaryDto(
    int     OrdersCount,
    decimal Revenue,
    decimal TotalCostOfGoodsSold,
    decimal WeightedCmvPercent,
    decimal GrossMargin,
    string  From,
    string  To);
