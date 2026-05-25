import { apiClient } from "@/services/api-client";

export interface TopProductDto {
  productId:   string;
  productName: string;
  quantitySold: number;
  revenue:     number;
}

export interface TopSellerDto {
  sellerName:  string;
  salesCount:  number;
  revenue:     number;
}

export interface SalesByDayDto {
  date:    string;  // yyyy-MM-dd
  revenue: number;
}

export interface StockAlertDto {
  productId:    string;
  productName:  string;
  currentStock: number;
  minStock:     number;
  status:       "low" | "zero";
}

export interface DashboardSummaryDto {
  totalSales:        number;
  cancelledCount:    number;
  totalRevenue:      number;
  averageTicket:     number;
  topProducts:       TopProductDto[];
  topSellers:        TopSellerDto[];
  salesByDay:        SalesByDayDto[];
  zeroStockCount:    number;
  lowStockCount:     number;
  stockAlerts:       StockAlertDto[];
  hasOpenCashSession: boolean;
}

export function getDashboardSummary(): Promise<DashboardSummaryDto> {
  return apiClient.get<DashboardSummaryDto>("/dashboard/summary");
}
