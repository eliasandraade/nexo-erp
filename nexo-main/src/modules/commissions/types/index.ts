export type CommissionStatus = "active" | "reversed";

export const commissionStatusLabels: Record<CommissionStatus, string> = {
  active: "Ativa",
  reversed: "Estornada",
};

/**
 * A commission record corresponds to a single sold item within a completed sale.
 * Records are derived dynamically from sales state — they are never stored independently.
 *
 * Status rules:
 * - active   → item is still active (sale completed, item not cancelled)
 * - reversed → item was cancelled (item-level or full-sale cancellation)
 */
export interface CommissionRecord {
  id: string;               // `${saleId}-${productId}` — stable and unique
  saleId: string;
  itemProductId: string;
  operator: string;
  productCode: string;
  productDescription: string;
  baseAmount: number;       // item.totalPrice
  commissionRate: number;   // e.g. 0.05 for 5%
  commissionAmount: number; // baseAmount × commissionRate
  status: CommissionStatus;
  createdAt: string;        // sale.timestamp
  reversedAt?: string;      // item.cancelledAt or sale.cancelledAt
  reason?: string;          // cancellation reason if reversed
}

export interface CommissionSummaryBySeller {
  operator: string;
  activeCommission: number;
  reversedCommission: number;
  netCommission: number;
  totalSalesCount: number;
  totalItemsCommissioned: number;
}

export interface CommissionFilters {
  operator: string;           // "all" or specific operator name
  status: CommissionStatus | "all";
}
