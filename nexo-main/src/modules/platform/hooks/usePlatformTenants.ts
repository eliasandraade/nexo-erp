import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import {
  createPlatformTenant,
  fetchPlatformTenant,
  fetchPlatformTenants,
  grantModule,
  impersonateTenant,
  revokeModule,
  setTenantStatus,
  updatePlatformTenant,
  fetchTenantNotes,
  createTenantNote,
  deleteTenantNote,
  toggleNotePin,
  resetUserPassword,
  forceLogout,
  fetchUserSessions,
  revokeAllSessions,
  fetchTrialExpired,
  fetchPlanHistory,
  fetchMrr,
  fetchChurn,
} from "../services/platformApi";
import { platformErrorMessage, toastPlatformError } from "../services/platformErrors";
import type { CreateTenantInput } from "../types";

/** Consistent error message for failed platform mutations (403 explains the two-step requirement). */
const errMsg = (e: unknown) => platformErrorMessage(e);

// ─── Queries ──────────────────────────────────────────────────────────────────

export function usePlatformTenants() {
  return useQuery({
    queryKey: ["platform", "tenants"],
    queryFn: fetchPlatformTenants,
    staleTime: 30_000,
  });
}

export function usePlatformTenant(tenantId: string) {
  return useQuery({
    queryKey: ["platform", "tenants", tenantId],
    queryFn: () => fetchPlatformTenant(tenantId),
    enabled: !!tenantId,
  });
}

// ─── Mutations ────────────────────────────────────────────────────────────────

export function useCreateTenant() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (input: CreateTenantInput) => createPlatformTenant(input),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["platform", "tenants"] }),
  });
}

export function useUpdateTenant(tenantId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (input: Parameters<typeof updatePlatformTenant>[1]) =>
      updatePlatformTenant(tenantId, input),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["platform", "tenants"] });
      qc.invalidateQueries({ queryKey: ["platform", "tenants", tenantId] });
    },
    onError: toastPlatformError,
  });
}

export function useSetTenantStatus(tenantId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (status: string) => setTenantStatus(tenantId, status),
    onSuccess: (_, status) => {
      qc.invalidateQueries({ queryKey: ["platform", "tenants"] });
      qc.invalidateQueries({ queryKey: ["platform", "tenants", tenantId] });
      toast.success(status === "Suspended" ? "Cliente suspenso." : "Cliente reativado.");
    },
    onError: (e) => toast.error(errMsg(e)),
  });
}

export function useGrantModule(tenantId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ moduleKey, notes }: { moduleKey: string; notes?: string }) =>
      grantModule(tenantId, moduleKey, undefined, notes),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["platform", "tenants", tenantId] });
      toast.success("Módulo ativado.");
    },
    onError: (e) => toast.error(errMsg(e)),
  });
}

export function useRevokeModule(tenantId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (moduleKey: string) => revokeModule(tenantId, moduleKey),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["platform", "tenants", tenantId] });
      toast.success("Módulo revogado.");
    },
    onError: (e) => toast.error(errMsg(e)),
  });
}

export function useImpersonate() {
  return useMutation({
    mutationFn: ({ tenantId, reason }: { tenantId: string; reason: string }) => impersonateTenant(tenantId, reason),
  });
}

// ─── Notes ────────────────────────────────────────────────────────────────────

export function useTenantNotes(tenantId: string) {
  return useQuery({
    queryKey: ["platform", "tenants", tenantId, "notes"],
    queryFn: () => fetchTenantNotes(tenantId),
    enabled: !!tenantId,
  });
}

export function useCreateNote(tenantId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ content, isPinned }: { content: string; isPinned: boolean }) =>
      createTenantNote(tenantId, content, isPinned),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["platform", "tenants", tenantId, "notes"] }),
    onError: toastPlatformError,
  });
}

export function useDeleteNote(tenantId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (noteId: string) => deleteTenantNote(tenantId, noteId),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["platform", "tenants", tenantId, "notes"] }),
    onError: toastPlatformError,
  });
}

export function useToggleNotePin(tenantId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (noteId: string) => toggleNotePin(tenantId, noteId),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["platform", "tenants", tenantId, "notes"] }),
    onError: toastPlatformError,
  });
}

// ─── User actions ─────────────────────────────────────────────────────────────

export function useResetUserPassword(tenantId: string) {
  return useMutation({
    mutationFn: ({ userId, newPassword }: { userId: string; newPassword: string }) =>
      resetUserPassword(tenantId, userId, newPassword),
    onSuccess: () => toast.success("Senha redefinida. O usuário deverá trocá-la no próximo login."),
    onError: (e) => toast.error(errMsg(e)),
  });
}

export function useForceLogout(tenantId: string) {
  return useMutation({
    mutationFn: (userId: string) => forceLogout(tenantId, userId),
    onError: toastPlatformError,
  });
}

// ─── Sessions ─────────────────────────────────────────────────────────────────

export function useUserSessions(tenantId: string, userId: string, enabled: boolean) {
  return useQuery({
    queryKey: ["platform", "tenants", tenantId, "users", userId, "sessions"],
    queryFn: () => fetchUserSessions(tenantId, userId),
    enabled: enabled && !!tenantId && !!userId,
    staleTime: 30_000,
  });
}

export function useRevokeAllSessions(tenantId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (userId: string) => revokeAllSessions(tenantId, userId),
    onSuccess: (_, userId) => {
      qc.invalidateQueries({ queryKey: ["platform", "tenants", tenantId, "users", userId, "sessions"] });
      toast.success("Sessões revogadas. O usuário foi desconectado.");
    },
    onError: (e) => toast.error(errMsg(e)),
  });
}

// ─── Trial expired ────────────────────────────────────────────────────────────

export function useTrialExpired() {
  return useQuery({
    queryKey: ["platform", "trial-expired"],
    queryFn: fetchTrialExpired,
    staleTime: 60_000,
  });
}

// ─── Plan history ─────────────────────────────────────────────────────────────

export function usePlanHistory(tenantId: string) {
  return useQuery({
    queryKey: ["platform", "tenants", tenantId, "plan-history"],
    queryFn: () => fetchPlanHistory(tenantId),
    enabled: !!tenantId,
    staleTime: 30_000,
  });
}

// ─── MRR / ARR ────────────────────────────────────────────────────────────────

export function useMrr() {
  return useQuery({
    queryKey: ["platform", "mrr"],
    queryFn: fetchMrr,
    staleTime: 60_000,
  });
}

// ─── Churn ────────────────────────────────────────────────────────────────────

export function useChurn(period = 30) {
  return useQuery({
    queryKey: ["platform", "churn", period],
    queryFn: () => fetchChurn(period),
    staleTime: 60_000,
  });
}

