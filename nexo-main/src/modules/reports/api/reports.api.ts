import { apiClient } from "@/services/api-client";

export interface SalesReportDto {
  totalSales:     number;
  cancelledSales: number;
  totalRevenue:   number;
  averageTicket:  number;
  cancelledValue: number;
  from:           string;
  to:             string;
}

export interface InventoryReportDto {
  totalProducts:   number;
  zeroStockCount:  number;
  lowStockCount:   number;
  normalCount:     number;
  totalStockValue: number;
  alertCount:      number;
}

export interface CustomerReportDto {
  totalCustomers:       number;
  newThisMonth:         number;
  withPurchases:        number;
  averagePurchaseValue: number;
}

export const fetchSalesReport = (from?: string, to?: string): Promise<SalesReportDto> => {
  const params = new URLSearchParams();
  if (from) params.set("from", from);
  if (to)   params.set("to", to);
  const qs = params.size > 0 ? `?${params}` : "";
  return apiClient.get<SalesReportDto>(`/reports/sales${qs}`);
};

export const fetchInventoryReport = (): Promise<InventoryReportDto> =>
  apiClient.get<InventoryReportDto>("/reports/inventory");

export const fetchCustomerReport = (): Promise<CustomerReportDto> =>
  apiClient.get<CustomerReportDto>("/reports/customers");
