import { useQuery } from "@tanstack/react-query";
import { getFoodSettings } from "../api/restaurante.api";

export const FOOD_SETTINGS_KEY = (storeId: string) =>
  ["food-settings", storeId] as const;

export function useFoodSettings(storeId: string) {
  return useQuery({
    queryKey: FOOD_SETTINGS_KEY(storeId),
    queryFn: getFoodSettings,
    staleTime: 60_000,
  });
}
