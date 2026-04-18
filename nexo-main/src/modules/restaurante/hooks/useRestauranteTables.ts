import { useQuery } from "@tanstack/react-query";
import { listTables } from "../api/restaurante.api";

export const TABLES_KEY = (storeId: string) => ["tables", storeId] as const;

export function useRestauranteTables(storeId: string) {
  return useQuery({
    queryKey: TABLES_KEY(storeId),
    queryFn: () => listTables(false),
    staleTime: 10_000,
  });
}
