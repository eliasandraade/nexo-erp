import { useQuery } from "@tanstack/react-query";
import { listOrders, getOrder } from "../api/restaurante.api";
import type { OrderDto } from "../types";

export const ORDERS_KEY = (storeId: string) => ["orders", storeId] as const;
export const ACTIVE_ORDER_KEY = (storeId: string, tableId: string) =>
  ["orders", "active", storeId, tableId] as const;

export function useActiveOrder(storeId: string, tableId: string) {
  return useQuery({
    queryKey: ACTIVE_ORDER_KEY(storeId, tableId),
    queryFn: async (): Promise<OrderDto | null> => {
      const orders = await listOrders();
      return (
        orders.find(
          (o) =>
            o.tableId === tableId &&
            !["Closed", "Paid", "Cancelled"].includes(o.status)
        ) ?? null
      );
    },
    enabled: !!tableId,
  });
}

export function useOrder(storeId: string, orderId: string) {
  return useQuery({
    queryKey: ["orders", storeId, orderId] as const,
    queryFn: () => getOrder(orderId),
    enabled: !!orderId,
  });
}
