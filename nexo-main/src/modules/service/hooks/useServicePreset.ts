import { useQuery } from "@tanstack/react-query";
import { getServicePreset, type ServicePresetDto } from "../api/service.api";

/**
 * Query-key factory for the whole Service module. Centralised so the stacked PRs
 * (cadastros / agenda / OS / pacotes / pagamentos) invalidate against a single source.
 */
export const serviceKeys = {
  all: ["service"] as const,
  preset: () => [...serviceKeys.all, "preset"] as const,
  settings: () => [...serviceKeys.all, "settings"] as const,
};

/**
 * Fetches the active Service preset (labels + capability flags) for the tenant.
 * `enabled` gates the call so non-Service tenants never hit the endpoint.
 */
export function useServicePresetQuery(enabled: boolean) {
  return useQuery<ServicePresetDto>({
    queryKey: serviceKeys.preset(),
    queryFn: getServicePreset,
    enabled,
    staleTime: 5 * 60 * 1000, // preset is stable within a session
  });
}
