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

// ─── Sessions ─────────────────────────────────────────────────────────────────

export interface UserSessionDto {
  id: string;
  ipAddress: string | null;
  userAgent: string | null;
  lastUsedAt: string;
  createdAt: string;
  expiresAt: string;
}

export async function fetchUserSessions(tenantId: string, userId: string): Promise<UserSessionDto[]> {
  return apiClient.get<UserSessionDto[]>(`/platform/tenants/${tenantId}/users/${userId}/sessions`);
}

export async function revokeAllSessions(tenantId: string, userId: string): Promise<void> {
  await apiClient.delete(`/platform/tenants/${tenantId}/users/${userId}/sessions`);
}

// ─── Plan history ─────────────────────────────────────────────────────────────

export interface PlanHistoryEvent {
  id: string;
  moduleKey: string;
  eventType: "granted" | "revoked" | "renewed" | "plan_changed";
  planType: string | null;
  periodEnd: string | null;
  notes: string | null;
  actorId: string | null;
  createdAt: string;
}

export async function fetchPlanHistory(tenantId: string): Promise<PlanHistoryEvent[]> {
  return apiClient.get<PlanHistoryEvent[]>(`/platform/tenants/${tenantId}/plan-history`);
}

// ─── MRR / ARR ────────────────────────────────────────────────────────────────

export interface MrrData {
  mrr: number;
  arr: number;
  activeSubscriptions: number;
  payingSubscriptions: number;
  nonPayingSubscriptions: number;
  byModule: { moduleKey: string; mrr: number }[];
}

export async function fetchMrr(): Promise<MrrData> {
  return apiClient.get<MrrData>("/platform/mrr");
}

// ─── Churn ────────────────────────────────────────────────────────────────────

export interface ChurnData {
  period: number;
  canceledSubscriptions: number;
  activeSubscriptions: number;
  churnRate: number;
  previousPeriodCanceled: number;
  trend: number;
}

export async function fetchChurn(period = 30): Promise<ChurnData> {
  return apiClient.get<ChurnData>(`/platform/churn?period=${period}`);
}

// ─── Trial expired ────────────────────────────────────────────────────────────

export interface TrialExpiredTenant {
  id: string;
  companyName: string;
  tradeName: string | null;
  email: string;
  status: string;
  trialEndsAt: string | null;
  createdAt: string;
  expiredDaysAgo: number;
  expiredReason: string;
}

export async function fetchTrialExpired(): Promise<TrialExpiredTenant[]> {
  return apiClient.get<TrialExpiredTenant[]>("/platform/tenants/trial-expired");
}

// ─── Feature Flags ────────────────────────────────────────────────────────────

export interface FeatureFlagDto {
  key: string;
  name: string;
  description: string | null;
  defaultEnabled: boolean;
  category: string;
  overrideCount: number;
  /** Resolved value for a specific tenant (present only when ?tenantId= was sent) */
  tenantValue: boolean | null;
  hasOverride: boolean;
  updatedAt: string;
}

export interface TenantFlagResolved {
  key: string;
  name: string;
  category: string;
  defaultEnabled: boolean;
  resolved: boolean;
  hasOverride: boolean;
  overrideValue: boolean | null;
  notes: string | null;
}

export interface FlagOverride {
  tenantId: string;
  tenantName: string;
  isEnabled: boolean;
  notes: string | null;
  updatedAt: string;
}

export async function fetchFlags(tenantId?: string): Promise<FeatureFlagDto[]> {
  const qs = tenantId ? `?tenantId=${tenantId}` : "";
  return apiClient.get<FeatureFlagDto[]>(`/platform/flags${qs}`);
}

export async function createFlag(input: {
  key: string; name: string; description?: string;
  defaultEnabled: boolean; category: string;
}): Promise<void> {
  await apiClient.post("/platform/flags", input);
}

export async function updateFlag(key: string, input: {
  name: string; description?: string; defaultEnabled: boolean; category: string;
}): Promise<void> {
  await apiClient.put(`/platform/flags/${key}`, input);
}

export async function toggleFlagDefault(key: string): Promise<{ key: string; defaultEnabled: boolean }> {
  return apiClient.patch<{ key: string; defaultEnabled: boolean }>(`/platform/flags/${key}/toggle`, {});
}

export async function fetchTenantFlags(tenantId: string): Promise<TenantFlagResolved[]> {
  return apiClient.get<TenantFlagResolved[]>(`/platform/tenants/${tenantId}/flags`);
}

export async function setTenantFlagOverride(
  tenantId: string, key: string, isEnabled: boolean, notes?: string
): Promise<void> {
  await apiClient.post(`/platform/tenants/${tenantId}/flags/${key}`, { isEnabled, notes });
}

export async function deleteTenantFlagOverride(tenantId: string, key: string): Promise<void> {
  await apiClient.delete(`/platform/tenants/${tenantId}/flags/${key}`);
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
