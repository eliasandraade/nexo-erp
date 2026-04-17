import { useQuery } from "@tanstack/react-query";
import { getModifierGroups } from "../api/restaurante.api";

export function useModifierGroups(productId: string | null) {
  return useQuery({
    queryKey: ["modifier-groups", productId] as const,
    queryFn: () => getModifierGroups(productId!),
    enabled: !!productId,
    staleTime: 30_000,
  });
}
