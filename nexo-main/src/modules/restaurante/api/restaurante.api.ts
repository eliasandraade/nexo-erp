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
export const listAreas = (): Promise<AreaDto[]> =>
  apiClient.get<AreaDto[]>("/restaurante/areas");

// ── Tables ────────────────────────────────────────────────────────────────────
export const listTables = (): Promise<TableDto[]> =>
  apiClient.get<TableDto[]>("/restaurante/tables");

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
