import { useQuery } from "@tanstack/react-query";
import { listDeliveryOrders } from "../api/restaurante.api";

export const DELIVERY_KEY = (storeId: string) =>
  ["delivery-orders", storeId] as const;

export function useDeliveryOrders(storeId: string) {
  return useQuery({
    queryKey: DELIVERY_KEY(storeId),
    queryFn: () => listDeliveryOrders(),
    staleTime: 0,
    refetchInterval: 15_000,
    enabled: !!storeId,
  });
}
