import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  createPlatformTenant,
  fetchPlatformTenant,
  fetchPlatformTenants,
  grantModule,
  impersonateTenant,
  revokeModule,
  setTenantStatus,
  updatePlatformTenant,
} from "../services/platformApi";
import type { CreateTenantInput } from "../types";

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
  });
}

export function useSetTenantStatus(tenantId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (status: string) => setTenantStatus(tenantId, status),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["platform", "tenants"] });
      qc.invalidateQueries({ queryKey: ["platform", "tenants", tenantId] });
    },
  });
}

export function useGrantModule(tenantId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ moduleKey, notes }: { moduleKey: string; notes?: string }) =>
      grantModule(tenantId, moduleKey, undefined, notes),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["platform", "tenants", tenantId] }),
  });
}

export function useRevokeModule(tenantId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (moduleKey: string) => revokeModule(tenantId, moduleKey),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["platform", "tenants", tenantId] }),
  });
}

export function useImpersonate() {
  return useMutation({
    mutationFn: (tenantId: string) => impersonateTenant(tenantId),
  });
}
