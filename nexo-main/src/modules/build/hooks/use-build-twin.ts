import { keepPreviousData, useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  bulkBind, createBinding, createZone, deleteBinding, deleteRealityCapture, deleteZone, fetchBindings, fetchBuildSettings,
  fetchElementEvents, fetchElements, fetchModelVersion, fetchModelViewer, fetchModels, fetchProjectZones, fetchRealityCaptures,
  fetchTwinOverview, fetchTwinState, fetchVersionZones, fetchZoneStates, recordElementEvents, reprocessModelVersion, setRealityCaptureVisibility,
  updateRealityCapture, updateZone,
  type BulkBuildBindingRequest, type CreateBuildBindingRequest, type ElementFilters, type RealityCaptureFilters,
  type RecordBuildElementEventsRequest, type SaveBuildZoneRequest, type UpdateRealityCaptureRequest,
} from "../api/build-twin.api";
import { versionPollInterval } from "../lib/twin";

export const TWIN_KEYS = {
  settings:  ()                                         => ["build", "settings"] as const,
  overview:  (projectId: string)                        => ["build", "twin", projectId, "overview"] as const,
  models:    (projectId: string)                        => ["build", "twin", projectId, "models"] as const,
  version:   (versionId: string)                        => ["build", "twin", "version", versionId] as const,
  viewer:    (versionId: string)                        => ["build", "twin", "version", versionId, "viewer"] as const,
  state:     (versionId: string, date: string)          => ["build", "twin", "version", versionId, "state", date] as const,
  elements:  (versionId: string, f: ElementFilters)     => ["build", "twin", "version", versionId, "elements", f] as const,
  events:    (versionId: string, externalId: string)    => ["build", "twin", "version", versionId, "events", externalId] as const,
  vzones:    (versionId: string)                        => ["build", "twin", "version", versionId, "zones"] as const,
  zones:     (projectId: string)                        => ["build", "twin", projectId, "zones"] as const,
  zoneStates: (projectId: string, date: string)         => ["build", "twin", projectId, "zone-states", date] as const,
  bindings:  (projectId: string)                        => ["build", "twin", projectId, "bindings"] as const,
  captures:  (projectId: string, f: RealityCaptureFilters) => ["build", "twin", projectId, "captures", f] as const,
} as const;

/** Anything derived from bindings/events/zones of the project (overview, states, elements). */
function invalidateTwin(qc: ReturnType<typeof useQueryClient>, projectId: string) {
  void qc.invalidateQueries({ queryKey: ["build", "twin", projectId] });
  void qc.invalidateQueries({ queryKey: ["build", "twin", "version"] });
}

export function useBuildSettings() {
  return useQuery({ queryKey: TWIN_KEYS.settings(), queryFn: fetchBuildSettings, staleTime: 60_000 });
}

export function useTwinOverview(projectId: string) {
  return useQuery({ queryKey: TWIN_KEYS.overview(projectId), queryFn: () => fetchTwinOverview(projectId), enabled: !!projectId });
}

export function useModels(projectId: string) {
  return useQuery({ queryKey: TWIN_KEYS.models(projectId), queryFn: () => fetchModels(projectId), enabled: !!projectId });
}

/** One version, polled every 3 s while Queued/Processing; stops on Ready/Failed/Rejected. */
export function useModelVersion(versionId: string | null) {
  return useQuery({
    queryKey: TWIN_KEYS.version(versionId ?? ""),
    queryFn:  () => fetchModelVersion(versionId!),
    enabled:  !!versionId,
    refetchInterval: (q) => versionPollInterval(q.state.data?.status),
  });
}

export function useModelViewer(versionId: string | null, enabled = true) {
  return useQuery({
    queryKey: TWIN_KEYS.viewer(versionId ?? ""),
    queryFn:  () => fetchModelViewer(versionId!),
    enabled:  !!versionId && enabled,
    // signed URL lives 15 min
    staleTime: 10 * 60_000,
  });
}

/** 4D state of a version on a date — cached per date, so moving the slider back is instant. */
export function useTwinState(versionId: string | null, date: string) {
  return useQuery({
    queryKey: TWIN_KEYS.state(versionId ?? "", date),
    queryFn:  () => fetchTwinState(versionId!, date),
    enabled:  !!versionId && !!date,
    staleTime: 60_000,
    placeholderData: keepPreviousData,
  });
}

export function useElements(versionId: string | null, filters: ElementFilters) {
  return useQuery({
    queryKey: TWIN_KEYS.elements(versionId ?? "", filters),
    queryFn:  () => fetchElements(versionId!, filters),
    enabled:  !!versionId,
    placeholderData: keepPreviousData,
  });
}

export function useElementEvents(versionId: string | null, externalId: string | null) {
  return useQuery({
    queryKey: TWIN_KEYS.events(versionId ?? "", externalId ?? ""),
    queryFn:  () => fetchElementEvents(versionId!, externalId!),
    enabled:  !!versionId && !!externalId,
  });
}

export function useVersionZones(versionId: string | null) {
  return useQuery({ queryKey: TWIN_KEYS.vzones(versionId ?? ""), queryFn: () => fetchVersionZones(versionId!), enabled: !!versionId });
}

export function useProjectZones(projectId: string) {
  return useQuery({ queryKey: TWIN_KEYS.zones(projectId), queryFn: () => fetchProjectZones(projectId), enabled: !!projectId });
}

export function useBindings(projectId: string) {
  return useQuery({ queryKey: TWIN_KEYS.bindings(projectId), queryFn: () => fetchBindings(projectId), enabled: !!projectId });
}

export function useRealityCaptures(projectId: string, filters: RealityCaptureFilters) {
  return useQuery({ queryKey: TWIN_KEYS.captures(projectId, filters), queryFn: () => fetchRealityCaptures(projectId, filters),
    enabled: !!projectId });
}

// ── Mutations ─────────────────────────────────────────────────────────────────

export function useReprocessVersion(projectId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (versionId: string) => reprocessModelVersion(versionId),
    onSuccess: (v) => { qc.setQueryData(TWIN_KEYS.version(v.id), v); invalidateTwin(qc, projectId); },
  });
}

export function useSaveZone(projectId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ id, req }: { id?: string; req: SaveBuildZoneRequest }) => id ? updateZone(id, req) : createZone(projectId, req),
    onSuccess: () => invalidateTwin(qc, projectId),
  });
}

export function useDeleteZone(projectId: string) {
  const qc = useQueryClient();
  return useMutation({ mutationFn: deleteZone, onSuccess: () => invalidateTwin(qc, projectId) });
}

export function useCreateBinding(projectId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (req: CreateBuildBindingRequest) => createBinding(projectId, req),
    onSuccess: () => invalidateTwin(qc, projectId),
  });
}

export function useBulkBind(projectId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (req: BulkBuildBindingRequest) => bulkBind(projectId, req),
    onSuccess: () => invalidateTwin(qc, projectId),
  });
}

export function useDeleteBinding(projectId: string) {
  const qc = useQueryClient();
  return useMutation({ mutationFn: deleteBinding, onSuccess: () => invalidateTwin(qc, projectId) });
}

export function useRecordElementEvents(projectId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (req: RecordBuildElementEventsRequest) => recordElementEvents(projectId, req),
    onSuccess: () => invalidateTwin(qc, projectId),
  });
}

export function useUpdateRealityCapture(projectId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ id, req }: { id: string; req: UpdateRealityCaptureRequest }) => updateRealityCapture(id, req),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["build", "twin", projectId, "captures"] }),
  });
}

export function useSetCaptureVisibility(projectId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ id, clientVisible }: { id: string; clientVisible: boolean }) => setRealityCaptureVisibility(id, clientVisible),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["build", "twin", projectId, "captures"] }),
  });
}

export function useDeleteRealityCapture(projectId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: deleteRealityCapture,
    onSuccess: () => qc.invalidateQueries({ queryKey: ["build", "twin", projectId, "captures"] }),
  });
}

/** After an upload (done outside React Query to report progress), refresh the twin. */
export function useInvalidateTwin(projectId: string) {
  const qc = useQueryClient();
  return () => invalidateTwin(qc, projectId);
}

/**
 * One element's details (category, zone, quantities, stage, state on the date) by its exact external id
 * (GET elements?externalId= — a direct lookup, no page scan). `null` when the version has no such element.
 */
export function useElementLookup(versionId: string | null, externalId: string | null, date: string) {
  return useQuery({
    queryKey: [...TWIN_KEYS.elements(versionId ?? "", { externalId: externalId ?? "", date }), "lookup"],
    enabled: !!versionId && !!externalId,
    queryFn: async () => {
      const res = await fetchElements(versionId!, { externalId: externalId!, date, take: 1 });
      return res.items.find((e) => e.externalId === externalId) ?? null;
    },
  });
}

/** Manual zones' state on a date through the zone bindings (GET twin/zones/state) — no model version needed. */
export function useZoneStates(projectId: string, date: string, enabled = true) {
  return useQuery({
    queryKey: TWIN_KEYS.zoneStates(projectId, date),
    queryFn: () => fetchZoneStates(projectId, date),
    enabled: !!projectId && enabled,
    retry: false,
  });
}
