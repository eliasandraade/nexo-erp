import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import {
  fetchAiProviders,
  fetchAiDashboard,
  fetchAiTelemetry,
  fetchAiCosts,
  fetchPromptVersions,
  fetchPromptTypes,
  runPlayground,
  updateAiProvider,
  setAiProviderKey,
  clearAiProviderKey,
  testAiProvider,
  setTenantAiLimits,
  createPromptVersion,
  activatePromptVersion,
} from "../services/interpreterAdminApi";
import type { AiProviderPatch, CreatePromptVersionInput, PlaygroundResult, TenantAiLimits } from "../types/aiOperations";

// ── Providers ─────────────────────────────────────────────────────────────────

const PROVIDERS_KEY = ["platform", "ai", "providers"] as const;

export function useAiProviders() {
  return useQuery({
    queryKey: PROVIDERS_KEY,
    queryFn:  fetchAiProviders,
    staleTime: 30_000,
  });
}

/** Every provider mutation changes the computed status of the others too (default, automatic order): refetch them all. */
function useProvidersMutation<TVars, TData>(fn: (vars: TVars) => Promise<TData>) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: fn,
    onSettled:  () => qc.invalidateQueries({ queryKey: PROVIDERS_KEY }),
  });
}

export function useUpdateAiProvider() {
  return useProvidersMutation(({ id, patch }: { id: string; patch: AiProviderPatch }) => updateAiProvider(id, patch));
}

export function useSetAiProviderKey() {
  return useProvidersMutation(({ id, apiKey }: { id: string; apiKey: string }) => setAiProviderKey(id, apiKey));
}

export function useClearAiProviderKey() {
  return useProvidersMutation(({ id }: { id: string }) => clearAiProviderKey(id));
}

export function useTestAiProvider() {
  return useProvidersMutation(({ id }: { id: string }) => testAiProvider(id));
}

// ── Dashboard ─────────────────────────────────────────────────────────────────

export function useAiDashboard() {
  return useQuery({
    queryKey: ["platform", "ai", "dashboard"],
    queryFn:  fetchAiDashboard,
    staleTime: 60_000,
    refetchInterval: 5 * 60_000,  // refresh every 5 minutes
  });
}

// ── Telemetry ─────────────────────────────────────────────────────────────────

export function useAiTelemetry(page: number, pageSize = 20) {
  return useQuery({
    queryKey: ["platform", "ai", "telemetry", page, pageSize],
    queryFn:  () => fetchAiTelemetry(page, pageSize),
    staleTime: 30_000,
  });
}

// ── Costs ─────────────────────────────────────────────────────────────────────

const COSTS_KEY = ["platform", "ai", "costs"] as const;

export function useAiCosts() {
  return useQuery({
    queryKey: COSTS_KEY,
    queryFn:  fetchAiCosts,
    staleTime: 60_000,
  });
}

/** Limits change the hit flags and which companies are listed: refetch the whole list. */
export function useSetTenantAiLimits() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ tenantId, limits }: { tenantId: string; limits: TenantAiLimits }) => setTenantAiLimits(tenantId, limits),
    onSuccess:  () => qc.invalidateQueries({ queryKey: COSTS_KEY }),
  });
}

// ── Prompts ───────────────────────────────────────────────────────────────────

const promptsKey = (promptType: string) => ["platform", "ai", "prompts", promptType] as const;

export function usePromptTypes() {
  return useQuery({ queryKey: ["platform", "ai", "prompt-types"] as const, queryFn: fetchPromptTypes, staleTime: 5 * 60_000 });
}

export function usePromptVersions(promptType: string) {
  return useQuery({
    queryKey: promptsKey(promptType),
    queryFn:  () => fetchPromptVersions(promptType),
    staleTime: 30_000,
  });
}

export function useCreatePromptVersion() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (input: CreatePromptVersionInput) => createPromptVersion(input),
    onSuccess:  (_data, input) => qc.invalidateQueries({ queryKey: promptsKey(input.promptType) }),
  });
}

/** Refetches on success and on failure too: a 409 means another activation won, so the list is stale. */
export function useActivatePromptVersion() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ versionId }: { promptType: string; versionId: string }) => activatePromptVersion(versionId),
    onSettled:  (_data, _error, { promptType }) => qc.invalidateQueries({ queryKey: promptsKey(promptType) }),
  });
}

// ── Playground ────────────────────────────────────────────────────────────────

/** Keeps the last result and the last error (never swallowed: the page shows message, code, reason and requestId). */
export function usePlayground() {
  const [result, setResult] = useState<PlaygroundResult | null>(null);
  const [error,  setError]  = useState<Error | null>(null);

  const mutation = useMutation({
    mutationFn: runPlayground,
    onMutate:   () => { setError(null); },
    onSuccess:  (data) => { setResult(data); setError(null); },
    onError:    (e: Error) => { setResult(null); setError(e); },
  });

  return {
    analyze:    mutation.mutate,
    isPending:  mutation.isPending,
    result,
    error,
    reset:      () => { setResult(null); setError(null); },
  };
}
