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

  professionals: () => [...serviceKeys.all, "professionals"] as const,
  professionalsList: (onlyActive: boolean) => [...serviceKeys.professionals(), { onlyActive }] as const,
  professional: (id: string) => [...serviceKeys.professionals(), id] as const,

  catalog: () => [...serviceKeys.all, "catalog"] as const,
  catalogList: (onlyActive: boolean) => [...serviceKeys.catalog(), { onlyActive }] as const,
  catalogItem: (id: string) => [...serviceKeys.catalog(), id] as const,

  subjects: () => [...serviceKeys.all, "subjects"] as const,
  subjectsList: (params: Record<string, unknown>) => [...serviceKeys.subjects(), params] as const,
  subject: (id: string) => [...serviceKeys.subjects(), id] as const,

  appointments: () => [...serviceKeys.all, "appointments"] as const,
  appointmentsList: (params: Record<string, unknown>) => [...serviceKeys.appointments(), params] as const,
  appointment: (id: string) => [...serviceKeys.appointments(), id] as const,

  records: (contextType: string, contextId: string) =>
    [...serviceKeys.all, "records", contextType, contextId] as const,
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
