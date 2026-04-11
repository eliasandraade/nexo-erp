import { useQuery } from "@tanstack/react-query";
import { fetchPlatformTenants, fetchPlatformTenant } from "../services/platformApi";
import type { PlatformTenant, PlatformTenantDetail } from "../types";

export function usePlatformTenants() {
  return useQuery<PlatformTenant[]>({
    queryKey: ["platform", "tenants"],
    queryFn: fetchPlatformTenants,
    staleTime: 30_000,
  });
}

export function usePlatformTenant(tenantId: string) {
  return useQuery<PlatformTenantDetail>({
    queryKey: ["platform", "tenants", tenantId],
    queryFn: () => fetchPlatformTenant(tenantId),
    enabled: !!tenantId,
  });
}
