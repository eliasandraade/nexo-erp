import { useMutation, useQuery, useQueryClient, type QueryClient } from "@tanstack/react-query";
import {
  addSelectionOption, approveChangeOrder, approveMeasurement, cancelChangeOrder, cancelMaterial, cancelSelection,
  confirmSelection, createBudgetFromTemplate, createBudgetTemplate, createBudgetVersion, createChangeOrder,
  createMaterial, createMeasurement, createSelection, deleteBudgetTemplate, deliverMaterial, duplicateBudget,
  fetchBudgetTemplates, fetchBudgetVersions, fetchChangeOrders, fetchMaterials, fetchMeasurements, fetchSelections,
  linkMaterialMovement, orderMaterial, quoteMaterial, rejectChangeOrder, rejectMeasurement, removeSelectionOption,
  saveBudgetAsTemplate, sendChangeOrder, setBudgetTerms, submitMeasurement, updateBudgetTemplate, updateChangeOrder,
  updateMaterial, updateMeasurement, updateSelection, updateSelectionOption,
  type CreateBudgetFromTemplateRequest, type MaterialFilters, type SaveBudgetAsTemplateRequest,
  type SaveBuildBudgetTemplateRequest, type SaveBuildChangeOrderRequest, type SaveBuildMaterialRequest,
  type SaveBuildMeasurementRequest, type SaveBuildSelectionOptionRequest, type SaveBuildSelectionRequest,
  type SetBuildBudgetTermsRequest,
} from "../api/build-commercial.api";
import type { BuildBudgetDto } from "../api/build.api";
import { BUILD_KEYS } from "./use-build";

export const COMMERCIAL_KEYS = {
  budgetVersions: (budgetId: string)  => ["build", "budget-versions", budgetId] as const,
  templates:      ()                  => ["build", "budget-templates"] as const,
  changeOrders:   (projectId: string) => ["build", "change-orders", projectId] as const,
  selections:     (projectId: string) => ["build", "selections", projectId] as const,
  materials:      (projectId: string, f?: MaterialFilters) => ["build", "materials", projectId, f ?? {}] as const,
  materialsAll:   (projectId: string) => ["build", "materials", projectId] as const,
  measurements:   (projectId: string) => ["build", "measurements", projectId] as const,
} as const;

// ── Budgets ───────────────────────────────────────────────────────────────────

function invalidateBudgets(qc: QueryClient, budget?: BuildBudgetDto) {
  const jobs = [
    qc.invalidateQueries({ queryKey: ["build", "budgets"] }),
    qc.invalidateQueries({ queryKey: ["build", "budget-versions"] }),
  ];
  if (budget) jobs.push(qc.invalidateQueries({ queryKey: BUILD_KEYS.budget(budget.id) }));
  return Promise.all(jobs);
}

export function useBudgetVersions(budgetId: string) {
  return useQuery({
    queryKey:  COMMERCIAL_KEYS.budgetVersions(budgetId),
    queryFn:   () => fetchBudgetVersions(budgetId),
    enabled:   !!budgetId,
    staleTime: 30_000,
  });
}

export function useSetBudgetTerms() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ id, req }: { id: string; req: SetBuildBudgetTermsRequest }) => setBudgetTerms(id, req),
    onSuccess:  (data) => {
      qc.setQueryData(BUILD_KEYS.budget(data.id), data);
      return invalidateBudgets(qc);
    },
  });
}

export function useDuplicateBudget() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ id, name }: { id: string; name?: string }) => duplicateBudget(id, name),
    onSuccess:  () => invalidateBudgets(qc),
  });
}

export function useNewBudgetVersion() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => createBudgetVersion(id),
    onSuccess:  () => invalidateBudgets(qc),
  });
}

export function useSaveBudgetAsTemplate() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ id, req }: { id: string; req: SaveBudgetAsTemplateRequest }) => saveBudgetAsTemplate(id, req),
    onSuccess:  () => qc.invalidateQueries({ queryKey: COMMERCIAL_KEYS.templates() }),
  });
}

// ── Templates ─────────────────────────────────────────────────────────────────

export function useBudgetTemplates(enabled = true) {
  return useQuery({
    queryKey:  COMMERCIAL_KEYS.templates(),
    queryFn:   fetchBudgetTemplates,
    enabled,
    staleTime: 60_000,
  });
}

export function useSaveBudgetTemplate() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ id, req }: { id?: string; req: SaveBuildBudgetTemplateRequest }) =>
      id ? updateBudgetTemplate(id, req) : createBudgetTemplate(req),
    onSuccess:  () => qc.invalidateQueries({ queryKey: COMMERCIAL_KEYS.templates() }),
  });
}

export function useDeleteBudgetTemplate() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => deleteBudgetTemplate(id),
    onSuccess:  () => qc.invalidateQueries({ queryKey: COMMERCIAL_KEYS.templates() }),
  });
}

export function useCreateBudgetFromTemplate() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ templateId, req }: { templateId: string; req: CreateBudgetFromTemplateRequest }) =>
      createBudgetFromTemplate(templateId, req),
    onSuccess:  () => invalidateBudgets(qc),
  });
}

// ── Change orders ─────────────────────────────────────────────────────────────

/** A decided change order moves the contract, the stage end and the progress: refresh all of it. */
function invalidateChangeOrders(qc: QueryClient, projectId: string) {
  return Promise.all([
    qc.invalidateQueries({ queryKey: COMMERCIAL_KEYS.changeOrders(projectId) }),
    qc.invalidateQueries({ queryKey: BUILD_KEYS.projectFinancial(projectId) }),
    qc.invalidateQueries({ queryKey: BUILD_KEYS.project(projectId) }),
    qc.invalidateQueries({ queryKey: BUILD_KEYS.projectDetails(projectId) }),
    qc.invalidateQueries({ queryKey: BUILD_KEYS.stages(projectId) }),
    qc.invalidateQueries({ queryKey: BUILD_KEYS.progressAll(projectId) }),
  ]);
}

export function useChangeOrders(projectId: string) {
  return useQuery({
    queryKey:  COMMERCIAL_KEYS.changeOrders(projectId),
    queryFn:   () => fetchChangeOrders(projectId),
    enabled:   !!projectId,
    staleTime: 30_000,
  });
}

export function useSaveChangeOrder(projectId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ id, req }: { id?: string; req: SaveBuildChangeOrderRequest }) =>
      id ? updateChangeOrder(id, req) : createChangeOrder(projectId, req),
    onSuccess:  () => qc.invalidateQueries({ queryKey: COMMERCIAL_KEYS.changeOrders(projectId) }),
  });
}

export type ChangeOrderAction = "send" | "approve" | "reject" | "cancel";

export function useChangeOrderAction(projectId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ id, action, comment }: { id: string; action: ChangeOrderAction; comment?: string }) => {
      switch (action) {
        case "send":    return sendChangeOrder(id);
        case "approve": return approveChangeOrder(id, comment);
        case "reject":  return rejectChangeOrder(id, comment);
        default:        return cancelChangeOrder(id);
      }
    },
    onSuccess: () => invalidateChangeOrders(qc, projectId),
  });
}

// ── Selections ────────────────────────────────────────────────────────────────

export function useSelections(projectId: string) {
  return useQuery({
    queryKey:  COMMERCIAL_KEYS.selections(projectId),
    queryFn:   () => fetchSelections(projectId),
    enabled:   !!projectId,
    staleTime: 30_000,
  });
}

function useSelectionMutation<TVars>(projectId: string, fn: (vars: TVars) => Promise<unknown>) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: fn,
    onSuccess:  () => Promise.all([
      qc.invalidateQueries({ queryKey: COMMERCIAL_KEYS.selections(projectId) }),
      // confirming may create a draft change order
      qc.invalidateQueries({ queryKey: COMMERCIAL_KEYS.changeOrders(projectId) }),
    ]),
  });
}

export const useSaveSelection = (projectId: string) =>
  useSelectionMutation(projectId, ({ id, req }: { id?: string; req: SaveBuildSelectionRequest }) =>
    id ? updateSelection(id, req) : createSelection(projectId, req));

export const useSaveSelectionOption = (projectId: string) =>
  useSelectionMutation(projectId,
    ({ selectionId, optionId, req }: { selectionId: string; optionId?: string; req: SaveBuildSelectionOptionRequest }) =>
      optionId ? updateSelectionOption(optionId, req) : addSelectionOption(selectionId, req));

export const useRemoveSelectionOption = (projectId: string) =>
  useSelectionMutation(projectId, (optionId: string) => removeSelectionOption(optionId));

export function useConfirmSelection(projectId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ id, optionId }: { id: string; optionId?: string }) => confirmSelection(id, optionId),
    onSuccess:  () => Promise.all([
      qc.invalidateQueries({ queryKey: COMMERCIAL_KEYS.selections(projectId) }),
      qc.invalidateQueries({ queryKey: COMMERCIAL_KEYS.changeOrders(projectId) }),
    ]),
  });
}

export const useCancelSelection = (projectId: string) =>
  useSelectionMutation(projectId, (id: string) => cancelSelection(id));

// ── Materials ─────────────────────────────────────────────────────────────────

export function useMaterials(projectId: string, filters: MaterialFilters = {}) {
  return useQuery({
    queryKey:  COMMERCIAL_KEYS.materials(projectId, filters),
    queryFn:   () => fetchMaterials(projectId, filters),
    enabled:   !!projectId,
    staleTime: 30_000,
  });
}

export type MaterialAction =
  | { kind: "quote";   supplierId?: string | null; estimatedUnitCost?: number | null }
  | { kind: "order";   supplierId?: string | null; unitCost?: number | null }
  | { kind: "deliver"; quantity: number; date?: string; note?: string; receivedBy?: string }
  | { kind: "cancel" }
  | { kind: "movement"; movementId: string | null };

function runMaterialAction(id: string, a: MaterialAction) {
  switch (a.kind) {
    case "quote":    return quoteMaterial(id, { supplierId: a.supplierId, estimatedUnitCost: a.estimatedUnitCost });
    case "order":    return orderMaterial(id, { supplierId: a.supplierId, unitCost: a.unitCost });
    case "deliver":  return deliverMaterial(id, { quantity: a.quantity, date: a.date, note: a.note, receivedBy: a.receivedBy });
    case "movement": return linkMaterialMovement(id, a.movementId);
    default:         return cancelMaterial(id);
  }
}

export function useSaveMaterial(projectId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ id, req }: { id?: string; req: SaveBuildMaterialRequest }) =>
      id ? updateMaterial(id, req) : createMaterial(projectId, req),
    onSuccess:  () => qc.invalidateQueries({ queryKey: COMMERCIAL_KEYS.materialsAll(projectId) }),
  });
}

export function useMaterialAction(projectId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ id, action }: { id: string; action: MaterialAction }) => runMaterialAction(id, action),
    onSuccess:  () => qc.invalidateQueries({ queryKey: COMMERCIAL_KEYS.materialsAll(projectId) }),
  });
}

// ── Measurements ──────────────────────────────────────────────────────────────

export function useMeasurements(projectId: string) {
  return useQuery({
    queryKey:  COMMERCIAL_KEYS.measurements(projectId),
    queryFn:   () => fetchMeasurements(projectId),
    enabled:   !!projectId,
    staleTime: 30_000,
  });
}

export function useSaveMeasurement(projectId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ id, req }: { id?: string; req: SaveBuildMeasurementRequest }) =>
      id ? updateMeasurement(id, req) : createMeasurement(projectId, req),
    onSuccess:  () => qc.invalidateQueries({ queryKey: COMMERCIAL_KEYS.measurements(projectId) }),
  });
}

export type MeasurementAction = "submit" | "approve" | "reject";

/** Approving applies the measured % to each stage: stages, progress and the project cards change. */
export function useMeasurementAction(projectId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ id, action, reason }: { id: string; action: MeasurementAction; reason?: string }) => {
      if (action === "submit") return submitMeasurement(id);
      if (action === "approve") return approveMeasurement(id);
      return rejectMeasurement(id, reason);
    },
    onSuccess: () => Promise.all([
      qc.invalidateQueries({ queryKey: COMMERCIAL_KEYS.measurements(projectId) }),
      qc.invalidateQueries({ queryKey: BUILD_KEYS.stages(projectId) }),
      qc.invalidateQueries({ queryKey: BUILD_KEYS.projectDetails(projectId) }),
      qc.invalidateQueries({ queryKey: BUILD_KEYS.progressAll(projectId) }),
      qc.invalidateQueries({ queryKey: ["build", "projects"] }),
      qc.invalidateQueries({ queryKey: BUILD_KEYS.dashboard() }),
    ]),
  });
}
