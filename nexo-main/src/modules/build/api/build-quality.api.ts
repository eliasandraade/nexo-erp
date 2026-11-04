import { ApiError, apiClient } from "@/services/api-client";

// Wave 3 — Qualidade e pós-obra (internal API). Enums travel as their string names; days are yyyy-MM-dd and
// instants ISO (UTC). See docs/build/internal-api.md (# Wave 3).

// ── Checklist templates ──────────────────────────────────────────────────────

export interface BuildChecklistItemRequest {
  text:           string;
  /** Default true on the server. */
  required?:      boolean;
  requiresPhoto?: boolean;
}

export interface SaveBuildChecklistTemplateRequest {
  name:           string;
  category?:      string | null;
  stageNameHint?: string | null;
  items?:         BuildChecklistItemRequest[];
}

export interface BuildChecklistTemplateItemDto {
  id:            string;
  order:         number;
  text:          string;
  required:      boolean;
  requiresPhoto: boolean;
}

export interface BuildChecklistTemplateDto {
  id:            string;
  name:          string;
  category:      string | null;
  stageNameHint: string | null;
  items:         BuildChecklistTemplateItemDto[];
  createdAt:     string;
  updatedAt:     string;
}

export const fetchChecklistTemplates = (): Promise<BuildChecklistTemplateDto[]> =>
  apiClient.get("/v1/build/checklist-templates");

export const createChecklistTemplate = (req: SaveBuildChecklistTemplateRequest): Promise<BuildChecklistTemplateDto> =>
  apiClient.post("/v1/build/checklist-templates", req);

/** Replaces the template, items included; inspections already created keep their own copy. */
export const updateChecklistTemplate = (id: string, req: SaveBuildChecklistTemplateRequest): Promise<BuildChecklistTemplateDto> =>
  apiClient.put(`/v1/build/checklist-templates/${id}`, req);

export const deleteChecklistTemplate = (id: string): Promise<void> =>
  apiClient.delete(`/v1/build/checklist-templates/${id}`);

// ── Inspections ──────────────────────────────────────────────────────────────

export type BuildInspectionKind = "Quality" | "ClientWalkthrough";
export type BuildInspectionStatus = "Planned" | "InProgress" | "Passed" | "PassedWithRemarks" | "Failed" | "Cancelled";
export type BuildInspectionItemResult = "Pending" | "Ok" | "NotOk" | "NotApplicable";
export type BuildNcSeverity = "Low" | "Medium" | "High" | "Critical";

export interface BuildInspectionItemDto {
  id:              string;
  order:           number;
  text:            string;
  required:        boolean;
  requiresPhoto:   boolean;
  result:          BuildInspectionItemResult;
  comment:         string | null;
  severity:        BuildNcSeverity | null;
  photoKeys:       string[];
  /** Signed URLs (about 15 minutes), same order as photoKeys. */
  photoUrls:       string[];
  nonConformityId: string | null;
}

export interface BuildInspectionDto {
  id:              string;
  projectId:       string;
  kind:            BuildInspectionKind;
  stageId:         string | null;
  stageName:       string | null;
  templateId:      string | null;
  title:           string;
  scheduledFor:    string | null;
  status:          BuildInspectionStatus;
  inspectorName:   string | null;
  startedAt:       string | null;
  completedAt:     string | null;
  completedByName: string | null;
  notes:           string | null;
  okItems:         number;
  notOkItems:      number;
  pendingItems:    number;
  items:           BuildInspectionItemDto[];
  createdAt:       string;
  updatedAt:       string;
}

export interface CreateBuildInspectionRequest {
  title:          string;
  stageId?:       string | null;
  templateId?:    string | null;
  scheduledFor?:  string | null;
  inspectorName?: string | null;
  notes?:         string | null;
  items?:         BuildChecklistItemRequest[];
}

export interface UpdateBuildInspectionRequest {
  title:          string;
  stageId?:       string | null;
  scheduledFor?:  string | null;
  inspectorName?: string | null;
  notes?:         string | null;
}

export interface RecordBuildInspectionItemRequest {
  result:     BuildInspectionItemResult;
  comment?:   string | null;
  photoKeys?: string[];
  /** Only for NotOk: severity of the NC the item opens on completion. */
  severity?:  BuildNcSeverity | null;
}

export interface InspectionFilters {
  kind?:   BuildInspectionKind;
  status?: BuildInspectionStatus;
}

function query(params: Record<string, string | boolean | undefined>): string {
  const q = new URLSearchParams();
  for (const [k, v] of Object.entries(params)) if (v !== undefined && v !== "") q.set(k, String(v));
  const s = q.toString();
  return s ? `?${s}` : "";
}

export const fetchInspections = (projectId: string, f: InspectionFilters = {}): Promise<BuildInspectionDto[]> =>
  apiClient.get(`/v1/build/projects/${projectId}/inspections${query({ kind: f.kind, status: f.status })}`);

export const createInspection = (projectId: string, req: CreateBuildInspectionRequest): Promise<BuildInspectionDto> =>
  apiClient.post(`/v1/build/projects/${projectId}/inspections`, req);

export const fetchInspection = (id: string): Promise<BuildInspectionDto> =>
  apiClient.get(`/v1/build/inspections/${id}`);

export const updateInspection = (id: string, req: UpdateBuildInspectionRequest): Promise<BuildInspectionDto> =>
  apiClient.put(`/v1/build/inspections/${id}`, req);

export const startInspection = (id: string): Promise<BuildInspectionDto> =>
  apiClient.post(`/v1/build/inspections/${id}/start`);

export const recordInspectionItem = (id: string, itemId: string, req: RecordBuildInspectionItemRequest): Promise<BuildInspectionDto> =>
  apiClient.put(`/v1/build/inspections/${id}/items/${itemId}`, req);

export const completeInspection = (id: string, notes?: string): Promise<BuildInspectionDto> =>
  apiClient.post(`/v1/build/inspections/${id}/complete`, notes ? { notes } : {});

export const cancelInspection = (id: string): Promise<BuildInspectionDto> =>
  apiClient.post(`/v1/build/inspections/${id}/cancel`);

// ── Non-conformities ─────────────────────────────────────────────────────────

export type BuildNcStatus = "Open" | "InTreatment" | "Resolved" | "Verified" | "Cancelled";

export interface BuildNonConformityDto {
  id:                 string;
  projectId:          string;
  number:             number;
  stageId:            string | null;
  stageName:          string | null;
  inspectionId:       string | null;
  inspectionItemId:   string | null;
  title:              string;
  description:        string | null;
  severity:           BuildNcSeverity;
  status:             BuildNcStatus;
  responsibleName:    string | null;
  dueDate:            string | null;
  isOverdue:          boolean;
  rootCause:          string | null;
  correctiveAction:   string | null;
  photoKeys:          string[];
  photoUrls:          string[];
  /** Informative only: nothing financial is created. */
  costImpact:         number | null;
  resolvedAt:         string | null;
  resolvedByName:     string | null;
  verifiedAt:         string | null;
  verifiedByName:     string | null;
  verifiedBySameUser: boolean;
  cancelReason:       string | null;
  createdByName:      string;
  createdAt:          string;
  updatedAt:          string;
}

export interface SaveBuildNonConformityRequest {
  title:             string;
  description?:      string | null;
  severity?:         BuildNcSeverity | null;
  stageId?:          string | null;
  responsibleName?:  string | null;
  dueDate?:          string | null;
  costImpact?:       number | null;
  photoKeys?:        string[];
  rootCause?:        string | null;
  correctiveAction?: string | null;
}

export interface NonConformityFilters {
  status?:   BuildNcStatus;
  severity?: BuildNcSeverity;
  overdue?:  boolean;
}

export const fetchNonConformities = (projectId: string, f: NonConformityFilters = {}): Promise<BuildNonConformityDto[]> =>
  apiClient.get(`/v1/build/projects/${projectId}/non-conformities${query({
    status: f.status, severity: f.severity, overdue: f.overdue ? true : undefined,
  })}`);

export const createNonConformity = (projectId: string, req: SaveBuildNonConformityRequest): Promise<BuildNonConformityDto> =>
  apiClient.post(`/v1/build/projects/${projectId}/non-conformities`, req);

export const fetchNonConformity = (id: string): Promise<BuildNonConformityDto> =>
  apiClient.get(`/v1/build/non-conformities/${id}`);

export const updateNonConformity = (id: string, req: SaveBuildNonConformityRequest): Promise<BuildNonConformityDto> =>
  apiClient.put(`/v1/build/non-conformities/${id}`, req);

export const startNonConformity = (id: string): Promise<BuildNonConformityDto> =>
  apiClient.post(`/v1/build/non-conformities/${id}/start`);

export const resolveNonConformity = (
  id: string, req: { correctiveAction?: string | null; rootCause?: string | null },
): Promise<BuildNonConformityDto> =>
  apiClient.post(`/v1/build/non-conformities/${id}/resolve`, req);

export const verifyNonConformity = (id: string): Promise<BuildNonConformityDto> =>
  apiClient.post(`/v1/build/non-conformities/${id}/verify`);

export const reopenNonConformity = (id: string): Promise<BuildNonConformityDto> =>
  apiClient.post(`/v1/build/non-conformities/${id}/reopen`);

export const cancelNonConformity = (id: string, reason: string): Promise<BuildNonConformityDto> =>
  apiClient.post(`/v1/build/non-conformities/${id}/cancel`, { reason });

// ── Quality summary ──────────────────────────────────────────────────────────

export interface BuildQualitySummaryDto {
  projectId:                     string;
  inspectionsByStatus:           Partial<Record<BuildInspectionStatus, number>>;
  completedInspections:          number;
  /** (Passed + PassedWithRemarks) / completed, in %; null without any completed inspection. */
  passRate:                      number | null;
  openNonConformities:           number;
  openNonConformitiesBySeverity: Partial<Record<BuildNcSeverity, number>>;
  overdueNonConformities:        number;
  awaitingVerification:          number;
  criticalOpen:                  number;
}

export const fetchQualitySummary = (projectId: string): Promise<BuildQualitySummaryDto> =>
  apiClient.get(`/v1/build/projects/${projectId}/quality/summary`);

// ── Handover ─────────────────────────────────────────────────────────────────

export type BuildHandoverStatus = "Scheduled" | "InspectionDone" | "Delivered";
export type BuildChannel = "Internal" | "Portal";

export interface BuildHandoverDocumentDto {
  name:    string;
  fileKey: string | null;
  fileUrl: string | null;
}

export interface BuildHandoverDto {
  id:                          string;
  projectId:                   string;
  status:                      BuildHandoverStatus;
  scheduledFor:                string | null;
  walkthroughInspectionId:     string | null;
  walkthroughStatus:           BuildInspectionStatus | null;
  deliveredAt:                 string | null;
  deliveredByName:             string | null;
  keysDelivered:               boolean;
  documents:                   BuildHandoverDocumentDto[];
  notes:                       string | null;
  acceptedAt:                  string | null;
  acceptedByName:              string | null;
  acceptanceChannel:           BuildChannel | null;
  acceptanceComment:           string | null;
  projectStatus:               string;
  projectStatusNote:           string | null;
  openCriticalNonConformities: number;
  createdAt:                   string;
  updatedAt:                   string;
}

export interface SaveBuildHandoverRequest {
  scheduledFor?:  string | null;
  keysDelivered?: boolean | null;
  documents?:     Array<{ name: string; fileKey?: string | null }>;
  notes?:         string | null;
}

export interface CreateBuildWalkthroughRequest {
  title?:         string | null;
  templateId?:    string | null;
  scheduledFor?:  string | null;
  inspectorName?: string | null;
  items?:         BuildChecklistItemRequest[];
}

/** Null while the handover was not scheduled yet (the server answers 404). */
export async function fetchHandover(projectId: string): Promise<BuildHandoverDto | null> {
  try {
    return await apiClient.get<BuildHandoverDto>(`/v1/build/projects/${projectId}/handover`);
  } catch (e) {
    if (e instanceof ApiError && e.status === 404) return null;
    throw e;
  }
}

export const saveHandover = (projectId: string, req: SaveBuildHandoverRequest): Promise<BuildHandoverDto> =>
  apiClient.put(`/v1/build/projects/${projectId}/handover`, req);

export const createWalkthrough = (projectId: string, req: CreateBuildWalkthroughRequest): Promise<BuildInspectionDto> =>
  apiClient.post(`/v1/build/projects/${projectId}/handover/walkthrough`, req);

export const deliverHandover = (projectId: string, keysDelivered?: boolean): Promise<BuildHandoverDto> =>
  apiClient.post(`/v1/build/projects/${projectId}/handover/deliver`, keysDelivered === undefined ? {} : { keysDelivered });

export const acceptHandover = (projectId: string, req: { signerName: string; comment?: string | null }): Promise<BuildHandoverDto> =>
  apiClient.post(`/v1/build/projects/${projectId}/handover/accept`, req);

// ── Warranty ─────────────────────────────────────────────────────────────────

export interface BuildWarrantyTermDto {
  id:          string;
  projectId:   string;
  system:      string;
  months:      number;
  /** Null until the delivery (the term then starts on the delivery date). */
  startsAt:    string | null;
  /** Last covered day, inclusive. */
  expiresAt:   string | null;
  isActive:    boolean;
  description: string | null;
  isDefault:   boolean;
}

export interface SaveBuildWarrantyTermRequest {
  system:       string;
  months:       number;
  startsAt?:    string | null;
  description?: string | null;
}

export const fetchWarranty = (projectId: string): Promise<BuildWarrantyTermDto[]> =>
  apiClient.get(`/v1/build/projects/${projectId}/warranty`);

export const createWarranty = (projectId: string, req: SaveBuildWarrantyTermRequest): Promise<BuildWarrantyTermDto> =>
  apiClient.post(`/v1/build/projects/${projectId}/warranty`, req);

/** The reference set (not legal advice); 409 when the project already has terms. */
export const createDefaultWarranty = (projectId: string): Promise<BuildWarrantyTermDto[]> =>
  apiClient.post(`/v1/build/projects/${projectId}/warranty/defaults`);

export const updateWarranty = (id: string, req: SaveBuildWarrantyTermRequest): Promise<BuildWarrantyTermDto> =>
  apiClient.put(`/v1/build/warranty-terms/${id}`, req);

export const deleteWarranty = (id: string): Promise<void> =>
  apiClient.delete(`/v1/build/warranty-terms/${id}`);

// ── Assistance ───────────────────────────────────────────────────────────────

export type BuildAssistanceStatus = "Open" | "Triaged" | "Scheduled" | "InProgress" | "Resolved" | "Closed" | "Rejected";

export interface BuildAssistanceDto {
  id:              string;
  publicId:        string;
  projectId:       string;
  projectName:     string;
  number:          number;
  category:        string;
  title:           string;
  description:     string;
  location:        string | null;
  openedVia:       BuildChannel;
  openedByName:    string;
  contactPhone:    string | null;
  openedOn:        string;
  status:          BuildAssistanceStatus;
  /** Internal — never shown to the client. */
  triageNotes:     string | null;
  warrantyCovered: boolean | null;
  warrantyTermId:  string | null;
  warrantySystem:  string | null;
  triagedAt:       string | null;
  triagedByName:   string | null;
  scheduledFor:    string | null;
  startedAt:       string | null;
  resolvedAt:      string | null;
  resolutionNotes: string | null;
  closedAt:        string | null;
  rejectedAt:      string | null;
  rejectionReason: string | null;
  clientRating:    number | null;
  clientFeedback:  string | null;
  photoKeys:       string[];
  photoUrls:       string[];
  createdAt:       string;
  updatedAt:       string;
}

export interface OpenBuildAssistanceRequest {
  category:      string;
  title:         string;
  description:   string;
  location?:     string | null;
  openedByName?: string | null;
  contactPhone?: string | null;
}

export interface AssistanceFilters {
  status?:  BuildAssistanceStatus;
  covered?: boolean;
}

export const fetchAssistance = (projectId: string, f: AssistanceFilters = {}): Promise<BuildAssistanceDto[]> =>
  apiClient.get(`/v1/build/projects/${projectId}/assistance${query({ status: f.status, covered: f.covered })}`);

export const openAssistance = (projectId: string, req: OpenBuildAssistanceRequest): Promise<BuildAssistanceDto> =>
  apiClient.post(`/v1/build/projects/${projectId}/assistance`, req);

export const fetchAssistanceRequest = (id: string): Promise<BuildAssistanceDto> =>
  apiClient.get(`/v1/build/assistance/${id}`);

/** Without warrantyCovered the server evaluates the coverage from the warranty of the category. */
export const triageAssistance = (
  id: string, req: { warrantyCovered?: boolean | null; notes?: string | null },
): Promise<BuildAssistanceDto> =>
  apiClient.post(`/v1/build/assistance/${id}/triage`, req);

export const scheduleAssistance = (id: string, scheduledFor: string): Promise<BuildAssistanceDto> =>
  apiClient.post(`/v1/build/assistance/${id}/schedule`, { scheduledFor });

export const startAssistance = (id: string): Promise<BuildAssistanceDto> =>
  apiClient.post(`/v1/build/assistance/${id}/start`);

export const resolveAssistance = (id: string, resolutionNotes: string): Promise<BuildAssistanceDto> =>
  apiClient.post(`/v1/build/assistance/${id}/resolve`, { resolutionNotes });

export const closeAssistance = (id: string): Promise<BuildAssistanceDto> =>
  apiClient.post(`/v1/build/assistance/${id}/close`);

export const rejectAssistance = (id: string, reason: string): Promise<BuildAssistanceDto> =>
  apiClient.post(`/v1/build/assistance/${id}/reject`, { reason });

export const addAssistancePhoto = (id: string, key: string): Promise<BuildAssistanceDto> =>
  apiClient.post(`/v1/build/assistance/${id}/photos`, { key });
