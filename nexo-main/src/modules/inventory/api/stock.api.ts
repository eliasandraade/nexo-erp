import { apiClient } from "@/services/api-client";
import type { StockItemDto, StockMovementDto, AdjustStockRequest } from "../types";

export function fetchStockItems(): Promise<StockItemDto[]> {
  return apiClient.get<StockItemDto[]>("/stock");
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
