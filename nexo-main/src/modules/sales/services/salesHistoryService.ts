import { posService } from "./posService";
import type { CompletedSale, SaleListFilters } from "../types";

const delay = (ms = 400) => new Promise((r) => setTimeout(r, ms));

function applyFilters(sales: CompletedSale[], filters: Partial<SaleListFilters>): CompletedSale[] {
  return sales.filter((sale) => {
    if (filters.search && filters.search.trim()) {
      const q = filters.search.trim().toLowerCase();
      const matchesId = sale.id.toLowerCase().includes(q);
      const matchesOperator = sale.operator.toLowerCase().includes(q);
      const matchesItem = sale.items.some(
        (i) =>
          i.description.toLowerCase().includes(q) ||
          i.code.toLowerCase().includes(q)
      );
      const matchesCustomer = sale.customerName
        ? sale.customerName.toLowerCase().includes(q)
        : false;
      if (!matchesId && !matchesOperator && !matchesItem && !matchesCustomer) return false;
    }

    if (filters.paymentMethod && filters.paymentMethod !== "all") {
      if (!sale.payments.some((p) => p.method === filters.paymentMethod)) return false;
    }

    if (filters.status && filters.status !== "all") {
      if (sale.status !== filters.status) return false;
    }

    return true;
  });
}

export const salesHistoryService = {
  /**
   * Returns all sales, optionally filtered.
   * posService is the authoritative source of truth — no data duplication.
   */
  async listSales(filters?: Partial<SaleListFilters>): Promise<CompletedSale[]> {
    const sales = await posService.getRecentSales();
    if (!filters) return sales;
    return applyFilters(sales, filters);
  },

  /**
   * Returns a single sale by ID. Returns undefined if not found.
   */
  async getSaleById(id: string): Promise<CompletedSale | undefined> {
    await delay(200);
    return posService.getSaleById(id);
  },
};
