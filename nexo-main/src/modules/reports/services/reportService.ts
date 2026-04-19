import { listSales } from "@/modules/sales/api/sales.api";
import { saleToLegacy } from "@/modules/sales/utils/saleAdapter";
import { cashService } from "@/modules/cash/services/cashService";
import { inventoryService } from "@/modules/inventory/services/inventoryService";
import { commissionService } from "@/modules/commissions/services/commissionService";
import type { CompletedSale } from "@/modules/sales/types";
import type {
  ReportFilters,
  SalesOperationalSummary,
  SalesByOperatorRow,
  TopProductRow,
  CancellationSummary,
  CommissionReportSummary,
  CashReportSummary,
  InventoryReportSummary,
} from "../types";

const delay = (ms = 200) => new Promise((r) => setTimeout(r, ms));

async function getAllSales(): Promise<CompletedSale[]> {
  const dtos = await listSales();
  return dtos.map(saleToLegacy);
}

function applyFilters(sales: CompletedSale[], filters?: ReportFilters): CompletedSale[] {
  if (!filters) return sales;
  return sales.filter((sale) => {
    if (filters.operator !== "all" && sale.operator !== filters.operator) return false;
    if (filters.status !== "all" && sale.status !== filters.status) return false;
    if (filters.paymentMethod !== "all") {
      if (!sale.payments.some((p) => p.method === filters.paymentMethod)) return false;
    }
    return true;
  });
}

export const reportService = {
  async getOperationalSummary(filters?: ReportFilters): Promise<SalesOperationalSummary> {
    await delay();
    const allSales = await getAllSales();
    const sales = applyFilters(allSales, filters);

    // Revenue from non-fully-cancelled sales only
    const revenueSales = sales.filter((s) => s.status !== "cancelled");
    const totalRevenue = revenueSales.reduce((acc, s) => acc + s.total, 0);
    const cancelledCount = sales.filter((s) => s.status === "cancelled").length;
    const partiallyCancelledCount = sales.filter((s) => s.status === "partially_cancelled").length;
    const cancelledValue = sales
      .filter((s) => s.status === "cancelled")
      .reduce((acc, s) => acc + s.total, 0);

    return {
      totalSales: sales.length,
      totalRevenue: Math.round(totalRevenue * 100) / 100,
      averageTicket: revenueSales.length > 0
        ? Math.round((totalRevenue / revenueSales.length) * 100) / 100
        : 0,
      cancelledCount,
      partiallyCancelledCount,
      cancelledValue: Math.round(cancelledValue * 100) / 100,
    };
  },

  async getSalesByOperator(filters?: ReportFilters): Promise<SalesByOperatorRow[]> {
    await delay();
    const allSales = await getAllSales();
    const sales = applyFilters(allSales, filters);

    const byOp = new Map<string, { count: number; revenue: number; cancelled: number }>();

    for (const sale of sales) {
      if (!byOp.has(sale.operator)) {
        byOp.set(sale.operator, { count: 0, revenue: 0, cancelled: 0 });
      }
      const entry = byOp.get(sale.operator)!;
      entry.count++;
      if (sale.status !== "cancelled") entry.revenue += sale.total;
      if (sale.status === "cancelled") entry.cancelled++;
    }

    return Array.from(byOp.entries())
      .map(([operator, data]) => ({
        operator,
        salesCount: data.count,
        totalRevenue: Math.round(data.revenue * 100) / 100,
        cancelledCount: data.cancelled,
        averageTicket: data.count - data.cancelled > 0
          ? Math.round((data.revenue / (data.count - data.cancelled)) * 100) / 100
          : 0,
      }))
      .sort((a, b) => b.totalRevenue - a.totalRevenue);
  },

  async getTopProducts(
    limit = 10,
    filters?: ReportFilters
  ): Promise<TopProductRow[]> {
    await delay();
    const allSales = await getAllSales();
    const sales = applyFilters(allSales, filters);

    const byProduct = new Map<
      string,
      { code: string; description: string; qty: number; revenue: number }
    >();

    for (const sale of sales) {
      for (const item of sale.items) {
        // Skip cancelled items
        if (item.status === "cancelled") continue;
        if (!byProduct.has(item.productId)) {
          byProduct.set(item.productId, {
            code: item.code,
            description: item.description,
            qty: 0,
            revenue: 0,
          });
        }
        const entry = byProduct.get(item.productId)!;
        entry.qty += item.quantity;
        entry.revenue += item.totalPrice;
      }
    }

    return Array.from(byProduct.values())
      .map((p) => ({
        productCode: p.code,
        productDescription: p.description,
        quantitySold: p.qty,
        revenueGenerated: Math.round(p.revenue * 100) / 100,
      }))
      .sort((a, b) => b.revenueGenerated - a.revenueGenerated)
      .slice(0, limit);
  },

  async getCancellationSummary(filters?: ReportFilters): Promise<CancellationSummary> {
    await delay();
    const allSales = await getAllSales();
    const sales = applyFilters(allSales, filters);

    const cancelledSales = sales.filter((s) => s.status === "cancelled");
    const partiallyCancelled = sales.filter((s) => s.status === "partially_cancelled");
    const cancelledItemsCount = sales.reduce(
      (acc, s) => acc + s.items.filter((i) => i.status === "cancelled").length,
      0
    );
    const totalReversedValue = cancelledSales.reduce((acc, s) => acc + s.total, 0);

    return {
      cancelledSalesCount: cancelledSales.length,
      partiallyCancelledCount: partiallyCancelled.length,
      cancelledItemsCount,
      totalReversedValue: Math.round(totalReversedValue * 100) / 100,
    };
  },

  async getCommissionSummary(): Promise<CommissionReportSummary> {
    await delay();
    const overall = await commissionService.getCommissionSummaryOverall();
    const bySeller = await commissionService.getCommissionSummaryBySeller();

    return {
      totalActive: overall.totalActive,
      totalReversed: overall.totalReversed,
      topSellers: bySeller.slice(0, 5).map((s) => ({
        operator: s.operator,
        activeCommission: s.activeCommission,
      })),
    };
  },

  async getCashSummary(): Promise<CashReportSummary> {
    await delay();
    const [currentSession, movements, sessionHistory] = await Promise.all([
      cashService.getCurrentSession(),
      cashService.listMovements(),
      cashService.getSessionHistory(),
    ]);

    const totalSalesThisSession = movements
      .filter((m) => m.type === "sale" && m.paymentMethod === "cash")
      .reduce((acc, m) => acc + m.amount, 0);

    const totalWithdrawals = movements
      .filter((m) => m.type === "withdrawal")
      .reduce((acc, m) => acc + Math.abs(m.amount), 0);

    const totalReinforcements = movements
      .filter((m) => m.type === "reinforcement")
      .reduce((acc, m) => acc + m.amount, 0);

    const closedWithDivergence = sessionHistory.filter(
      (s) => s.divergence !== undefined && Math.abs(s.divergence!) > 0.01
    ).length;

    return {
      currentSession: {
        isOpen: !!currentSession,
        operator: currentSession?.operator,
        openedAt: currentSession?.openedAt,
        expectedBalance: currentSession?.expectedBalance ?? 0,
      },
      totalSalesThisSession: Math.round(totalSalesThisSession * 100) / 100,
      totalWithdrawalsThisSession: Math.round(totalWithdrawals * 100) / 100,
      totalReinforcementsThisSession: Math.round(totalReinforcements * 100) / 100,
      closedSessionsWithDivergence: closedWithDivergence,
    };
  },

  async getInventorySummary(): Promise<InventoryReportSummary> {
    await delay();
    const [items, alerts] = await Promise.all([
      inventoryService.list(),
      inventoryService.listAlerts(),
    ]);

    return {
      zeroStockCount: items.filter((i) => i.status === "zero").length,
      lowStockCount: items.filter((i) => i.status === "low").length,
      normalCount: items.filter((i) => i.status === "normal").length,
      highStockCount: items.filter((i) => i.status === "high").length,
      totalAlertCount: alerts.length,
    };
  },

  async listOperators(): Promise<string[]> {
    await delay(50);
    const sales = await getAllSales();
    const ops = new Set(sales.map((s) => s.operator));
    return Array.from(ops).sort();
  },
};
