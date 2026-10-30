import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import {
  fetchAiProviders,
  fetchAiDashboard,
  fetchAiTelemetry,
  fetchAiCosts,
  fetchPromptVersions,
  runPlayground,
  updateAiProvider,
  setAiProviderKey,
  clearAiProviderKey,
  testAiProvider,
  setActivePrompt,
} from "../services/interpreterAdminApi";
import type { AiProviderPatch, PlaygroundResult } from "../types/aiOperations";

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

export function useAiCosts() {
  return useQuery({
    queryKey: ["platform", "ai", "costs"],
    queryFn:  fetchAiCosts,
    staleTime: 60_000,
  });
}

// ── Prompts ───────────────────────────────────────────────────────────────────

export function usePromptVersions(promptType: string) {
  return useQuery({
    queryKey: ["platform", "ai", "prompts", promptType],
    queryFn:  () => fetchPromptVersions(promptType),
    staleTime: 30_000,
  });
}

export function useSetActivePrompt() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ versionId }: { promptType: string; versionId: string }) => setActivePrompt(versionId),
    onSuccess:  (_data, { promptType }) =>
      qc.invalidateQueries({ queryKey: ["platform", "ai", "prompts", promptType] }),
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
