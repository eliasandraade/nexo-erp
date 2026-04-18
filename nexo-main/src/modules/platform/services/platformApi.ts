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

// ─── Audit log ───────────────────────────────────────────────────────────────

export interface AuditLogParams {
  tenantId?: string;
  search?: string;
  severity?: string;
  actionType?: string;
  page?: number;
  pageSize?: number;
}

export interface AuditLogResponse {
  total: number;
  page: number;
  pageSize: number;
  records: {
    id: string;
    tenantId?: string;
    actionType: string;
    severity: string;
    actorId?: string;
    actorName?: string;
    actorType: string;
    entityType: string;
    entityId: string;
    description: string;
    ipAddress?: string;
    createdAt: string;
  }[];
}

export async function fetchAuditLog(params: AuditLogParams = {}): Promise<AuditLogResponse> {
  const qs = new URLSearchParams();
  if (params.tenantId)  qs.set("tenantId",  params.tenantId);
  if (params.search)    qs.set("search",    params.search);
  if (params.severity)  qs.set("severity",  params.severity);
  if (params.actionType) qs.set("actionType", params.actionType);
  if (params.page)      qs.set("page",      String(params.page));
  if (params.pageSize)  qs.set("pageSize",  String(params.pageSize));
  return apiClient.get<AuditLogResponse>(`/platform/audit?${qs}`);
}

// ─── Notes ────────────────────────────────────────────────────────────────────

export interface TenantNoteDto {
  id: string;
  content: string;
  authorName: string;
  authorId?: string;
  isPinned: boolean;
  createdAt: string;
}

export async function fetchTenantNotes(tenantId: string): Promise<TenantNoteDto[]> {
  return apiClient.get<TenantNoteDto[]>(`/platform/tenants/${tenantId}/notes`);
}

export async function createTenantNote(tenantId: string, content: string, isPinned = false): Promise<void> {
  await apiClient.post(`/platform/tenants/${tenantId}/notes`, { content, isPinned });
}

export async function deleteTenantNote(tenantId: string, noteId: string): Promise<void> {
  await apiClient.delete(`/platform/tenants/${tenantId}/notes/${noteId}`);
}

export async function toggleNotePin(tenantId: string, noteId: string): Promise<void> {
  await apiClient.patch(`/platform/tenants/${tenantId}/notes/${noteId}/pin`, {});
}

// ─── User actions ─────────────────────────────────────────────────────────────

export async function resetUserPassword(tenantId: string, userId: string, newPassword: string): Promise<void> {
  await apiClient.post(`/platform/tenants/${tenantId}/users/${userId}/reset-password`, { newPassword });
}

export async function forceLogout(tenantId: string, userId: string): Promise<void> {
  await apiClient.post(`/platform/tenants/${tenantId}/users/${userId}/force-logout`, {});
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
