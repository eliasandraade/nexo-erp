import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { fetchTenantBuildSettings, updateTenantBuildSettings, type UpdatePlatformBuildSettings } from "../services/platformApi";

export const buildSettingsKey = (tenantId: string) => ["platform", "tenants", tenantId, "build-settings"] as const;

export function useTenantBuildSettings(tenantId: string) {
  return useQuery({ queryKey: buildSettingsKey(tenantId), queryFn: () => fetchTenantBuildSettings(tenantId), enabled: !!tenantId });
}

export function useUpdateTenantBuildSettings(tenantId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (req: UpdatePlatformBuildSettings) => updateTenantBuildSettings(tenantId, req),
    onSuccess: (data) => qc.setQueryData(buildSettingsKey(tenantId), data),
  });
}
