# CMV Restaurante Fase 2 — Financeiro Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a `/restaurante/financeiro` page showing CMV per dish and financial KPIs (revenue, COGS, CMV%, gross margin) for a selected month/year period.

**Architecture:** Two new read-only backend endpoints in `FinanceiroController` follow the same pattern as `ReportsController` — query `NexoDbContext` directly, no service layer. The CMV report derives costs from current recipe card data; the summary correlates those costs with actual orders in the period to compute weighted CMV. Frontend is a single page with a month picker, four KPI cards, and a sortable/filterable CMV-per-dish table.

**Tech Stack:** .NET 8 + EF Core + Npgsql (backend); React + TypeScript + TanStack Query v5 + TailwindCSS + shadcn/ui (frontend); xUnit + Testcontainers (integration tests).

---

## File Structure

**Backend — Create:**
- `nexo-backend/src/Nexo.Api/Controllers/Modules/Restaurante/FinanceiroController.cs`
  — Two GET endpoints (`/cmv-report`, `/summary`) + response record types. Pure query, no service.
- `nexo-backend/tests/Nexo.IntegrationTests/Restaurante/FinanceiroReportTests.cs`
  — 2 integration tests verifying the endpoints.

**Frontend — Create:**
- `nexo-main/src/modules/restaurante/api/financeiro.api.ts`
  — TypeScript response types + two fetch functions.
- `nexo-main/src/modules/restaurante/hooks/use-financeiro.ts`
  — Two TanStack Query hooks (`useCmvReport`, `useFinanceiroSummary`).
- `nexo-main/src/modules/restaurante/pages/FinanceiroPage.tsx`
  — Full page: month picker, KPI cards, CMV table with sort/filter.

**Frontend — Modify:**
- `nexo-main/src/app/router/AppRouter.tsx` — add `/restaurante/financeiro` route in MGMT block.
- `nexo-main/src/app/router/routes.ts` — add nav entry for Financeiro.

---

## Key Domain Facts (zero-context reference)

- `RestRecipeCard : StoreEntity` — fields used: `ProductId`, `Yield`, `TotalPrepTimeMin`, `IsActive`
- `RestRecipeIngredient : TenantEntity` — fields: `RecipeCardId`, `IngredientProductId`, `Quantity`
- `FoodServiceSettings : StoreEntity` — fields: `CostPerMinuteGas`, `CostPerMinuteLaborRate`
- `Product : StoreEntity` — fields: `SalePrice`, `CostPrice`, `Name`, `Code`
- `RestOrder : StoreEntity` — `Status` (Paid=terminal), `ClosedAt`, `CouvertAmount`, `ServiceFeeAmount`
- `RestOrderItem : TenantEntity` — `OrderId`, `ProductId`, `Quantity`, `Total`, `Status` (Cancelled to exclude)
- Global query filters apply automatically; `IgnoreQueryFilters()` is FORBIDDEN.
- CMV formula: `unitIngCost = Σ(ing.Qty × prod.CostPrice) / card.Yield`; `gasCost = prepMin × gasRate`; `laborCost = prepMin × laborRate`; `totalCost = unitIngCost + gasCost + laborCost`; `CMV% = totalCost / salePrice × 100`

---

## Task 1: Backend — FinanceiroController with CMV Report endpoint

**Files:**
- Create: `nexo-backend/src/Nexo.Api/Controllers/Modules/Restaurante/FinanceiroController.cs`

- [ ] **Step 1: Write the failing integration test for `/cmv-report`**

Create `nexo-backend/tests/Nexo.IntegrationTests/Restaurante/FinanceiroReportTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nexo.Application.Features.Auth;
using Nexo.Application.Features.Products;
using Nexo.Application.Modules.Restaurante;
using Nexo.Domain.Entities;
using Nexo.Domain.Enums;
using Nexo.Infrastructure.Persistence;
using Nexo.IntegrationTests.Helpers;

namespace Nexo.IntegrationTests.Restaurante;

[Collection("Integration")]
public class FinanceiroReportTests : IAsyncLifetime
{
    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public FinanceiroReportTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client  = factory.CreateApiClient();
    }

    public async Task InitializeAsync()
    {
        await AuthenticateAsync(_client);
        await EnsureModuleSubscriptionAsync("restaurante");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task AuthenticateAsync(HttpClient client)
    {
        var r = await client.PostAsJsonAsync("/api/auth/login",
            new { login = "admin", password = "nexo@2026" });
        r.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await r.Content.ReadFromJsonAsync<LoginResponse>();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", body!.AccessToken);
    }

    private async Task EnsureModuleSubscriptionAsync(string moduleKey)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        var tenant = await db.Tenants.FirstOrDefaultAsync();
        if (tenant is null) return;
        var exists = await db.ModuleSubscriptions
            .AnyAsync(s => s.TenantId == tenant.Id && s.ModuleKey == moduleKey);
        if (!exists)
        {
            var sub = ModuleSubscription.CreateFromStripe(
                tenantId: tenant.Id, moduleKey: moduleKey,
                stripeSubscriptionId: $"sub_test_{moduleKey}",
                stripePriceId: $"price_test_{moduleKey}",
                planType: PlanType.Lifetime,
                periodStart: DateTime.UtcNow, periodEnd: null);
            db.ModuleSubscriptions.Add(sub);
            await db.SaveChangesAsync();
        }
    }

    private async Task<ProductDto> CreateProductAsync(
        string code, string name, decimal salePrice, decimal costPrice, bool isIngredient)
    {
        var r = await _client.PostAsJsonAsync("/api/products",
            new CreateProductRequest(
                Code: code, Name: name, Unit: "un",
                SalePrice: salePrice, CostPrice: costPrice,
                TrackStock: false, IsIngredient: isIngredient));
        r.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await r.Content.ReadFromJsonAsync<ProductDto>())!;
    }

    // ── TEST 1: CMV Report ────────────────────────────────────────────────────

    [Fact]
    public async Task CmvReport_ReturnsItemWithCorrectCmvMetrics()
    {
        var suffix = Guid.NewGuid().ToString("N")[..6];

        // Arrange — ingredient product (costPrice = 8)
        var ing = await CreateProductAsync($"ING-F2-{suffix}", $"Ing F2 {suffix}", 0m, 8m, true);

        // Arrange — menu item product (salePrice = 30)
        var prato = await CreateProductAsync($"PRT-F2-{suffix}", $"Prato F2 {suffix}", 30m, 0m, false);

        // Arrange — recipe card (yield=1, no prep steps)
        var cardResp = await _client.PostAsJsonAsync("/api/restaurante/recipe-cards",
            new CreateRecipeCardRequest(ProductId: prato.Id, Yield: 1m, YieldUnit: "un"));
        cardResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var card = (await cardResp.Content.ReadFromJsonAsync<RecipeCardDto>())!;

        // Arrange — add ingredient (qty=2, cost=8 → totalIngCost=16, unitIngCost=16/1=16)
        var addIng = await _client.PostAsJsonAsync(
            $"/api/restaurante/recipe-cards/{card.Id}/ingredients",
            new AddIngredientRequest(
                IngredientProductId: ing.Id, Quantity: 2m, Unit: "un"));
        addIng.StatusCode.Should().Be(HttpStatusCode.OK);

        // Act
        var resp = await _client.GetFromJsonAsync<CmvReportDto>(
            "/api/restaurante/financeiro/cmv-report");

        // Assert
        resp.Should().NotBeNull();
        var item = resp!.Items.FirstOrDefault(i => i.ProductId == prato.Id);
        item.Should().NotBeNull("the newly created dish must appear in the CMV report");
        item!.SalePrice.Should().Be(30m);
        item.UnitIngredientCost.Should().Be(16m, "2 units × costPrice 8 = 16, yield=1 → unitIngCost=16");
        item.UnitCost.Should().Be(16m, "no prep → gasCost=0, laborCost=0 → totalCost=16");
        item.CmvPercent.Should().BeApproximately(53.33m, 0.01m, "16/30×100≈53.33");
        item.Margin.Should().BeApproximately(14m, 0.01m, "30-16=14");
    }

    // ── TEST 2: Financial Summary ─────────────────────────────────────────────

    [Fact]
    public async Task FinanceiroSummary_ReturnsZeroRevenue_WhenNoPaidOrdersInPeriod()
    {
        // Use a future date range with no orders
        var from = "2099-01-01";
        var to   = "2099-01-31";

        var resp = await _client.GetFromJsonAsync<FinanceiroSummaryDto>(
            $"/api/restaurante/financeiro/summary?from={from}&to={to}");

        resp.Should().NotBeNull();
        resp!.Revenue.Should().Be(0m);
        resp.TotalCostOfGoodsSold.Should().Be(0m);
        resp.OrdersCount.Should().Be(0);
        resp.WeightedCmvPercent.Should().Be(0m);
        resp.GrossMargin.Should().Be(0m);
    }
}

// ── Response types (mirrors backend records) ──────────────────────────────────

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
    IReadOnlyList<CmvReportItemDto> Items,
    string From,
    string To);

public record FinanceiroSummaryDto(
    int     OrdersCount,
    decimal Revenue,
    decimal TotalCostOfGoodsSold,
    decimal WeightedCmvPercent,
    decimal GrossMargin,
    string  From,
    string  To);
```

- [ ] **Step 2: Run test to verify it fails**

```
cd nexo-backend
dotnet test tests/Nexo.IntegrationTests --filter "FinanceiroReportTests" -v minimal
```

Expected: compile error — `CmvReportDto` and controller don't exist yet.

- [ ] **Step 3: Create `FinanceiroController.cs` with the CMV report endpoint**

Create `nexo-backend/src/Nexo.Api/Controllers/Modules/Restaurante/FinanceiroController.cs`:

```csharp
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
    // Returns CMV metrics for every active recipe card using current ingredient
    // costs. Independent of period — reflects current cost structure.
    // ─────────────────────────────────────────────────────────────────────────
    [HttpGet("cmv-report")]
    public async Task<ActionResult<CmvReportDto>> GetCmvReport(CancellationToken ct)
    {
        // ── Operational cost rates ────────────────────────────────────────────
        var settings  = await _db.FoodServiceSettings.FirstOrDefaultAsync(ct);
        var gasRate   = settings?.CostPerMinuteGas      ?? 0m;
        var laborRate = settings?.CostPerMinuteLaborRate ?? 0m;

        // ── Recipe cards (store-scoped via global query filter) ───────────────
        var cards = await _db.RestRecipeCards
            .Where(rc => rc.IsActive)
            .ToListAsync(ct);

        if (cards.Count == 0)
            return Ok(new CmvReportDto([], DateTime.UtcNow.ToString("yyyy-MM-dd"), DateTime.UtcNow.ToString("yyyy-MM-dd")));

        var cardIds = cards.Select(c => c.Id).ToList();

        // ── Ingredients for all recipe cards (one query) ──────────────────────
        var allIngredients = await _db.RestRecipeIngredients
            .Where(i => cardIds.Contains(i.RecipeCardId))
            .ToListAsync(ct);

        var ingredientsByCard = allIngredients
            .GroupBy(i => i.RecipeCardId)
            .ToDictionary(g => g.Key, g => g.ToList());

        // ── Products (menu items + ingredient products) — one query ───────────
        var productIds = cards.Select(c => c.ProductId)
            .Concat(allIngredients.Select(i => i.IngredientProductId))
            .Distinct()
            .ToList();

        var products = await _db.Products
            .Where(p => productIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, ct);

        // ── Compute CMV per card ──────────────────────────────────────────────
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
        return Ok(new CmvReportDto(sorted, string.Empty, string.Empty));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET /api/restaurante/financeiro/summary?from=yyyy-MM-dd&to=yyyy-MM-dd
    // KPIs derived from actual paid orders in the period.
    // ─────────────────────────────────────────────────────────────────────────
    [HttpGet("summary")]
    public async Task<ActionResult<FinanceiroSummaryDto>> GetSummary(
        [FromQuery] string? from,
        [FromQuery] string? to,
        CancellationToken ct)
    {
        var today   = DateTime.UtcNow.Date;
        var fromUtc = TryParseDate(from) ?? new DateTime(today.Year, today.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var toUtc   = (TryParseDate(to) ?? today).AddDays(1); // exclusive upper bound

        // ── Step 1: Revenue from Paid orders in period ────────────────────────
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
        {
            return Ok(new FinanceiroSummaryDto(
                OrdersCount:          0,
                Revenue:              0m,
                TotalCostOfGoodsSold: 0m,
                WeightedCmvPercent:   0m,
                GrossMargin:          0m,
                From:                 fromUtc.ToString("yyyy-MM-dd"),
                To:                   toUtc.AddDays(-1).ToString("yyyy-MM-dd")));
        }

        // ── Step 2: Order items grouped by product ────────────────────────────
        var orderIds = orderRows.Select(r => r.Id).ToList();

        var itemsGrouped = await _db.RestOrderItems
            .Where(oi => orderIds.Contains(oi.OrderId)
                      && oi.Status != RestOrderItemStatus.Cancelled)
            .GroupBy(oi => oi.ProductId)
            .Select(g => new { ProductId = g.Key, TotalQty = g.Sum(i => i.Quantity) })
            .ToListAsync(ct);

        // ── Step 3: Load recipe cards and ingredients for sold products ────────
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

        // ── Step 4: Load product costs ────────────────────────────────────────
        var allProductIds = recipeCards.Select(rc => rc.ProductId)
            .Concat(allIngredients.Select(i => i.IngredientProductId))
            .Distinct().ToList();

        var products = await _db.Products
            .Where(p => allProductIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, ct);

        // ── Step 5: Operational rates ─────────────────────────────────────────
        var settings  = await _db.FoodServiceSettings.FirstOrDefaultAsync(ct);
        var gasRate   = settings?.CostPerMinuteGas      ?? 0m;
        var laborRate = settings?.CostPerMinuteLaborRate ?? 0m;

        // ── Step 6: Compute COGS ──────────────────────────────────────────────
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

    // ── Helpers ───────────────────────────────────────────────────────────────

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
    IReadOnlyList<CmvReportItemDto> Items,
    string From,
    string To);

public record FinanceiroSummaryDto(
    int     OrdersCount,
    decimal Revenue,
    decimal TotalCostOfGoodsSold,
    decimal WeightedCmvPercent,
    decimal GrossMargin,
    string  From,
    string  To);
```

- [ ] **Step 4: Build to verify no compile errors**

```
cd nexo-backend
dotnet build src/Nexo.Api/Nexo.Api.csproj -c Release --no-restore 2>&1 | tail -5
```

Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 5: Run integration tests**

```
cd nexo-backend
dotnet test tests/Nexo.IntegrationTests --filter "FinanceiroReportTests" -v minimal
```

Expected: both tests PASS (Docker must be running). If Docker is not running, skip — tests verify correctness later.

- [ ] **Step 6: Commit**

```bash
git add nexo-backend/src/Nexo.Api/Controllers/Modules/Restaurante/FinanceiroController.cs
git add nexo-backend/tests/Nexo.IntegrationTests/Restaurante/FinanceiroReportTests.cs
git commit -m "feat(restaurante): add FinanceiroController with CMV report and financial summary endpoints"
```

---

## Task 2: Frontend — API types and fetch functions

**Files:**
- Create: `nexo-main/src/modules/restaurante/api/financeiro.api.ts`

- [ ] **Step 1: Create the file**

```typescript
import { apiClient } from "@/services/api-client";

// ── Response types ────────────────────────────────────────────────────────────

export interface CmvReportItemDto {
  productId:          string;
  productName:        string;
  productCode:        string;
  salePrice:          number;
  unitIngredientCost: number;
  gasCost:            number;
  laborCost:          number;
  unitCost:           number;
  cmvPercent:         number;
  margin:             number;
  marginPercent:      number;
}

export interface CmvReportDto {
  items: CmvReportItemDto[];
  from:  string;
  to:    string;
}

export interface FinanceiroSummaryDto {
  ordersCount:          number;
  revenue:              number;
  totalCostOfGoodsSold: number;
  weightedCmvPercent:   number;
  grossMargin:          number;
  from:                 string;
  to:                   string;
}

// ── Fetch functions ───────────────────────────────────────────────────────────

export const fetchCmvReport = (): Promise<CmvReportDto> =>
  apiClient.get<CmvReportDto>("/restaurante/financeiro/cmv-report");

export const fetchFinanceiroSummary = (
  from: string,
  to:   string,
): Promise<FinanceiroSummaryDto> =>
  apiClient.get<FinanceiroSummaryDto>(
    `/restaurante/financeiro/summary?from=${from}&to=${to}`,
  );
```

- [ ] **Step 2: Run TypeScript check**

```
cd nexo-main
npx tsc --noEmit 2>&1 | head -20
```

Expected: no errors for this file (no imports to check yet).

- [ ] **Step 3: Commit**

```bash
git add nexo-main/src/modules/restaurante/api/financeiro.api.ts
git commit -m "feat(restaurante): add financeiro API types and fetch functions"
```

---

## Task 3: Frontend — TanStack Query hooks

**Files:**
- Create: `nexo-main/src/modules/restaurante/hooks/use-financeiro.ts`

- [ ] **Step 1: Create the file**

```typescript
import { useQuery } from "@tanstack/react-query";
import { fetchCmvReport, fetchFinanceiroSummary } from "../api/financeiro.api";

export const CMV_REPORT_KEY = ["financeiro", "cmv-report"] as const;

export const FINANCEIRO_SUMMARY_KEY = (from: string, to: string) =>
  ["financeiro", "summary", from, to] as const;

/** CMV report — independent of period, reflects current recipe card costs. */
export function useCmvReport() {
  return useQuery({
    queryKey: CMV_REPORT_KEY,
    queryFn:  fetchCmvReport,
    staleTime: 2 * 60_000, // 2 min — costs change rarely
  });
}

/** Financial KPIs for the given period (yyyy-MM-dd strings). */
export function useFinanceiroSummary(from: string, to: string) {
  return useQuery({
    queryKey: FINANCEIRO_SUMMARY_KEY(from, to),
    queryFn:  () => fetchFinanceiroSummary(from, to),
    enabled:  !!from && !!to,
  });
}
```

- [ ] **Step 2: Run TypeScript check**

```
cd nexo-main
npx tsc --noEmit 2>&1 | head -20
```

Expected: no errors.

- [ ] **Step 3: Commit**

```bash
git add nexo-main/src/modules/restaurante/hooks/use-financeiro.ts
git commit -m "feat(restaurante): add useCmvReport and useFinanceiroSummary hooks"
```

---

## Task 4: Frontend — FinanceiroPage

**Files:**
- Create: `nexo-main/src/modules/restaurante/pages/FinanceiroPage.tsx`

This page has:
- A month picker (month `<select>` + year `<input>`) that derives `from`/`to` dates
- Four KPI cards (revenue, COGS, CMV%, gross margin)
- A sortable, searchable CMV table (client-side)

- [ ] **Step 1: Create the file**

```typescript
import { useState, useMemo } from "react";
import {
  TrendingUp, TrendingDown, DollarSign, ShoppingBag,
  ArrowUpDown, ArrowUp, ArrowDown, Search,
} from "lucide-react";
import { cn } from "@/lib/utils";
import { Input } from "@/components/ui/input";
import { Button } from "@/components/ui/button";
import {
  Select, SelectContent, SelectItem, SelectTrigger, SelectValue,
} from "@/components/ui/select";
import { PageHeader } from "@/components/shared/PageHeader";
import { useCmvReport, useFinanceiroSummary } from "../hooks/use-financeiro";
import type { CmvReportItemDto } from "../api/financeiro.api";

// ── Helpers ───────────────────────────────────────────────────────────────────

function fmt(v: number) {
  return v.toLocaleString("pt-BR", { style: "currency", currency: "BRL" });
}

function fmtPct(v: number) {
  return `${v.toFixed(1)}%`;
}

function cmvColor(pct: number): string {
  if (pct < 30) return "text-green-600 dark:text-green-400";
  if (pct <= 40) return "text-yellow-600 dark:text-yellow-400";
  return "text-red-600 dark:text-red-400";
}

function cmvBadge(pct: number): string {
  if (pct < 30) return "bg-green-100 text-green-700 dark:bg-green-900/30 dark:text-green-400";
  if (pct <= 40) return "bg-yellow-100 text-yellow-700 dark:bg-yellow-900/30 dark:text-yellow-400";
  return "bg-red-100 text-red-700 dark:bg-red-900/30 dark:text-red-400";
}

// ── Month picker helpers ──────────────────────────────────────────────────────

const MONTHS = [
  "Janeiro","Fevereiro","Março","Abril","Maio","Junho",
  "Julho","Agosto","Setembro","Outubro","Novembro","Dezembro",
];

function monthBounds(year: number, month: number): { from: string; to: string } {
  const pad   = (n: number) => String(n).padStart(2, "0");
  const last  = new Date(year, month, 0).getDate(); // last day of month
  return {
    from: `${year}-${pad(month)}-01`,
    to:   `${year}-${pad(month)}-${pad(last)}`,
  };
}

// ── KPI Card ──────────────────────────────────────────────────────────────────

interface KpiCardProps {
  icon:    React.ElementType;
  label:   string;
  value:   string;
  sub?:    string;
  color?:  string;
}

function KpiCard({ icon: Icon, label, value, sub, color = "text-primary" }: KpiCardProps) {
  return (
    <div className="rounded-xl border border-border bg-card p-4 flex flex-col gap-2">
      <div className="flex items-center gap-2">
        <div className={cn("p-2 rounded-lg bg-muted/60", color)}>
          <Icon className="h-4 w-4" />
        </div>
        <span className="text-xs font-medium text-muted-foreground">{label}</span>
      </div>
      <p className="text-2xl font-bold text-foreground tabular-nums leading-none">{value}</p>
      {sub && <p className="text-xs text-muted-foreground">{sub}</p>}
    </div>
  );
}

// ── CMV Table ─────────────────────────────────────────────────────────────────

type SortField = "productName" | "salePrice" | "unitCost" | "cmvPercent" | "margin";
type SortDir   = "asc" | "desc";

interface CmvTableProps {
  items: CmvReportItemDto[];
}

function CmvTable({ items }: CmvTableProps) {
  const [search,  setSearch]  = useState("");
  const [sortBy,  setSortBy]  = useState<SortField>("cmvPercent");
  const [sortDir, setSortDir] = useState<SortDir>("desc");

  const toggleSort = (field: SortField) => {
    if (sortBy === field) {
      setSortDir(d => d === "asc" ? "desc" : "asc");
    } else {
      setSortBy(field);
      setSortDir("desc");
    }
  };

  const SortIcon = ({ field }: { field: SortField }) => {
    if (sortBy !== field) return <ArrowUpDown className="h-3 w-3 text-muted-foreground" />;
    return sortDir === "asc"
      ? <ArrowUp className="h-3 w-3" />
      : <ArrowDown className="h-3 w-3" />;
  };

  const filtered = useMemo(() => {
    const q = search.toLowerCase();
    return items.filter(i =>
      i.productName.toLowerCase().includes(q) ||
      i.productCode.toLowerCase().includes(q),
    );
  }, [items, search]);

  const sorted = useMemo(() => {
    return [...filtered].sort((a, b) => {
      const dir = sortDir === "asc" ? 1 : -1;
      if (sortBy === "productName") return dir * a.productName.localeCompare(b.productName);
      return dir * (a[sortBy] - b[sortBy]);
    });
  }, [filtered, sortBy, sortDir]);

  const thClass = "text-left text-xs font-medium text-muted-foreground uppercase tracking-wide px-3 py-2";
  const tdClass = "px-3 py-3 text-sm";

  return (
    <div className="space-y-3">
      <div className="relative">
        <Search className="absolute left-3 top-1/2 -translate-y-1/2 h-4 w-4 text-muted-foreground" />
        <Input
          className="pl-9 text-sm"
          placeholder="Filtrar por nome ou código…"
          value={search}
          onChange={e => setSearch(e.target.value)}
        />
      </div>

      {sorted.length === 0 ? (
        <div className="text-center py-12 text-sm text-muted-foreground">
          Nenhum prato encontrado. Crie fichas técnicas para seus produtos do cardápio.
        </div>
      ) : (
        <div className="rounded-xl border border-border overflow-hidden">
          <div className="overflow-x-auto">
            <table className="w-full min-w-[640px]">
              <thead className="bg-muted/40 border-b border-border">
                <tr>
                  <th className={thClass}>
                    <button
                      className="flex items-center gap-1 hover:text-foreground transition-colors"
                      onClick={() => toggleSort("productName")}
                    >
                      Prato <SortIcon field="productName" />
                    </button>
                  </th>
                  <th className={cn(thClass, "text-right")}>
                    <button
                      className="flex items-center gap-1 ml-auto hover:text-foreground transition-colors"
                      onClick={() => toggleSort("salePrice")}
                    >
                      Preço venda <SortIcon field="salePrice" />
                    </button>
                  </th>
                  <th className={cn(thClass, "text-right")}>
                    <button
                      className="flex items-center gap-1 ml-auto hover:text-foreground transition-colors"
                      onClick={() => toggleSort("unitCost")}
                    >
                      Custo unitário <SortIcon field="unitCost" />
                    </button>
                  </th>
                  <th className={cn(thClass, "text-right")}>
                    <button
                      className="flex items-center gap-1 ml-auto hover:text-foreground transition-colors"
                      onClick={() => toggleSort("margin")}
                    >
                      Margem <SortIcon field="margin" />
                    </button>
                  </th>
                  <th className={cn(thClass, "text-right")}>
                    <button
                      className="flex items-center gap-1 ml-auto hover:text-foreground transition-colors"
                      onClick={() => toggleSort("cmvPercent")}
                    >
                      CMV% <SortIcon field="cmvPercent" />
                    </button>
                  </th>
                </tr>
              </thead>
              <tbody className="divide-y divide-border">
                {sorted.map(item => (
                  <tr key={item.productId} className="hover:bg-muted/20 transition-colors">
                    <td className={tdClass}>
                      <p className="font-medium">{item.productName}</p>
                      <p className="text-xs text-muted-foreground">{item.productCode}</p>
                    </td>
                    <td className={cn(tdClass, "text-right tabular-nums")}>
                      {fmt(item.salePrice)}
                    </td>
                    <td className={cn(tdClass, "text-right tabular-nums")}>
                      <span>{fmt(item.unitCost)}</span>
                      {(item.gasCost > 0 || item.laborCost > 0) && (
                        <p className="text-xs text-muted-foreground">
                          ing: {fmt(item.unitIngredientCost)}
                        </p>
                      )}
                    </td>
                    <td className={cn(tdClass, "text-right tabular-nums")}>
                      <p>{fmt(item.margin)}</p>
                      <p className="text-xs text-muted-foreground">{fmtPct(item.marginPercent)}</p>
                    </td>
                    <td className={cn(tdClass, "text-right")}>
                      <span className={cn(
                        "inline-block px-2 py-0.5 rounded-full text-xs font-semibold tabular-nums",
                        cmvBadge(item.cmvPercent),
                      )}>
                        {fmtPct(item.cmvPercent)}
                      </span>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      )}

      <p className="text-xs text-muted-foreground">
        {sorted.length} de {items.length} prato{items.length !== 1 ? "s" : ""} ·
        CMV verde &lt;30% · amarelo 30–40% · vermelho &gt;40%
      </p>
    </div>
  );
}

// ── Main page ─────────────────────────────────────────────────────────────────

export default function FinanceiroPage() {
  const now = new Date();
  const [month, setMonth] = useState(now.getMonth() + 1); // 1-based
  const [year,  setYear]  = useState(now.getFullYear());

  const { from, to } = monthBounds(year, month);

  const { data: cmvData,  isLoading: cmvLoading  } = useCmvReport();
  const { data: summary,  isLoading: sumLoading  } = useFinanceiroSummary(from, to);

  const isLoading = cmvLoading || sumLoading;

  return (
    <div className="p-6 space-y-6">
      <PageHeader
        title="Financeiro"
        description="CMV por prato e KPIs do período selecionado."
      />

      {/* ── Period picker ──────────────────────────────────────────────── */}
      <div className="flex items-center gap-3 flex-wrap">
        <Select value={String(month)} onValueChange={v => setMonth(Number(v))}>
          <SelectTrigger className="w-36 text-sm">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            {MONTHS.map((m, i) => (
              <SelectItem key={i + 1} value={String(i + 1)}>{m}</SelectItem>
            ))}
          </SelectContent>
        </Select>

        <div className="flex items-center gap-2">
          <Button
            variant="outline" size="icon" className="h-9 w-9 text-sm"
            onClick={() => setYear(y => y - 1)}
          >
            ‹
          </Button>
          <span className="tabular-nums text-sm font-medium w-12 text-center">{year}</span>
          <Button
            variant="outline" size="icon" className="h-9 w-9 text-sm"
            onClick={() => setYear(y => y + 1)}
            disabled={year >= now.getFullYear()}
          >
            ›
          </Button>
        </div>

        <span className="text-xs text-muted-foreground">
          {from} → {to}
        </span>
      </div>

      {/* ── KPI Cards ──────────────────────────────────────────────────── */}
      <div className="grid grid-cols-2 md:grid-cols-4 gap-4">
        <KpiCard
          icon={DollarSign}
          label="Faturamento bruto"
          value={isLoading ? "—" : fmt(summary?.revenue ?? 0)}
          sub={isLoading ? undefined : `${summary?.ordersCount ?? 0} comanda(s)`}
          color="text-blue-600"
        />
        <KpiCard
          icon={ShoppingBag}
          label="Custo de mercadoria"
          value={isLoading ? "—" : fmt(summary?.totalCostOfGoodsSold ?? 0)}
          sub="CMG do período"
          color="text-orange-600"
        />
        <KpiCard
          icon={TrendingUp}
          label="CMV% ponderado"
          value={isLoading ? "—" : fmtPct(summary?.weightedCmvPercent ?? 0)}
          sub="Baseado nos pedidos"
          color={isLoading ? "text-muted-foreground"
            : cmvColor(summary?.weightedCmvPercent ?? 0)}
        />
        <KpiCard
          icon={TrendingDown}
          label="Margem bruta"
          value={isLoading ? "—" : fmt(summary?.grossMargin ?? 0)}
          sub={isLoading || !summary?.revenue ? undefined
            : fmtPct(100 - (summary.totalCostOfGoodsSold / summary.revenue) * 100)}
          color="text-green-600"
        />
      </div>

      {/* ── CMV Table ──────────────────────────────────────────────────── */}
      <div className="space-y-2">
        <div>
          <h2 className="text-sm font-semibold">CMV por prato</h2>
          <p className="text-xs text-muted-foreground mt-0.5">
            Custo atual baseado nas fichas técnicas. Ordene por coluna clicando no cabeçalho.
          </p>
        </div>
        {cmvLoading ? (
          <div className="space-y-2">
            {[1, 2, 3].map(i => <div key={i} className="h-14 rounded-xl bg-muted animate-pulse" />)}
          </div>
        ) : (
          <CmvTable items={cmvData?.items ?? []} />
        )}
      </div>
    </div>
  );
}
```

- [ ] **Step 2: Run TypeScript check**

```
cd nexo-main
npx tsc --noEmit 2>&1 | head -20
```

Expected: no errors.

- [ ] **Step 3: Commit**

```bash
git add nexo-main/src/modules/restaurante/pages/FinanceiroPage.tsx
git commit -m "feat(restaurante): add FinanceiroPage with KPI cards, month picker and CMV table"
```

---

## Task 5: Frontend — Wire route and nav entry

**Files:**
- Modify: `nexo-main/src/app/router/AppRouter.tsx`
- Modify: `nexo-main/src/app/router/routes.ts`

- [ ] **Step 1: Add import and route in `AppRouter.tsx`**

Add the import after the `RelatoriosPage` import:
```typescript
import FinanceiroPage from "@/modules/restaurante/pages/FinanceiroPage";
```

Add the route inside the "Gestão restaurante — management only" block (after `RelatoriosPage`):
```tsx
<Route path="/restaurante/financeiro" element={<FinanceiroPage />} />
```

The full block after the change:
```tsx
{/* Gestão restaurante — management only */}
<Route element={<RoleRoute path="/restaurante/portal" />}>
  <Route element={<MainAppLayout />}>
    <Route path="/restaurante/portal"     element={<PortalSetupPage />} />
    <Route path="/restaurante/configurar" element={<RestauranteSetupPage />} />
    <Route path="/restaurante/relatorios" element={<RelatoriosPage />} />
    <Route path="/restaurante/financeiro" element={<FinanceiroPage />} />
  </Route>
</Route>
```

- [ ] **Step 2: Add nav entry in `routes.ts`**

Add after the Relatórios entry:
```typescript
import { TrendingUp } from "lucide-react"; // already imported? check existing imports
```

The `TrendingUp` icon is already in the lucide-react imports if not already present. Add it to the import list. Then add the route entry:

```typescript
{ path: "/restaurante/financeiro", label: "Financeiro", icon: TrendingUp, moduleKey: "restaurante", roles: MGMT },
```

The full restaurante section in `routes.ts` after the change:
```typescript
// ── Restaurante — operação (vendedor) e gestão (gerente/diretoria) ───────
{ path: "/restaurante",            label: "Restaurante",   icon: UtensilsCrossed,   moduleKey: "restaurante", roles: [...MGMT, "vendedor"] },
{ path: "/restaurante/delivery",   label: "Entregas",      icon: Bike,              moduleKey: "restaurante", roles: [...MGMT, "vendedor"] },
{ path: "/restaurante/portal",     label: "Portal",        icon: Globe,             moduleKey: "restaurante", roles: MGMT },
{ path: "/restaurante/configurar", label: "Config. Mesas", icon: SlidersHorizontal, moduleKey: "restaurante", roles: MGMT },
{ path: "/restaurante/relatorios", label: "Relatórios",    icon: BarChart2,         moduleKey: "restaurante", roles: MGMT },
{ path: "/restaurante/financeiro", label: "Financeiro",    icon: TrendingUp,        moduleKey: "restaurante", roles: MGMT },
```

- [ ] **Step 3: Run TypeScript check**

```
cd nexo-main
npx tsc --noEmit 2>&1 | head -20
```

Expected: no errors.

- [ ] **Step 4: Commit**

```bash
git add nexo-main/src/app/router/AppRouter.tsx nexo-main/src/app/router/routes.ts
git commit -m "feat(restaurante): wire /restaurante/financeiro route and nav entry"
```

---

## Self-Review

**1. Spec coverage:**
- ✅ `GET /api/restaurante/financeiro/cmv-report` — CMV por prato (custo, preço, CMV%)
- ✅ `GET /api/restaurante/financeiro/summary` — faturamento, custo total ingredientes, CMV% médio ponderado, margem bruta
- ✅ Nova rota `/restaurante/financeiro` (MGMT only)
- ✅ Tabela CMV por prato: ordenável (client-side sort), filtrável (search input), CMV%, custo unitário, preço de venda, margem
- ✅ KPI cards: faturamento bruto, custo de mercadoria, CMV%, margem bruta
- ✅ Filtro de período (mês/ano via month select + year stepper)

**2. Placeholder scan:** None found. All code blocks are complete.

**3. Type consistency:**
- `CmvReportItemDto` — defined in backend record, test file, and `financeiro.api.ts`. Field names match (camelCase in TS, PascalCase in C# records → serialized as camelCase by default).
- `FinanceiroSummaryDto` — same.
- `useCmvReport` → `fetchCmvReport` → `/restaurante/financeiro/cmv-report` → `CmvReportDto` — chain is consistent.
- `useFinanceiroSummary(from, to)` → `fetchFinanceiroSummary(from, to)` → `/restaurante/financeiro/summary?from=&to=` → `FinanceiroSummaryDto` — consistent.
- `monthBounds` returns `from`/`to` as `yyyy-MM-dd` strings — matches what the backend expects.
- `TrendingUp` icon — exists in lucide-react, matches other icon usage in `routes.ts`.
