import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { ApiError } from "@/services/api-client";
import {
  assignMovementStage, fetchCostByStage, fetchProjectProgress, fetchProjectProgressAt,
  fetchScheduleRevisions, freezeScheduleBaseline,
} from "../api/build-progress.api";
import { BUILD_KEYS } from "./use-build";
import { MOVEMENT_KEYS } from "./use-interpreter";

export const PROGRESS_KEYS = {
  progress:    (projectId: string)               => [...BUILD_KEYS.progressAll(projectId), "current"] as const,
  at:          (projectId: string, date: string) => [...BUILD_KEYS.progressAll(projectId), "at", date] as const,
  revisions:   (projectId: string)               => [...BUILD_KEYS.progressAll(projectId), "revisions"] as const,
  costByStage: (projectId: string)               => [...BUILD_KEYS.progressAll(projectId), "cost-by-stage"] as const,
} as const;

export function useProjectProgress(projectId: string) {
  return useQuery({
    queryKey:  PROGRESS_KEYS.progress(projectId),
    queryFn:   () => fetchProjectProgress(projectId),
    enabled:   !!projectId,
    staleTime: 30_000,
  });
}

/** "Como estava em": `null` when there is no snapshot up to the date (the API answers 404). */
export function useProjectProgressAt(projectId: string, date: string | null) {
  return useQuery({
    queryKey: PROGRESS_KEYS.at(projectId, date ?? ""),
    queryFn:  async () => {
      try {
        return await fetchProjectProgressAt(projectId, date as string);
      } catch (e) {
        if (e instanceof ApiError && e.status === 404) return null;
        throw e;
      }
    },
    enabled:   !!projectId && !!date,
    staleTime: 60_000,
    retry:     false,
  });
}

export function useScheduleRevisions(projectId: string) {
  return useQuery({
    queryKey:  PROGRESS_KEYS.revisions(projectId),
    queryFn:   () => fetchScheduleRevisions(projectId),
    enabled:   !!projectId,
    staleTime: 60_000,
  });
}

export function useFreezeBaseline(projectId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (reason?: string) => freezeScheduleBaseline(projectId, reason),
    onSuccess:  () => qc.invalidateQueries({ queryKey: BUILD_KEYS.progressAll(projectId) }),
  });
}

export function useCostByStage(projectId: string) {
  return useQuery({
    queryKey:  PROGRESS_KEYS.costByStage(projectId),
    queryFn:   () => fetchCostByStage(projectId),
    enabled:   !!projectId,
    staleTime: 30_000,
  });
}

export function useAssignMovementStage(projectId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ movementId, stageId }: { movementId: string; stageId: string | null }) =>
      assignMovementStage(projectId, movementId, stageId),
    onSuccess: () => Promise.all([
      qc.invalidateQueries({ queryKey: PROGRESS_KEYS.costByStage(projectId) }),
      qc.invalidateQueries({ queryKey: MOVEMENT_KEYS.projectMovements(projectId) }),
    ]),
  });
}
