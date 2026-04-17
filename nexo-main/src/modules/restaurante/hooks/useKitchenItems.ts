// nexo-main/src/modules/restaurante/hooks/useKitchenItems.ts
import { useQuery } from "@tanstack/react-query";
import { listKitchenOrders } from "../api/restaurante.api";
import type { KitchenItem } from "../types";

const KITCHEN_STATUSES = new Set(["Pending", "Preparing", "Ready"]);

export const KITCHEN_KEY = (storeId: string) =>
  ["kitchen-items", storeId] as const;

export function useKitchenItems(storeId: string, refetchInterval?: number) {
  return useQuery({
    queryKey: KITCHEN_KEY(storeId),
    queryFn: async (): Promise<KitchenItem[]> => {
      const orders = await listKitchenOrders();
      const items: KitchenItem[] = [];
      for (const order of orders) {
        for (const item of order.items) {
          if (!KITCHEN_STATUSES.has(item.status)) continue;
          items.push({
            orderId:         order.id,
            orderNumber:     order.orderNumber,
            tableNumber:     order.tableNumber,
            orderType:       order.orderType,
            itemId:          item.id,
            productName:     item.productName,
            quantity:        item.quantity,
            notes:           item.notes,
            modifiers:       item.modifiers,
            status:          item.status,
            sentToKitchenAt: item.sentToKitchenAt,
          });
        }
      }
      return items;
    },
    refetchInterval,
    staleTime: 0,
  });
}
