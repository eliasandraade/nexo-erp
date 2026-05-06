import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { getFoodSettings, updateOperationalCosts } from "../api/restaurante.api";
import type { UpdateOperationalCostsRequest } from "../types";

export const FOOD_SETTINGS_KEY = (storeId: string) =>
  ["food-settings", storeId] as const;

export function useFoodSettings(storeId: string) {
  return useQuery({
    queryKey: FOOD_SETTINGS_KEY(storeId),
    queryFn: getFoodSettings,
    staleTime: 60_000,
  });
}

export function useUpdateOperationalCosts(storeId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (req: UpdateOperationalCostsRequest) => updateOperationalCosts(req),
    onSuccess: (data) => qc.setQueryData(FOOD_SETTINGS_KEY(storeId), data),
  });
}
