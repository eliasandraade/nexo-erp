import { apiClient } from "@/services/api-client";
import { downloadFile } from "@/services/pdf.api";

export interface SalesReportDayDto { date: string; sales: number; revenue: number }
export interface SalesReportProductDto { productId: string; productName: string; quantity: number; revenue: number }
export interface SalesReportSellerDto { sellerName: string; sales: number; revenue: number }
export interface SalesReportPaymentDto { method: string; sales: number; amount: number }

/** Sales of a period (Brazil days). totalRevenue is net of returns; grossRevenue − returnedValue = totalRevenue. */
export interface SalesReportDto {
  totalSales: number;
  cancelledSales: number;
  totalRevenue: number;
  averageTicket: number;
  cancelledValue: number;
  from: string;
  to: string;
  grossRevenue: number;
  returnedValue: number;
  returnedSales: number;
  discountValue: number;
  days: SalesReportDayDto[];
  topProducts: SalesReportProductDto[];
  sellers: SalesReportSellerDto[];
  payments: SalesReportPaymentDto[];
}

export interface InventoryReportDto {
  totalProducts: number;
  zeroStockCount: number;
  lowStockCount: number;
  normalCount: number;
  totalStockValue: number;
  alertCount: number;
}

export interface CustomerReportDto {
  totalCustomers: number;
  newThisMonth: number;
  withPurchases: number;
  averagePurchaseValue: number;
}

export const REPORTS_KEY = ["reports"] as const;

const range = (from: string, to: string) => `from=${from}&to=${to}`;

export const getSalesReport = (from: string, to: string) =>
  apiClient.get<SalesReportDto>(`/reports/sales?${range(from, to)}`);

export const getInventoryReport = () => apiClient.get<InventoryReportDto>("/reports/inventory");

export const getCustomerReport = () => apiClient.get<CustomerReportDto>("/reports/customers");

export const exportSales = (from: string, to: string) =>
  downloadFile(`/reports/sales/export?${range(from, to)}`, `vendas_${from}_a_${to}.csv`, "Falha ao exportar as vendas");
