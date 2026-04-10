import type { SaleStatus, PaymentMethod } from "@/modules/sales/types";

export interface ReportFilters {
  operator: string;                    // "all" or specific operator name
  status: SaleStatus | "all";
  paymentMethod: PaymentMethod | "all";
}

export interface SalesOperationalSummary {
  totalSales: number;
  totalRevenue: number;
  averageTicket: number;
  cancelledCount: number;
  partiallyCancelledCount: number;
  cancelledValue: number;
}

export interface SalesByOperatorRow {
  operator: string;
  salesCount: number;
  totalRevenue: number;
  cancelledCount: number;
  averageTicket: number;
}

export interface TopProductRow {
  productCode: string;
  productDescription: string;
  quantitySold: number;
  revenueGenerated: number;
}

export interface CancellationSummary {
  cancelledSalesCount: number;
  partiallyCancelledCount: number;
  cancelledItemsCount: number;
  totalReversedValue: number;
}

export interface CommissionReportSummary {
  totalActive: number;
  totalReversed: number;
  topSellers: Array<{ operator: string; activeCommission: number }>;
}

export interface CashReportSummary {
  currentSession: {
    isOpen: boolean;
    operator?: string;
    openedAt?: string;
    expectedBalance: number;
  };
  totalSalesThisSession: number;
  totalWithdrawalsThisSession: number;
  totalReinforcementsThisSession: number;
  closedSessionsWithDivergence: number;
}

export interface InventoryReportSummary {
  zeroStockCount: number;
  lowStockCount: number;
  normalCount: number;
  highStockCount: number;
  totalAlertCount: number;
}
