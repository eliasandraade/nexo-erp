import { useQuery } from "@tanstack/react-query";
import { fetchMyStores } from "../services/storesApi";
import type { StoreDto } from "../types";

export function useMyStores() {
  return useQuery<StoreDto[]>({
    queryKey: ["stores", "mine"],
    queryFn: fetchMyStores,
    staleTime: 5 * 60 * 1000,
  });
}
