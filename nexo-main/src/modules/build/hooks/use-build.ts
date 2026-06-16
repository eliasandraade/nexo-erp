import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import {
  fetchProjects, fetchProject, fetchProjectDetails, fetchProjectFinancialSummary,
  fetchBuildDashboard,
  createProject, updateProject,
  startProject, pauseProject, completeProject, cancelProject,
  fetchStages, createStage, updateStageProgress, reorderStages, deleteStage,
  fetchBudgets, fetchBudget, createBudget,
  sendBudget, approveBudget, rejectBudget, convertBudget, setBudgetMargin,
  addBudgetItem, updateBudgetItem, removeBudgetItem,
  fetchDailyLogs, fetchDailyLog, createDailyLog, updateDailyLog,
  addDailyLogPhoto, removeDailyLogPhoto,
  type BuildProjectStatus, type BuildBudgetStatus,
  type CreateBuildProjectRequest, type UpdateBuildProjectRequest,
  type CreateBuildStageRequest, type UpdateBuildStageProgressRequest,
  type ReorderBuildStagesRequest,
  type CreateBuildBudgetRequest, type AddBuildBudgetItemRequest,
  type UpdateBuildBudgetItemRequest, type SetBudgetMarginRequest,
  type ConvertBudgetToProjectRequest,
  type CreateDailyLogRequest, type UpdateDailyLogRequest, type AddDailyLogPhotoRequest,
} from "../api/build.api";

// ── Query keys ────────────────────────────────────────────────────────────────

export const BUILD_KEYS = {
  dashboard:         ()                            => ["build", "dashboard"] as const,
  projects:          (status?: BuildProjectStatus) => ["build", "projects", status] as const,
  project:           (id: string)                  => ["build", "project", id] as const,
  projectDetails:    (id: string)                  => ["build", "project", id, "details"] as const,
  projectFinancial:  (id: string)                  => ["build", "project", id, "financial"] as const,
  stages:            (projectId: string)           => ["build", "stages", projectId] as const,
  budgets:           (projectId?: string, status?: BuildBudgetStatus) =>
                                                      ["build", "budgets", projectId, status] as const,
  budget:            (id: string)                  => ["build", "budget", id] as const,
  dailyLogs:         (projectId: string)           => ["build", "daily-logs", projectId] as const,
  dailyLog:          (id: string)                  => ["build", "daily-log", id] as const,
} as const;

// ── Dashboard ─────────────────────────────────────────────────────────────────

export function useBuildDashboard() {
  return useQuery({
    queryKey:  BUILD_KEYS.dashboard(),
    queryFn:   fetchBuildDashboard,
    staleTime: 30_000,
  });
}

// ── Projects ──────────────────────────────────────────────────────────────────

export function useProjects(status?: BuildProjectStatus, pageSize = 50) {
  return useQuery({
    queryKey: BUILD_KEYS.projects(status),
    queryFn:  () => fetchProjects({ status, pageSize }),
    staleTime: 30_000,
  });
}

export function useProject(id: string) {
  return useQuery({
    queryKey: BUILD_KEYS.project(id),
    queryFn:  () => fetchProject(id),
    enabled:  !!id,
    staleTime: 30_000,
  });
}

export function useProjectDetails(id: string) {
  return useQuery({
    queryKey: BUILD_KEYS.projectDetails(id),
    queryFn:  () => fetchProjectDetails(id),
    enabled:  !!id,
    staleTime: 30_000,
  });
}

export function useProjectFinancial(id: string) {
  return useQuery({
    queryKey: BUILD_KEYS.projectFinancial(id),
    queryFn:  () => fetchProjectFinancialSummary(id),
    enabled:  !!id,
    staleTime: 60_000,
  });
}

export function useCreateProject() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (req: CreateBuildProjectRequest) => createProject(req),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["build", "projects"] });
    },
  });
}

export function useUpdateProject(id: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (req: UpdateBuildProjectRequest) => updateProject(id, req),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: BUILD_KEYS.project(id) });
      qc.invalidateQueries({ queryKey: BUILD_KEYS.projectDetails(id) });
      qc.invalidateQueries({ queryKey: ["build", "projects"] });
    },
  });
}

function useProjectTransition(id: string, fn: (id: string) => Promise<unknown>) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: () => fn(id),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: BUILD_KEYS.project(id) });
      qc.invalidateQueries({ queryKey: BUILD_KEYS.projectDetails(id) });
      qc.invalidateQueries({ queryKey: ["build", "projects"] });
    },
  });
}

export const useStartProject    = (id: string) => useProjectTransition(id, startProject);
export const usePauseProject    = (id: string) => useProjectTransition(id, pauseProject);
export const useCompleteProject = (id: string) => useProjectTransition(id, completeProject);
export const useCancelProject   = (id: string) => useProjectTransition(id, cancelProject);

// ── Stages ────────────────────────────────────────────────────────────────────

export function useStages(projectId: string) {
  return useQuery({
    queryKey: BUILD_KEYS.stages(projectId),
    queryFn:  () => fetchStages(projectId),
    enabled:  !!projectId,
    staleTime: 30_000,
  });
}

export function useCreateStage(projectId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (req: CreateBuildStageRequest) => createStage(projectId, req),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: BUILD_KEYS.stages(projectId) });
      qc.invalidateQueries({ queryKey: BUILD_KEYS.projectDetails(projectId) });
    },
  });
}

export function useUpdateStageProgress(projectId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ id, req }: { id: string; req: UpdateBuildStageProgressRequest }) =>
      updateStageProgress(id, req),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: BUILD_KEYS.stages(projectId) });
    },
  });
}

export function useReorderStages(projectId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (req: ReorderBuildStagesRequest) => reorderStages(projectId, req),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: BUILD_KEYS.stages(projectId) });
    },
  });
}

export function useDeleteStage(projectId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => deleteStage(id),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: BUILD_KEYS.stages(projectId) });
    },
  });
}

// ── Budgets ───────────────────────────────────────────────────────────────────

export function useBudgets(projectId?: string, status?: BuildBudgetStatus) {
  return useQuery({
    queryKey: BUILD_KEYS.budgets(projectId, status),
    queryFn:  () => fetchBudgets({ projectId, status, pageSize: 50 }),
    staleTime: 30_000,
  });
}

export function useBudget(id: string) {
  return useQuery({
    queryKey: BUILD_KEYS.budget(id),
    queryFn:  () => fetchBudget(id),
    enabled:  !!id,
    staleTime: 30_000,
  });
}

export function useCreateBudget() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (req: CreateBuildBudgetRequest) => createBudget(req),
    onSuccess: (data) => {
      qc.invalidateQueries({ queryKey: ["build", "budgets"] });
      if (data.projectId) {
        qc.invalidateQueries({ queryKey: BUILD_KEYS.budgets(data.projectId) });
      }
    },
  });
}

function useBudgetMutation(invalidateProjectId?: string) {
  const qc = useQueryClient();
  const invalidate = (budgetId: string) => {
    qc.invalidateQueries({ queryKey: BUILD_KEYS.budget(budgetId) });
    qc.invalidateQueries({ queryKey: ["build", "budgets"] });
    if (invalidateProjectId) {
      qc.invalidateQueries({ queryKey: BUILD_KEYS.project(invalidateProjectId) });
      qc.invalidateQueries({ queryKey: BUILD_KEYS.projectDetails(invalidateProjectId) });
    }
  };
  return { invalidate };
}

export function useSendBudget(projectId?: string) {
  const { invalidate } = useBudgetMutation(projectId);
  return useMutation({
    mutationFn: (id: string) => sendBudget(id),
    onSuccess:  (_, id)      => invalidate(id),
  });
}

export function useApproveBudget(projectId?: string) {
  const { invalidate } = useBudgetMutation(projectId);
  return useMutation({
    mutationFn: (id: string) => approveBudget(id),
    onSuccess:  (_, id)      => invalidate(id),
  });
}

export function useRejectBudget(projectId?: string) {
  const { invalidate } = useBudgetMutation(projectId);
  return useMutation({
    mutationFn: (id: string) => rejectBudget(id),
    onSuccess:  (_, id)      => invalidate(id),
  });
}

export function useConvertBudget() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ id, req }: { id: string; req: ConvertBudgetToProjectRequest }) =>
      convertBudget(id, req),
    onSuccess: (data) => {
      qc.invalidateQueries({ queryKey: ["build", "budgets"] });
      qc.invalidateQueries({ queryKey: ["build", "projects"] });
      if (data.projectId) {
        qc.invalidateQueries({ queryKey: BUILD_KEYS.project(data.projectId) });
        qc.invalidateQueries({ queryKey: BUILD_KEYS.projectDetails(data.projectId) });
      }
    },
  });
}

export function useSetBudgetMargin() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ id, req }: { id: string; req: SetBudgetMarginRequest }) =>
      setBudgetMargin(id, req),
    onSuccess: (data) => {
      qc.invalidateQueries({ queryKey: BUILD_KEYS.budget(data.id) });
      qc.invalidateQueries({ queryKey: ["build", "budgets"] });
    },
  });
}

export function useAddBudgetItem(budgetId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (req: AddBuildBudgetItemRequest) => addBudgetItem(budgetId, req),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: BUILD_KEYS.budget(budgetId) });
    },
  });
}

export function useUpdateBudgetItem(budgetId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ id, req }: { id: string; req: UpdateBuildBudgetItemRequest }) =>
      updateBudgetItem(id, req),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: BUILD_KEYS.budget(budgetId) });
    },
  });
}

export function useRemoveBudgetItem(budgetId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => removeBudgetItem(id),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: BUILD_KEYS.budget(budgetId) });
    },
  });
}

// ── Daily logs ────────────────────────────────────────────────────────────────

export function useDailyLogs(projectId: string, pageSize = 30) {
  return useQuery({
    queryKey: BUILD_KEYS.dailyLogs(projectId),
    queryFn:  () => fetchDailyLogs(projectId, { pageSize }),
    enabled:  !!projectId,
    staleTime: 30_000,
  });
}

export function useDailyLog(id: string) {
  return useQuery({
    queryKey: BUILD_KEYS.dailyLog(id),
    queryFn:  () => fetchDailyLog(id),
    enabled:  !!id,
  });
}

export function useCreateDailyLog(projectId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (req: CreateDailyLogRequest) => createDailyLog(projectId, req),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: BUILD_KEYS.dailyLogs(projectId) });
      qc.invalidateQueries({ queryKey: BUILD_KEYS.projectDetails(projectId) });
    },
  });
}

export function useUpdateDailyLog(projectId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ id, req }: { id: string; req: UpdateDailyLogRequest }) =>
      updateDailyLog(id, req),
    onSuccess: (data) => {
      qc.invalidateQueries({ queryKey: BUILD_KEYS.dailyLog(data.id) });
      qc.invalidateQueries({ queryKey: BUILD_KEYS.dailyLogs(projectId) });
    },
  });
}

export function useAddDailyLogPhoto(projectId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ logId, req }: { logId: string; req: AddDailyLogPhotoRequest }) =>
      addDailyLogPhoto(logId, req),
    onSuccess: (data) => {
      qc.invalidateQueries({ queryKey: BUILD_KEYS.dailyLog(data.id) });
      qc.invalidateQueries({ queryKey: BUILD_KEYS.dailyLogs(projectId) });
    },
  });
}

export function useRemoveDailyLogPhoto(projectId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (photoId: string) => removeDailyLogPhoto(photoId),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: BUILD_KEYS.dailyLogs(projectId) });
    },
  });
}
