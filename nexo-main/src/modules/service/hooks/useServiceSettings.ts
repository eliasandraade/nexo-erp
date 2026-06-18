import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { getServiceSettings, setServicePreset, type ServiceSettingsDto } from "../api/service.api";
import { serviceKeys } from "./useServicePreset";

/**
 * Per-store Service configuration. `enabled` gates it so non-Service tenants never call it.
 * `isConfigured: false` ⇒ the store hasn't chosen a vertical yet → onboarding.
 */
export function useServiceSettings(enabled: boolean) {
  return useQuery<ServiceSettingsDto>({
    queryKey: serviceKeys.settings(),
    queryFn: getServiceSettings,
    enabled,
    staleTime: 5 * 60 * 1000,
  });
}

/** Chooses (or changes) the store's preset, then refreshes settings + the resolved preset. */
export function useSetServicePreset() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (presetKey: string) => setServicePreset(presetKey),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: serviceKeys.settings() });
      qc.invalidateQueries({ queryKey: serviceKeys.preset() });
    },
  });
}
