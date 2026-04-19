import { apiClient } from "@/services/api-client";
import type {
  AreaDto,
  TableDto,
  OrderDto,
  OpenOrderRequest,
  AddOrderItemRequest,
  PayOrderRequest,
  FoodServiceSettingsDto,
  UpdateFoodServiceSettingsRequest,
  ModifierGroupDto,
} from "../types";

// ── Settings ──────────────────────────────────────────────────────────────────
export const getFoodSettings = (): Promise<FoodServiceSettingsDto> =>
  apiClient.get<FoodServiceSettingsDto>("/restaurante/settings");

export const updateFoodSettings = (
  req: UpdateFoodServiceSettingsRequest
): Promise<FoodServiceSettingsDto> =>
  apiClient.put<FoodServiceSettingsDto>("/restaurante/settings", req);

// ── Areas ─────────────────────────────────────────────────────────────────────
export const listAreas = (includeInactive = false): Promise<AreaDto[]> =>
  apiClient.get<AreaDto[]>(`/restaurante/areas?includeInactive=${includeInactive}`);

export const createArea = (req: { name: string; description?: string }): Promise<AreaDto> =>
  apiClient.post<AreaDto>("/restaurante/areas", req);

export const updateArea = (id: string, req: { name: string; description?: string; isActive: boolean }): Promise<AreaDto> =>
  apiClient.put<AreaDto>(`/restaurante/areas/${id}`, req);

// ── Tables ────────────────────────────────────────────────────────────────────
export const listTables = (includeInactive = false): Promise<TableDto[]> =>
  apiClient.get<TableDto[]>(`/restaurante/tables?includeInactive=${includeInactive}`);

export const createTable = (req: { areaId: string; number: string; capacity: number }): Promise<TableDto> =>
  apiClient.post<TableDto>("/restaurante/tables", req);

export const updateTable = (id: string, req: { areaId: string; number: string; capacity: number; isActive: boolean }): Promise<TableDto> =>
  apiClient.put<TableDto>(`/restaurante/tables/${id}`, req);

export const getTableOrders = (tableId: string): Promise<OrderDto[]> =>
  apiClient.get<OrderDto[]>(`/restaurante/tables/${tableId}/orders`);

// ── Orders ────────────────────────────────────────────────────────────────────
export const listOrders = (): Promise<OrderDto[]> =>
  apiClient.get<OrderDto[]>("/restaurante/orders");

export const getOrder = (orderId: string): Promise<OrderDto> =>
  apiClient.get<OrderDto>(`/restaurante/orders/${orderId}`);

export const openOrder = (req: OpenOrderRequest): Promise<OrderDto> =>
  apiClient.post<OrderDto>("/restaurante/orders", req);

export const addOrderItem = (
  orderId: string,
  req: AddOrderItemRequest
): Promise<OrderDto> =>
  apiClient.post<OrderDto>(`/restaurante/orders/${orderId}/items`, req);

export const updateItemStatus = (
  orderId: string,
  itemId: string,
  status: string
): Promise<OrderDto> =>
  apiClient.patch<OrderDto>(
    `/restaurante/orders/${orderId}/items/${itemId}/status`,
    { status }
  );

export const closeOrder = (
  orderId: string
): Promise<{ orderId: string; saleId: string; total: number; message: string }> =>
  apiClient.post<{ orderId: string; saleId: string; total: number; message: string }>(
    `/restaurante/orders/${orderId}/close`,
    {}
  );

export const payOrder = (
  orderId: string,
  req: PayOrderRequest
): Promise<OrderDto> =>
  apiClient.post<OrderDto>(`/restaurante/orders/${orderId}/pay`, req);

export const cancelOrder = (orderId: string): Promise<OrderDto> =>
  apiClient.post<OrderDto>(`/restaurante/orders/${orderId}/cancel`, {});

// ── Modifiers ─────────────────────────────────────────────────────────────────
export const getModifierGroups = (productId: string): Promise<ModifierGroupDto[]> =>
  apiClient.get<ModifierGroupDto[]>(
    `/restaurante/modifier-groups?productId=${productId}`
  );

// ── Kitchen (polling path) ────────────────────────────────────────────────────
export const listKitchenOrders = (): Promise<OrderDto[]> =>
  apiClient.get<OrderDto[]>("/restaurante/orders");

// ── Reports ───────────────────────────────────────────────────────────────────

export interface RestauranteSummaryDto {
  ordersCount:         number;
  revenue:             number;
  averageTicket:       number;
  averageTableMinutes: number;
  from:                string;
  to:                  string;
}

export function fetchRestauranteSummary(
  from: string,
  to: string
): Promise<RestauranteSummaryDto> {
  return apiClient.get<RestauranteSummaryDto>(
    `/restaurante/reports/summary?from=${from}&to=${to}`
  );
}

