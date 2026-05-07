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
  rotateAiProviderKey,
  setActivePrompt,
} from "../services/interpreterAdminApi";
import type { PlaygroundResult } from "../types/aiOperations";

// ── Providers ─────────────────────────────────────────────────────────────────

export function useAiProviders() {
  return useQuery({
    queryKey: ["platform", "ai", "providers"],
    queryFn:  fetchAiProviders,
    staleTime: 30_000,
  });
}

export function useUpdateAiProvider() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ id, patch }: { id: string; patch: Parameters<typeof updateAiProvider>[1] }) =>
      updateAiProvider(id, patch),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["platform", "ai", "providers"] }),
  });
}

export function useRotateAiProviderKey() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ id, apiKey }: { id: string; apiKey?: string }) =>
      rotateAiProviderKey(id, apiKey),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["platform", "ai", "providers"] }),
  });
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

export function usePlayground() {
  const [result, setResult] = useState<PlaygroundResult | null>(null);
  const [error,  setError]  = useState<string | null>(null);

  const mutation = useMutation({
    mutationFn: runPlayground,
    onSuccess:  (data) => { setResult(data); setError(null); },
    onError:    (e: Error) => { setError(e.message); },
  });

  return {
    analyze:    mutation.mutate,
    isPending:  mutation.isPending,
    result,
    error,
    reset:      () => { setResult(null); setError(null); },
  };
}
