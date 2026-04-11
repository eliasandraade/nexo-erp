import { apiClient } from "@/services/api-client";
import type { PlatformTenant, PlatformTenantDetail } from "../types";

export async function fetchPlatformTenants(): Promise<PlatformTenant[]> {
  return apiClient.get<PlatformTenant[]>("/platform/tenants");
}

export async function fetchPlatformTenant(tenantId: string): Promise<PlatformTenantDetail> {
  return apiClient.get<PlatformTenantDetail>(`/platform/tenants/${tenantId}`);
}
