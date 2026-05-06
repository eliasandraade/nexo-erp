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
}

export interface FinanceiroSummaryDto {
  ordersCount:          number;
  revenue:              number;
  totalCostOfGoodsSold: number;
  weightedCmvPercent:   number;
  grossMargin:          number;
  totalPersonnelCost:   number;
  totalFixedExpenses:   number;
  operationalProfit:    number;
  breakEvenRevenue:     number;
  from:                 string;
  to:                   string;
}

// ── Fetch functions ───────────────────────────────────────────────────────────

export const fetchCmvReport = (): Promise<CmvReportDto> =>
  apiClient.get<CmvReportDto>("/restaurante/financeiro/cmv-report");

export const fetchFinanceiroSummary = (
  from: string,
  to:   string,
): Promise<FinanceiroSummaryDto> => {
  const p = new URLSearchParams({ from, to });
  return apiClient.get<FinanceiroSummaryDto>(`/restaurante/financeiro/summary?${p}`);
};
