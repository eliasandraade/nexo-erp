import { useInfiniteQuery, useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import {
  analyzeMovement, confirmMovement, fetchProjectMovements, voidMovement,
  type AnalyzeMovementRequest, type ConfirmMovementRequest,
} from "../api/interpreter.api";
import { BUILD_KEYS } from "./use-build";

// ── Query keys ────────────────────────────────────────────────────────────────

export const MOVEMENT_KEYS = {
  projectMovements: (projectId: string) =>
    ["movements", "project", projectId] as const,
};

// ── Queries ───────────────────────────────────────────────────────────────────

/** Confirmed expenses of the project, page by page; `fetchNextPage` loads older ones. */
export function useProjectMovements(projectId: string, enabled = true) {
  return useInfiniteQuery({
    queryKey: [...MOVEMENT_KEYS.projectMovements(projectId), "pages"],
    queryFn:  ({ pageParam }) => fetchProjectMovements(projectId, { page: pageParam, pageSize: 30 }),
    initialPageParam: 1,
    getNextPageParam: (last) => (last.page * last.pageSize < last.totalCount ? last.page + 1 : undefined),
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
      qc.invalidateQueries({ queryKey: BUILD_KEYS.dashboard() });
    },
  });
}

/** Reverses a wrong expense: out of the project's realized cost, counter-entry in the ledger. */
export function useVoidMovement(projectId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => voidMovement(id),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: BUILD_KEYS.projectFinancial(projectId) });
      qc.invalidateQueries({ queryKey: MOVEMENT_KEYS.projectMovements(projectId) });
      qc.invalidateQueries({ queryKey: BUILD_KEYS.dashboard() });
    },
  });
}
