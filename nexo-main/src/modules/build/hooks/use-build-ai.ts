import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  acceptSuggestion, analyzeDocument, deleteDocument, explainRisk, fetchCostRisk, fetchDelayRisk, fetchDocuments,
  fetchSuggestions, generateClientUpdate, generateDailyLogCopilot, generateModelMapping, generateProgressInterpretation,
  generateWeeklyDigest, rejectSuggestion, setDocumentVisibility, updateDocument, uploadDocument,
  type AcceptSuggestionRequest, type BuildAiSuggestionDto, type BuildRiskKind, type ClientUpdateAiRequest,
  type DailyLogCopilotRequest, type ModelMapperRequest, type ProgressInterpreterRequest, type UpdateBuildDocumentRequest,
  type UploadBuildDocumentInput,
} from "../api/build-ai.api";
import type { UploadProgressHandler } from "@/services/api-client";
import { useBuildSettings } from "./use-build-twin";
import { BUILD_KEYS } from "./use-build";
import { PORTAL_KEYS } from "./use-build-portal";
import { latestPending } from "../lib/ai";

export const AI_KEYS = {
  suggestions: (projectId: string, capability?: string) => ["build", "ai", projectId, "suggestions", capability ?? "all"] as const,
  risk:        (projectId: string, kind: BuildRiskKind) => ["build", "ai", projectId, "risk", kind] as const,
  documents:   (projectId: string) => ["build", "ai", projectId, "documents"] as const,
} as const;

/** BuildAi / BuildVision switches of the company (only the platform changes them). Vision implies AI. */
export function useAiGate() {
  const { data, isLoading } = useBuildSettings();
  const s = data && typeof data === "object" && !Array.isArray(data) ? data : null;
  const aiEnabled = s?.buildAiEnabled === true;
  return { loading: isLoading, aiEnabled, visionEnabled: aiEnabled && s?.buildVisionEnabled === true };
}

/** Suggestions of one capability of the project (newest first, expired ones come back as Expired). */
export function useSuggestions(projectId: string, capability: string, enabled = true) {
  return useQuery({
    queryKey: AI_KEYS.suggestions(projectId, capability),
    queryFn:  () => fetchSuggestions(projectId, { capability }),
    enabled:  !!projectId && enabled,
  });
}

/**
 * The suggestion a panel shows: the one just generated/decided here (kept fresh from the list), otherwise the newest
 * pending one of the capability (suggestions survive navigation). `match` narrows it (e.g. one document).
 */
export function useActiveSuggestion(projectId: string, capability: string, match?: (s: BuildAiSuggestionDto) => boolean) {
  const { data } = useSuggestions(projectId, capability);
  const [current, setCurrent] = useState<BuildAiSuggestionDto | null>(null);
  const list = (Array.isArray(data) ? data : []).filter((s) => s && typeof s === "object" && (!match || match(s)));
  const shown = current ? list.find((s) => s.id === current.id) ?? current : latestPending(list);
  return { suggestion: shown, setCurrent };
}

function useGenerate<TVars>(projectId: string, capability: string, fn: (vars: TVars) => Promise<BuildAiSuggestionDto>) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: fn,
    onSuccess:  () => qc.invalidateQueries({ queryKey: AI_KEYS.suggestions(projectId, capability) }),
  });
}

export const useGenerateDailyLogCopilot = (projectId: string) =>
  useGenerate(projectId, "DailyLogCopilot", (req: DailyLogCopilotRequest) => generateDailyLogCopilot(projectId, req));
export const useGenerateProgressInterpretation = (projectId: string) =>
  useGenerate(projectId, "ProgressInterpreter", (req: ProgressInterpreterRequest) => generateProgressInterpretation(projectId, req));
export const useGenerateModelMapping = (projectId: string) =>
  useGenerate(projectId, "ModelMapper", (req: ModelMapperRequest) => generateModelMapping(projectId, req));
export const useGenerateClientUpdate = (projectId: string) =>
  useGenerate(projectId, "ClientUpdate", (req: ClientUpdateAiRequest) => generateClientUpdate(projectId, req));
export const useGenerateWeeklyDigest = (projectId: string) =>
  useGenerate(projectId, "WeeklyDigest", (_: void) => generateWeeklyDigest(projectId));
export const useExplainRisk = (projectId: string, kind: BuildRiskKind) =>
  useGenerate(projectId, kind === "cost" ? "CostRisk" : "DelayRisk", (_: void) => explainRisk(projectId, kind));

/** What an accepted suggestion may have changed — refreshed after accept (and reject, to drop the pending card). */
function invalidateAfterDecision(qc: ReturnType<typeof useQueryClient>, s: BuildAiSuggestionDto) {
  const p = s.projectId;
  void qc.invalidateQueries({ queryKey: ["build", "ai", p] });
  switch (s.capability) {
    case "DailyLogCopilot":
      void qc.invalidateQueries({ queryKey: BUILD_KEYS.dailyLogs(p) });
      void qc.invalidateQueries({ queryKey: BUILD_KEYS.projectDetails(p) });
      break;
    case "ProgressInterpreter":
      void qc.invalidateQueries({ queryKey: BUILD_KEYS.progressAll(p) });
      void qc.invalidateQueries({ queryKey: BUILD_KEYS.projectDetails(p) });
      void qc.invalidateQueries({ queryKey: ["build", "project", p] });
      void qc.invalidateQueries({ queryKey: BUILD_KEYS.stages(p) });
      void qc.invalidateQueries({ queryKey: ["build", "twin", p] });
      break;
    case "ModelMapper":
      void qc.invalidateQueries({ queryKey: ["build", "twin", p] });
      void qc.invalidateQueries({ queryKey: ["build", "twin", "version"] });
      break;
    case "ClientUpdate":
    case "WeeklyDigest":
      void qc.invalidateQueries({ queryKey: PORTAL_KEYS.clientUpdates(p) });
      break;
  }
}

export function useAcceptSuggestion() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ id, req }: { id: string; req: AcceptSuggestionRequest }) => acceptSuggestion(id, req),
    onSuccess:  (s) => invalidateAfterDecision(qc, s),
  });
}

export function useRejectSuggestion() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ id, note }: { id: string; note?: string }) => rejectSuggestion(id, note),
    onSuccess:  (s) => invalidateAfterDecision(qc, s),
  });
}

// ── Risk ──────────────────────────────────────────────────────────────────────

export function useRiskAnalysis(projectId: string, kind: BuildRiskKind) {
  return useQuery({
    queryKey: AI_KEYS.risk(projectId, kind),
    queryFn:  () => (kind === "cost" ? fetchCostRisk(projectId) : fetchDelayRisk(projectId)),
    enabled:  !!projectId,
  });
}

// ── Documents ─────────────────────────────────────────────────────────────────

export function useDocuments(projectId: string) {
  return useQuery({ queryKey: AI_KEYS.documents(projectId), queryFn: () => fetchDocuments(projectId), enabled: !!projectId });
}

function useDocumentMutation<TVars, TRes>(projectId: string, fn: (vars: TVars) => Promise<TRes>) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: fn,
    onSuccess:  () => qc.invalidateQueries({ queryKey: AI_KEYS.documents(projectId) }),
  });
}

export const useUploadDocument = (projectId: string) =>
  useDocumentMutation(projectId, ({ input, onProgress }: { input: UploadBuildDocumentInput; onProgress?: UploadProgressHandler }) =>
    uploadDocument(projectId, input, onProgress));
export const useUpdateDocument = (projectId: string) =>
  useDocumentMutation(projectId, ({ id, req }: { id: string; req: UpdateBuildDocumentRequest }) => updateDocument(id, req));
export const useSetDocumentVisibility = (projectId: string) =>
  useDocumentMutation(projectId, ({ id, clientVisible }: { id: string; clientVisible: boolean }) => setDocumentVisibility(id, clientVisible));
export const useDeleteDocument = (projectId: string) =>
  useDocumentMutation(projectId, (id: string) => deleteDocument(id));
export const useAnalyzeDocument = (projectId: string) =>
  useGenerate(projectId, "DocumentIntelligence", (id: string) => analyzeDocument(id));
