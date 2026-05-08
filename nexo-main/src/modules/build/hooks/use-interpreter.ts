import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import {
  analyzeMovement, confirmMovement, fetchProjectMovements,
  type AnalyzeMovementRequest, type ConfirmMovementRequest,
} from "../api/interpreter.api";
import { BUILD_KEYS } from "./use-build";

// ── Query keys ────────────────────────────────────────────────────────────────

export const MOVEMENT_KEYS = {
  projectMovements: (projectId: string) =>
    ["movements", "project", projectId] as const,
};

// ── Queries ───────────────────────────────────────────────────────────────────

export function useProjectMovements(projectId: string, enabled = true) {
  return useQuery({
    queryKey: MOVEMENT_KEYS.projectMovements(projectId),
    queryFn:  () => fetchProjectMovements(projectId),
    enabled:  !!projectId && enabled,
    staleTime: 30_000,
  });
}

// ── Mutations ─────────────────────────────────────────────────────────────────

/** Analyzes text → returns DraftId + SuggestionId + extracted fields. */
export function useAnalyzeMovement() {
  return useMutation({
    mutationFn: (req: AnalyzeMovementRequest) => analyzeMovement(req),
  });
}

/**
 * Confirms a draft movement and invalidates the project's financial summary
 * + movement list so the UI reflects the new expense immediately.
 */
export function useConfirmMovement(projectId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ draftId, req }: { draftId: string; req: ConfirmMovementRequest }) =>
      confirmMovement(draftId, req),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: BUILD_KEYS.projectFinancial(projectId) });
      qc.invalidateQueries({ queryKey: MOVEMENT_KEYS.projectMovements(projectId) });
    },
  });
}
