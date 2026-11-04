import { apiClient, type UploadProgressHandler } from "@/services/api-client";

// Wave 4 — Gêmeo digital (docs/build/internal-api.md "# Wave 4", docs/build/digital-twin.md).
// Storage keys never come back: files only as short-lived signed URLs.

// ── Enums ─────────────────────────────────────────────────────────────────────

export type BuildModelDiscipline = "Arquitetura" | "Estrutura" | "Hidraulica" | "Eletrica" | "Outro";
export type BuildModelSourceFormat = "Ifc" | "Glb" | "Gltf" | "Rvt" | "Dwg" | "Dxf" | "Pdf" | "Image";
export type BuildGeometrySource = "NativeBim" | "Converted3D" | "AssistedMapping" | "SimplifiedRepresentation";
export type BuildViewerSource = "None" | "Uploaded" | "ConvertedServer" | "ConvertedBrowser";
export type BuildModelVersionStatus = "Uploaded" | "Queued" | "Processing" | "Ready" | "Failed" | "Rejected";
export type BuildElementState = "NotStarted" | "InProgress" | "Completed" | "Delayed" | "Blocked" | "NeedsReview";
export type BuildZoneKind =
  | "Site" | "Building" | "Storey" | "Space"                              // from the model
  | "Floor" | "Area" | "Room" | "Facade" | "Roof" | "External";           // manual
export type BuildManualZoneKind = "Floor" | "Area" | "Room" | "Facade" | "Roof" | "External";
export type BuildBindingScope = "Element" | "Zone" | "Category";
export type BuildRealityCaptureType = "Photo" | "Photo360" | "Video" | "Drone" | "PointCloud";

// ── DTOs ──────────────────────────────────────────────────────────────────────

export interface BuildTenantSettingsDto {
  tenantId:            string;
  build3DEnabled:      boolean;
  buildAiEnabled:      boolean;
  buildVisionEnabled:  boolean;
  buildPredictEnabled: boolean;
  /** False when no row exists yet (the defaults apply). */
  customized:          boolean;
  updatedByName:       string | null;
  updatedAt:           string | null;
}

export interface BuildModelVersionDto {
  id:             string;
  modelId:        string;
  number:         number;
  sourceFormat:   BuildModelSourceFormat;
  sourceFileName: string;
  sizeBytes:      number;
  sha256:         string | null;
  geometrySource: BuildGeometrySource;
  viewerSource:   BuildViewerSource;
  hasViewer:      boolean;
  status:         BuildModelVersionStatus;
  statusMessage:  string | null;
  elementCount:   number;
  storeyCount:    number;
  schema:         string | null;
  layers:         string[] | null;
  processedAt:    string | null;
  createdAt:      string;
}

export interface BuildModelDto {
  id:               string;
  projectId:        string;
  name:             string;
  discipline:       BuildModelDiscipline;
  currentVersionId: string | null;
  /** Newest first. */
  versions:         BuildModelVersionDto[];
  createdAt:        string;
}

export interface BuildModelUploadResultDto {
  model:   BuildModelDto;
  version: BuildModelVersionDto;
}

export interface BuildModelViewerDto {
  versionId:      string;
  viewerUrl:      string | null;
  expiresAt:      string | null;
  geometrySource: BuildGeometrySource;
  viewerSource:   BuildViewerSource;
}

export interface BuildModelElementDto {
  externalId:    string;
  category:      string;
  name:          string | null;
  typeName:      string | null;
  material:      string | null;
  zoneId:        string | null;
  zoneName:      string | null;
  /** Quantities from the IFC (netVolume, netArea, length, count…). */
  properties:    Record<string, unknown> | null;
  state:         BuildElementState | null;
  stageId:       string | null;
  explicitState: boolean;
}

export interface BuildModelElementPageDto {
  items:      BuildModelElementDto[];
  total:      number;
  skip:       number;
  take:       number;
  categories: string[];
}

export interface BuildZonePoint { x: number; y: number }

/** Meters typed by the user — never generated. */
export interface BuildSimplifiedBox { width: number; depth: number; height: number; elevation: number }

export interface BuildModelZoneDto {
  id:             string;
  modelVersionId: string | null;
  kind:           BuildZoneKind;
  name:           string;
  externalId:     string | null;
  parentZoneId:   string | null;
  elevation:      number | null;
  order:          number;
  outline:        BuildZonePoint[] | null;
  sheetVersionId: string | null;
  simplifiedBox:  BuildSimplifiedBox | null;
  elementCount:   number;
}

export interface SaveBuildZoneRequest {
  kind:            BuildManualZoneKind;
  name:            string;
  order?:          number;
  parentZoneId?:   string | null;
  outline?:        BuildZonePoint[] | null;
  sheetVersionId?: string | null;
  simplifiedBox?:  BuildSimplifiedBox | null;
}

export interface BuildTwinStateDto {
  date:       string;
  counts:     Partial<Record<BuildElementState, number>>;
  /** External ids per state. */
  states:     Partial<Record<BuildElementState, string[]>>;
  /** Zone ids per state (model zones and manual zones). */
  zoneStates: Partial<Record<BuildElementState, string[]>>;
  unbound:    number;
  total:      number;
}

export interface BuildModelStageBindingDto {
  id:                string;
  stageId:           string;
  stageName:         string;
  scope:             BuildBindingScope;
  modelVersionId:    string | null;
  elementExternalId: string | null;
  zoneId:            string | null;
  category:          string | null;
  source:            string;
  createdAt:         string;
}

export interface CreateBuildBindingRequest {
  stageId:            string;
  scope:              BuildBindingScope;
  modelVersionId?:    string | null;
  elementExternalId?: string | null;
  zoneId?:            string | null;
  category?:          string | null;
}

export interface BulkBuildBindingRequest {
  stageId:          string;
  modelVersionId:   string;
  category?:        string | null;
  zoneId?:          string | null;
  externalIds?:     string[] | null;
  replaceExisting?: boolean;
}

export interface BulkBuildBindingResultDto { matched: number; created: number; replaced: number }

export interface RecordBuildElementEventsRequest {
  modelVersionId: string;
  externalIds:    string[];
  state:          BuildElementState;
  note?:          string | null;
  effectiveDate?: string | null;
}

export interface BuildElementEventDto {
  id:                string;
  modelVersionId:    string;
  elementExternalId: string;
  state:             BuildElementState;
  source:            string;
  sourceRef:         string | null;
  note:              string | null;
  effectiveDate:     string;
  recordedByName:    string;
  recordedAt:        string;
}

export interface BuildTwinStageDto {
  stageId: string; name: string; order: number; progressPercent: number; status: string; boundElements: number;
}

export interface BuildTwinOverviewDto {
  projectId:              string;
  build3DEnabled:         boolean;
  geometrySource:         BuildGeometrySource | "None";
  models:                 BuildModelDto[];
  manualZones:            number;
  totalElements:          number;
  boundElements:          number;
  bindingCoveragePercent: number;
  pendingJobs:            number;
  stages:                 BuildTwinStageDto[];
}

export interface BuildRealityCaptureDto {
  id:                string;
  projectId:         string;
  type:              BuildRealityCaptureType;
  capturedAt:        string;
  stageId:           string | null;
  zoneId:            string | null;
  elementExternalId: string | null;
  title:             string;
  notes:             string | null;
  contentType:       string;
  sizeBytes:         number;
  clientVisible:     boolean;
  url:               string | null;
  urlExpiresAt:      string | null;
  /** False for point clouds (stored, not processed). */
  processed:         boolean;
  createdAt:         string;
}

export interface CreateRealityCaptureInput {
  file:               File;
  type:               BuildRealityCaptureType;
  title:              string;
  capturedAt?:        string | null;
  stageId?:           string | null;
  zoneId?:            string | null;
  elementExternalId?: string | null;
  notes?:             string | null;
  clientVisible?:     boolean;
}

export interface UpdateRealityCaptureRequest {
  title:              string;
  notes?:             string | null;
  capturedAt?:        string | null;
  stageId?:           string | null;
  zoneId?:            string | null;
  elementExternalId?: string | null;
}

export interface RealityCaptureFilters {
  type?: BuildRealityCaptureType | ""; stageId?: string; zoneId?: string; elementExternalId?: string; from?: string; to?: string;
}

/** Short-lived (15 min) signed URL of a version's private source file (company users only, audited). */
export interface BuildModelSourceDto {
  versionId:    string;
  fileName:     string;
  sourceFormat: BuildModelSourceFormat;
  sizeBytes:    number;
  sha256:       string | null;
  url:          string;
  expiresAt:    string;
}

/** State of the project's manual zones on a date, through the zone bindings (no model version needed). */
export interface BuildZoneStateDto {
  zoneId:       string;
  name:         string;
  kind:         BuildZoneKind;
  parentZoneId: string | null;
  state:        BuildElementState | null;
  stageId:      string | null;
  stageName:    string | null;
}

export interface BuildZoneStatesDto {
  date:   string;
  zones:  BuildZoneStateDto[];
  counts: Record<string, number>;
}

export interface ElementFilters {
  category?: string; zoneId?: string; state?: BuildElementState | "Unbound" | ""; date?: string; skip?: number; take?: number;
  /** Exact external ids, comma-separated (up to 200 → else 422 too_many_ids). */
  externalId?: string;
}

const B = "/v1/build";

function qs(params: Record<string, string | number | undefined | null>): string {
  const q = new URLSearchParams();
  for (const [k, v] of Object.entries(params)) if (v !== undefined && v !== null && v !== "") q.set(k, String(v));
  const s = q.toString();
  return s ? `?${s}` : "";
}

// ── Settings / overview ───────────────────────────────────────────────────────

export const fetchBuildSettings = () => apiClient.get<BuildTenantSettingsDto>(`${B}/settings`);
export const fetchTwinOverview = (projectId: string) => apiClient.get<BuildTwinOverviewDto>(`${B}/projects/${projectId}/twin`);

// ── Models ────────────────────────────────────────────────────────────────────

export const fetchModels = (projectId: string) => apiClient.get<BuildModelDto[]>(`${B}/projects/${projectId}/models`);
export const fetchModelVersion = (versionId: string) => apiClient.get<BuildModelVersionDto>(`${B}/models/versions/${versionId}`);
export const fetchModelViewer = (versionId: string) => apiClient.get<BuildModelViewerDto>(`${B}/models/versions/${versionId}/viewer`);
export const reprocessModelVersion = (versionId: string) =>
  apiClient.post<BuildModelVersionDto>(`${B}/models/versions/${versionId}/reprocess`);

export function uploadModel(projectId: string, input: { file: File; name?: string; discipline?: BuildModelDiscipline },
  onProgress?: UploadProgressHandler) {
  const form = new FormData();
  form.append("file", input.file);
  if (input.name?.trim()) form.append("name", input.name.trim());
  if (input.discipline) form.append("discipline", input.discipline);
  return apiClient.upload<BuildModelUploadResultDto>("POST", `${B}/projects/${projectId}/models`, form, onProgress);
}

export function uploadModelVersion(modelId: string, file: File, onProgress?: UploadProgressHandler) {
  const form = new FormData();
  form.append("file", file);
  return apiClient.upload<BuildModelUploadResultDto>("POST", `${B}/models/${modelId}/versions`, form, onProgress);
}

/** The GLB converted in the browser (node names = IFC GlobalIds). */
export function uploadViewer(versionId: string, form: FormData, onProgress?: UploadProgressHandler) {
  return apiClient.upload<BuildModelVersionDto>("PUT", `${B}/models/versions/${versionId}/viewer`, form, onProgress);
}

// ── Elements, zones, state ────────────────────────────────────────────────────

export const fetchElements = (versionId: string, f: ElementFilters = {}) =>
  apiClient.get<BuildModelElementPageDto>(`${B}/models/versions/${versionId}/elements${qs({
    category: f.category, zoneId: f.zoneId, state: f.state, date: f.date, skip: f.skip, take: f.take, externalId: f.externalId,
  })}`);

/** Signed URL of the original file: 422 source_not_stored (RVT/DWG refused), 503 storage_unavailable. */
export const fetchModelSource = (versionId: string) =>
  apiClient.get<BuildModelSourceDto>(`${B}/models/versions/${versionId}/source`);

export const fetchZoneStates = (projectId: string, date?: string) =>
  apiClient.get<BuildZoneStatesDto>(`${B}/projects/${projectId}/twin/zones/state${qs({ date })}`);

export const fetchElementEvents = (versionId: string, externalId: string) =>
  apiClient.get<BuildElementEventDto[]>(`${B}/models/versions/${versionId}/elements/${encodeURIComponent(externalId)}/events`);

export const fetchVersionZones = (versionId: string) => apiClient.get<BuildModelZoneDto[]>(`${B}/models/versions/${versionId}/zones`);

export const fetchTwinState = (versionId: string, date?: string) =>
  apiClient.get<BuildTwinStateDto>(`${B}/models/versions/${versionId}/state${qs({ date })}`);

export const fetchProjectZones = (projectId: string) => apiClient.get<BuildModelZoneDto[]>(`${B}/projects/${projectId}/zones`);
export const createZone = (projectId: string, req: SaveBuildZoneRequest) =>
  apiClient.post<BuildModelZoneDto>(`${B}/projects/${projectId}/zones`, req);
export const updateZone = (zoneId: string, req: SaveBuildZoneRequest) => apiClient.put<BuildModelZoneDto>(`${B}/zones/${zoneId}`, req);
export const deleteZone = (zoneId: string) => apiClient.delete<void>(`${B}/zones/${zoneId}`);

// ── Bindings and element states ───────────────────────────────────────────────

export const fetchBindings = (projectId: string) =>
  apiClient.get<BuildModelStageBindingDto[]>(`${B}/projects/${projectId}/twin/bindings`);
export const createBinding = (projectId: string, req: CreateBuildBindingRequest) =>
  apiClient.post<BuildModelStageBindingDto>(`${B}/projects/${projectId}/twin/bindings`, req);
export const bulkBind = (projectId: string, req: BulkBuildBindingRequest) =>
  apiClient.post<BulkBuildBindingResultDto>(`${B}/projects/${projectId}/twin/bindings/bulk`, req);
export const deleteBinding = (bindingId: string) => apiClient.delete<void>(`${B}/twin/bindings/${bindingId}`);
export const recordElementEvents = (projectId: string, req: RecordBuildElementEventsRequest) =>
  apiClient.post<BuildElementEventDto[]>(`${B}/projects/${projectId}/twin/element-events`, req);

// ── Reality capture ───────────────────────────────────────────────────────────

export const fetchRealityCaptures = (projectId: string, f: RealityCaptureFilters = {}) =>
  apiClient.get<BuildRealityCaptureDto[]>(`${B}/projects/${projectId}/reality-captures${qs({
    type: f.type, stageId: f.stageId, zoneId: f.zoneId, elementExternalId: f.elementExternalId, from: f.from, to: f.to,
  })}`);

export function uploadRealityCapture(projectId: string, input: CreateRealityCaptureInput, onProgress?: UploadProgressHandler) {
  const form = new FormData();
  form.append("file", input.file);
  form.append("type", input.type);
  form.append("title", input.title);
  if (input.capturedAt) form.append("capturedAt", input.capturedAt);
  if (input.stageId) form.append("stageId", input.stageId);
  if (input.zoneId) form.append("zoneId", input.zoneId);
  if (input.elementExternalId?.trim()) form.append("elementExternalId", input.elementExternalId.trim());
  if (input.notes?.trim()) form.append("notes", input.notes.trim());
  form.append("clientVisible", input.clientVisible ? "true" : "false");
  return apiClient.upload<BuildRealityCaptureDto>("POST", `${B}/projects/${projectId}/reality-captures`, form, onProgress);
}

export const updateRealityCapture = (id: string, req: UpdateRealityCaptureRequest) =>
  apiClient.put<BuildRealityCaptureDto>(`${B}/reality-captures/${id}`, req);
export const setRealityCaptureVisibility = (id: string, clientVisible: boolean) =>
  apiClient.put<BuildRealityCaptureDto>(`${B}/reality-captures/${id}/client-visibility`, { clientVisible });
export const deleteRealityCapture = (id: string) => apiClient.delete<void>(`${B}/reality-captures/${id}`);
