import { useMutation, useQuery, useQueryClient, type QueryClient } from "@tanstack/react-query";
import {
  acceptHandover, addAssistancePhoto, cancelInspection, cancelNonConformity, closeAssistance, completeInspection,
  createChecklistTemplate, createDefaultWarranty, createInspection, createNonConformity, createWalkthrough,
  createWarranty, deleteChecklistTemplate, deleteWarranty, deliverHandover, fetchAssistance, fetchChecklistTemplates,
  fetchHandover, fetchInspection, fetchInspections, fetchNonConformities, fetchQualitySummary, fetchWarranty,
  openAssistance, recordInspectionItem, rejectAssistance, reopenNonConformity, resolveAssistance,
  resolveNonConformity, saveHandover, scheduleAssistance, startAssistance, startInspection, startNonConformity,
  triageAssistance, updateChecklistTemplate, updateInspection, updateNonConformity, updateWarranty,
  verifyNonConformity,
  type AssistanceFilters, type BuildInspectionDto, type CreateBuildInspectionRequest,
  type CreateBuildWalkthroughRequest, type InspectionFilters, type NonConformityFilters,
  type OpenBuildAssistanceRequest, type RecordBuildInspectionItemRequest, type SaveBuildChecklistTemplateRequest,
  type SaveBuildHandoverRequest, type SaveBuildNonConformityRequest, type SaveBuildWarrantyTermRequest,
  type UpdateBuildInspectionRequest,
} from "../api/build-quality.api";
import { BUILD_KEYS } from "./use-build";

export const QUALITY_KEYS = {
  templates:       ()                  => ["build", "checklist-templates"] as const,
  inspectionsAll:  (projectId: string) => ["build", "inspections", projectId] as const,
  inspections:     (projectId: string, f?: InspectionFilters) => ["build", "inspections", projectId, f ?? {}] as const,
  inspection:      (id: string)        => ["build", "inspection", id] as const,
  ncAll:           (projectId: string) => ["build", "non-conformities", projectId] as const,
  nonConformities: (projectId: string, f?: NonConformityFilters) => ["build", "non-conformities", projectId, f ?? {}] as const,
  summary:         (projectId: string) => ["build", "quality-summary", projectId] as const,
  handover:        (projectId: string) => ["build", "handover", projectId] as const,
  warranty:        (projectId: string) => ["build", "warranty", projectId] as const,
  assistanceAll:   (projectId: string) => ["build", "assistance", projectId] as const,
  assistance:      (projectId: string, f?: AssistanceFilters) => ["build", "assistance", projectId, f ?? {}] as const,
} as const;

/** Inspections, NCs and the summary move together (completing an inspection opens NCs). */
function invalidateQuality(qc: QueryClient, projectId: string) {
  return Promise.all([
    qc.invalidateQueries({ queryKey: QUALITY_KEYS.inspectionsAll(projectId) }),
    qc.invalidateQueries({ queryKey: QUALITY_KEYS.ncAll(projectId) }),
    qc.invalidateQueries({ queryKey: QUALITY_KEYS.summary(projectId) }),
    qc.invalidateQueries({ queryKey: QUALITY_KEYS.handover(projectId) }),
  ]);
}

// ── Checklist templates ───────────────────────────────────────────────────────

export function useChecklistTemplates() {
  return useQuery({ queryKey: QUALITY_KEYS.templates(), queryFn: fetchChecklistTemplates, staleTime: 60_000 });
}

export function useSaveChecklistTemplate() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ id, req }: { id?: string; req: SaveBuildChecklistTemplateRequest }) =>
      id ? updateChecklistTemplate(id, req) : createChecklistTemplate(req),
    onSuccess:  () => qc.invalidateQueries({ queryKey: QUALITY_KEYS.templates() }),
  });
}

export function useDeleteChecklistTemplate() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => deleteChecklistTemplate(id),
    onSuccess:  () => qc.invalidateQueries({ queryKey: QUALITY_KEYS.templates() }),
  });
}

// ── Inspections ───────────────────────────────────────────────────────────────

export function useInspections(projectId: string, f: InspectionFilters = {}) {
  return useQuery({
    queryKey:  QUALITY_KEYS.inspections(projectId, f),
    queryFn:   () => fetchInspections(projectId, f),
    enabled:   !!projectId,
    staleTime: 30_000,
  });
}

export function useInspection(id: string | null) {
  return useQuery({
    queryKey:  QUALITY_KEYS.inspection(id ?? ""),
    queryFn:   () => fetchInspection(id as string),
    enabled:   !!id,
    staleTime: 10_000,
  });
}

export function useCreateInspection(projectId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (req: CreateBuildInspectionRequest) => createInspection(projectId, req),
    onSuccess:  (data) => {
      qc.setQueryData(QUALITY_KEYS.inspection(data.id), data);
      return invalidateQuality(qc, projectId);
    },
  });
}

export function useUpdateInspection(projectId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ id, req }: { id: string; req: UpdateBuildInspectionRequest }) => updateInspection(id, req),
    onSuccess:  (data) => {
      qc.setQueryData(QUALITY_KEYS.inspection(data.id), data);
      return invalidateQuality(qc, projectId);
    },
  });
}

export type InspectionAction =
  | { kind: "start" }
  | { kind: "item"; itemId: string; req: RecordBuildInspectionItemRequest }
  | { kind: "complete"; notes?: string }
  | { kind: "cancel" };

function runInspectionAction(id: string, a: InspectionAction): Promise<BuildInspectionDto> {
  switch (a.kind) {
    case "start":    return startInspection(id);
    case "item":     return recordInspectionItem(id, a.itemId, a.req);
    case "complete": return completeInspection(id, a.notes);
    default:         return cancelInspection(id);
  }
}

/** Every inspection change answers with the whole inspection: the cache is updated in place. */
export function useInspectionAction(projectId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ id, action }: { id: string; action: InspectionAction }) => runInspectionAction(id, action),
    onSuccess:  (data, { action }) => {
      qc.setQueryData(QUALITY_KEYS.inspection(data.id), data);
      if (action.kind === "item") return qc.invalidateQueries({ queryKey: QUALITY_KEYS.inspectionsAll(projectId) });
      return invalidateQuality(qc, projectId);
    },
  });
}

// ── Non-conformities ──────────────────────────────────────────────────────────

export function useNonConformities(projectId: string, f: NonConformityFilters = {}) {
  return useQuery({
    queryKey:  QUALITY_KEYS.nonConformities(projectId, f),
    queryFn:   () => fetchNonConformities(projectId, f),
    enabled:   !!projectId,
    staleTime: 30_000,
  });
}

export function useSaveNonConformity(projectId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ id, req }: { id?: string; req: SaveBuildNonConformityRequest }) =>
      id ? updateNonConformity(id, req) : createNonConformity(projectId, req),
    onSuccess:  () => invalidateQuality(qc, projectId),
  });
}

export type NcAction =
  | { kind: "start" }
  | { kind: "resolve"; rootCause?: string | null; correctiveAction?: string | null }
  | { kind: "verify" }
  | { kind: "reopen" }
  | { kind: "cancel"; reason: string };

function runNcAction(id: string, a: NcAction) {
  switch (a.kind) {
    case "start":   return startNonConformity(id);
    case "resolve": return resolveNonConformity(id, { rootCause: a.rootCause, correctiveAction: a.correctiveAction });
    case "verify":  return verifyNonConformity(id);
    case "reopen":  return reopenNonConformity(id);
    default:        return cancelNonConformity(id, a.reason);
  }
}

/** Resolving / cancelling a Critical NC frees its stage: stages and progress are refreshed too. */
export function useNonConformityAction(projectId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ id, action }: { id: string; action: NcAction }) => runNcAction(id, action),
    onSuccess:  () => Promise.all([
      invalidateQuality(qc, projectId),
      qc.invalidateQueries({ queryKey: BUILD_KEYS.stages(projectId) }),
    ]),
  });
}

export function useQualitySummary(projectId: string) {
  return useQuery({
    queryKey:  QUALITY_KEYS.summary(projectId),
    queryFn:   () => fetchQualitySummary(projectId),
    enabled:   !!projectId,
    staleTime: 30_000,
  });
}

// ── Handover ──────────────────────────────────────────────────────────────────

export function useHandover(projectId: string) {
  return useQuery({
    queryKey:  QUALITY_KEYS.handover(projectId),
    queryFn:   () => fetchHandover(projectId),
    enabled:   !!projectId,
    staleTime: 30_000,
  });
}

export function useSaveHandover(projectId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (req: SaveBuildHandoverRequest) => saveHandover(projectId, req),
    onSuccess:  (data) => { qc.setQueryData(QUALITY_KEYS.handover(projectId), data); },
  });
}

export function useCreateWalkthrough(projectId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (req: CreateBuildWalkthroughRequest) => createWalkthrough(projectId, req),
    onSuccess:  (data) => {
      qc.setQueryData(QUALITY_KEYS.inspection(data.id), data);
      return invalidateQuality(qc, projectId);
    },
  });
}

/** Delivering completes the project and starts (or creates) the warranty terms. */
export function useDeliverHandover(projectId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (keysDelivered?: boolean) => deliverHandover(projectId, keysDelivered),
    onSuccess:  (data) => {
      qc.setQueryData(QUALITY_KEYS.handover(projectId), data);
      return Promise.all([
        qc.invalidateQueries({ queryKey: QUALITY_KEYS.warranty(projectId) }),
        qc.invalidateQueries({ queryKey: BUILD_KEYS.projectDetails(projectId) }),
        qc.invalidateQueries({ queryKey: ["build", "projects"] }),
        qc.invalidateQueries({ queryKey: BUILD_KEYS.dashboard() }),
      ]);
    },
  });
}

export function useAcceptHandover(projectId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (req: { signerName: string; comment?: string | null }) => acceptHandover(projectId, req),
    onSuccess:  (data) => { qc.setQueryData(QUALITY_KEYS.handover(projectId), data); },
  });
}

// ── Warranty ──────────────────────────────────────────────────────────────────

export function useWarranty(projectId: string) {
  return useQuery({
    queryKey:  QUALITY_KEYS.warranty(projectId),
    queryFn:   () => fetchWarranty(projectId),
    enabled:   !!projectId,
    staleTime: 30_000,
  });
}

export function useSaveWarranty(projectId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ id, req }: { id?: string; req: SaveBuildWarrantyTermRequest }) =>
      id ? updateWarranty(id, req) : createWarranty(projectId, req),
    onSuccess:  () => qc.invalidateQueries({ queryKey: QUALITY_KEYS.warranty(projectId) }),
  });
}

export function useDeleteWarranty(projectId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => deleteWarranty(id),
    onSuccess:  () => qc.invalidateQueries({ queryKey: QUALITY_KEYS.warranty(projectId) }),
  });
}

export function useDefaultWarranty(projectId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: () => createDefaultWarranty(projectId),
    onSuccess:  () => qc.invalidateQueries({ queryKey: QUALITY_KEYS.warranty(projectId) }),
  });
}

// ── Assistance ────────────────────────────────────────────────────────────────

export function useAssistance(projectId: string, f: AssistanceFilters = {}) {
  return useQuery({
    queryKey:  QUALITY_KEYS.assistance(projectId, f),
    queryFn:   () => fetchAssistance(projectId, f),
    enabled:   !!projectId,
    staleTime: 30_000,
  });
}

export function useOpenAssistance(projectId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (req: OpenBuildAssistanceRequest) => openAssistance(projectId, req),
    onSuccess:  () => qc.invalidateQueries({ queryKey: QUALITY_KEYS.assistanceAll(projectId) }),
  });
}

export type AssistanceAction =
  | { kind: "triage"; warrantyCovered?: boolean | null; notes?: string | null }
  | { kind: "schedule"; scheduledFor: string }
  | { kind: "start" }
  | { kind: "resolve"; resolutionNotes: string }
  | { kind: "close" }
  | { kind: "reject"; reason: string }
  | { kind: "photo"; key: string };

function runAssistanceAction(id: string, a: AssistanceAction) {
  switch (a.kind) {
    case "triage": {
      const req: { warrantyCovered?: boolean | null; notes?: string | null } = { notes: a.notes ?? null };
      if (a.warrantyCovered !== undefined) req.warrantyCovered = a.warrantyCovered;
      return triageAssistance(id, req);
    }
    case "schedule": return scheduleAssistance(id, a.scheduledFor);
    case "start":    return startAssistance(id);
    case "resolve":  return resolveAssistance(id, a.resolutionNotes);
    case "close":    return closeAssistance(id);
    case "reject":   return rejectAssistance(id, a.reason);
    default:         return addAssistancePhoto(id, a.key);
  }
}

export function useAssistanceAction(projectId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ id, action }: { id: string; action: AssistanceAction }) => runAssistanceAction(id, action),
    onSuccess:  () => qc.invalidateQueries({ queryKey: QUALITY_KEYS.assistanceAll(projectId) }),
  });
}
