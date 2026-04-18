import { apiClient } from "@/services/api-client";
import type {
  ApiEndpoint,
  CreateTenantInput,
  ImpersonateResult,
  PlatformHealth,
  PlatformStats,
  PlatformTenant,
  PlatformTenantDetail,
} from "../types";

// ─── Tenants ──────────────────────────────────────────────────────────────────

export async function fetchPlatformTenants(): Promise<PlatformTenant[]> {
  return apiClient.get<PlatformTenant[]>("/platform/tenants");
}

export async function fetchPlatformTenant(tenantId: string): Promise<PlatformTenantDetail> {
  return apiClient.get<PlatformTenantDetail>(`/platform/tenants/${tenantId}`);
}

export async function createPlatformTenant(input: CreateTenantInput): Promise<{ id: string; slug: string }> {
  return apiClient.post("/platform/tenants", input);
}

export async function updatePlatformTenant(
  tenantId: string,
  input: {
    companyName: string;
    tradeName?: string;
    taxId: string;
    email: string;
    phone?: string;
    businessType?: string;
  }
): Promise<void> {
  await apiClient.put(`/platform/tenants/${tenantId}`, input);
}

export async function setTenantStatus(tenantId: string, status: string): Promise<void> {
  await apiClient.put(`/platform/tenants/${tenantId}/status`, { status });
}

// ─── Modules ──────────────────────────────────────────────────────────────────

export async function grantModule(
  tenantId: string,
  moduleKey: string,
  expiresAt?: string,
  notes?: string
): Promise<void> {
  await apiClient.post(`/platform/tenants/${tenantId}/modules`, { moduleKey, expiresAt, notes });
}

export async function revokeModule(tenantId: string, moduleKey: string): Promise<void> {
  await apiClient.delete(`/platform/tenants/${tenantId}/modules/${moduleKey}`);
}

// ─── Impersonation ────────────────────────────────────────────────────────────

export async function impersonateTenant(tenantId: string): Promise<ImpersonateResult> {
  return apiClient.post<ImpersonateResult>(`/platform/tenants/${tenantId}/impersonate`, {});
}

// ─── Stats / Health / Endpoints ───────────────────────────────────────────────

export async function fetchPlatformStats(): Promise<PlatformStats> {
  return apiClient.get<PlatformStats>("/platform/stats");
}

export async function fetchPlatformHealth(): Promise<PlatformHealth> {
  return apiClient.get<PlatformHealth>("/platform/health");
}

export async function fetchApiEndpoints(): Promise<ApiEndpoint[]> {
  return apiClient.get<ApiEndpoint[]>("/platform/system/endpoints");
}
