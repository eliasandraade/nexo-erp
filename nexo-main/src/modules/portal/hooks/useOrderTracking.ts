import { useQuery } from "@tanstack/react-query";
import { trackOrder } from "../api/portal.api";

export function useOrderTracking(token: string) {
  return useQuery({
    queryKey: ["order-tracking", token],
    queryFn:  () => trackOrder(token),
    refetchInterval: 30_000,
    staleTime: 0,
    retry: false,
  });
}
