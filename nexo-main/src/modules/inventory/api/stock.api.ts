import { apiClient } from "@/services/api-client";
import type { StockItemDto, StockMovementDto, AdjustStockRequest, StockPagedResponse } from "../types";

export function fetchStockItems(): Promise<StockItemDto[]> {
  return apiClient.get<StockItemDto[]>("/stock");
}

export interface StockPagedParams {
  page?: number;
  pageSize?: number;
  search?: string;
  status?: string;
}

export function fetchStockPaged(params: StockPagedParams = {}): Promise<StockPagedResponse> {
  const { page = 1, pageSize = 50, search, status } = params;
  const qs = new URLSearchParams({ page: String(page), pageSize: String(pageSize) });
  if (search) qs.set("search", search);
  if (status && status !== "all") qs.set("status", status);
  return apiClient.get<StockPagedResponse>(`/stock/paged?${qs}`);
}

export function fetchStockItem(productId: string): Promise<StockItemDto> {
  return apiClient.get<StockItemDto>(`/stock/product/${productId}`);
}

export function fetchProductMovements(productId: string): Promise<StockMovementDto[]> {
  return apiClient.get<StockMovementDto[]>(`/stock/product/${productId}/movements`);
}

export function adjustStock(request: AdjustStockRequest): Promise<StockMovementDto> {
  return apiClient.post<StockMovementDto>("/stock/adjust", request);
}
