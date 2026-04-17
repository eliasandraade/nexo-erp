import { useQuery } from "@tanstack/react-query";
import { listAreas } from "../api/restaurante.api";

export const AREAS_KEY = (storeId: string) => ["areas", storeId] as const;

export function useRestauranteAreas(storeId: string) {
  return useQuery({
    queryKey: AREAS_KEY(storeId),
    queryFn: listAreas,
    staleTime: 60_000,
  });
}
