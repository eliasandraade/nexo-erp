import { posService } from "@/modules/sales/services/posService";
import type { CommissionRecord, CommissionSummaryBySeller, CommissionFilters } from "../types";
import { getCommissionRate } from "../data/commissionRates";

const delay = (ms = 200) => new Promise((r) => setTimeout(r, ms));

/**
 * Derives commission records from the current sales state.
 *
 * Design rationale:
 * - Sales are the single source of truth (posService).
 * - Commissions are computed on-read — no separate in-memory store.
 * - This guarantees commissions always reflect the latest cancellation state
 *   without synchronization overhead.
 *
 * Derivation rules per item:
 * - item.status === "cancelled"  → CommissionRecord { status: "reversed" }
 * - item.status === "active" or absent AND sale not fully cancelled → { status: "active" }
 * - sale.status === "cancelled" (all items cancelled) → all records are "reversed"
 */
function deriveRecordsFromSales(
  sales: Awaited<ReturnType<typeof posService.getRecentSales>>
): CommissionRecord[] {
  const records: CommissionRecord[] = [];

  for (const sale of sales) {
    for (const item of sale.items) {
      const isItemCancelled = item.status === "cancelled";
      const commissionRate = getCommissionRate(item.productId);
      const commissionAmount =
        Math.round(item.totalPrice * commissionRate * 100) / 100;

      const record: CommissionRecord = {
        id: `${sale.id}-${item.productId}`,
        saleId: sale.id,
        itemProductId: item.productId,
        operator: sale.operator,
        productCode: item.code,
        productDescription: item.description,
        baseAmount: item.totalPrice,
        commissionRate,
        commissionAmount,
        status: isItemCancelled ? "reversed" : "active",
        createdAt: sale.timestamp,
        reversedAt: isItemCancelled ? (item.cancelledAt ?? sale.cancelledAt) : undefined,
        reason: isItemCancelled ? (item.cancellationReason ?? sale.cancellationReason) : undefined,
      };

      records.push(record);
    }
  }

  // Most-recent sale first
  return records.sort(
    (a, b) => new Date(b.createdAt).getTime() - new Date(a.createdAt).getTime()
  );
}

function applyFilters(
  records: CommissionRecord[],
  filters: CommissionFilters
): CommissionRecord[] {
  return records.filter((r) => {
    if (filters.operator !== "all" && r.operator !== filters.operator) return false;
    if (filters.status !== "all" && r.status !== filters.status) return false;
    return true;
  });
}

export const commissionService = {
  /**
   * Returns all commission records derived from current sales state.
   * Optionally filtered.
   */
  async listCommissionRecords(
    filters?: CommissionFilters
  ): Promise<CommissionRecord[]> {
    await delay();
    const sales = await posService.getRecentSales();
    const records = deriveRecordsFromSales(sales);
    return filters ? applyFilters(records, filters) : records;
  },

  /**
   * Returns a per-operator commission summary.
   * Filtered to the same scope as listCommissionRecords when filters provided.
   */
  async getCommissionSummaryBySeller(
    filters?: CommissionFilters
  ): Promise<CommissionSummaryBySeller[]> {
    await delay();
    const sales = await posService.getRecentSales();
    // Always derive from unfiltered records then apply operator/status scope
    const all = deriveRecordsFromSales(sales);
    const scoped = filters ? applyFilters(all, filters) : all;

    const byOperator = new Map<
      string,
      { active: number; reversed: number; salesIds: Set<string>; itemCount: number }
    >();

    for (const r of scoped) {
      if (!byOperator.has(r.operator)) {
        byOperator.set(r.operator, {
          active: 0,
          reversed: 0,
          salesIds: new Set(),
          itemCount: 0,
        });
      }
      const entry = byOperator.get(r.operator)!;
      if (r.status === "active") entry.active += r.commissionAmount;
      else entry.reversed += r.commissionAmount;
      entry.salesIds.add(r.saleId);
      entry.itemCount++;
    }

    return Array.from(byOperator.entries())
      .map(([operator, data]) => ({
        operator,
        activeCommission: Math.round(data.active * 100) / 100,
        reversedCommission: Math.round(data.reversed * 100) / 100,
        netCommission: Math.round((data.active - data.reversed) * 100) / 100,
        totalSalesCount: data.salesIds.size,
        totalItemsCommissioned: data.itemCount,
      }))
      .sort((a, b) => b.activeCommission - a.activeCommission);
  },

  /**
   * Overall summary across all sellers.
   */
  async getCommissionSummaryOverall(): Promise<{
    totalActive: number;
    totalReversed: number;
    totalNet: number;
    sellersWithCommission: number;
    impactedSalesCount: number;
  }> {
    await delay(100);
    const sales = await posService.getRecentSales();
    const records = deriveRecordsFromSales(sales);

    const active = records
      .filter((r) => r.status === "active")
      .reduce((acc, r) => acc + r.commissionAmount, 0);
    const reversed = records
      .filter((r) => r.status === "reversed")
      .reduce((acc, r) => acc + r.commissionAmount, 0);
    const operators = new Set(records.map((r) => r.operator));
    const saleIds = new Set(records.map((r) => r.saleId));

    return {
      totalActive: Math.round(active * 100) / 100,
      totalReversed: Math.round(reversed * 100) / 100,
      totalNet: Math.round((active - reversed) * 100) / 100,
      sellersWithCommission: operators.size,
      impactedSalesCount: saleIds.size,
    };
  },

  /**
   * Returns all commission records for a specific sale.
   */
  async getCommissionBySaleId(saleId: string): Promise<CommissionRecord[]> {
    await delay(100);
    const sales = await posService.getRecentSales();
    const records = deriveRecordsFromSales(sales);
    return records.filter((r) => r.saleId === saleId);
  },

  /**
   * Returns unique operator names present in the commission records.
   * Used for filter dropdowns.
   */
  async listOperators(): Promise<string[]> {
    await delay(50);
    const sales = await posService.getRecentSales();
    const operators = new Set(sales.map((s) => s.operator));
    return Array.from(operators).sort();
  },
};
