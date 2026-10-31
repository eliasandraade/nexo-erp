import { apiClient, type UploadProgressHandler } from "@/services/api-client";

// Wave 5 — IA do Build (docs/build/internal-api.md "# Wave 5", docs/build/ai.md).
// The AI only suggests: every capability stores a Pending suggestion; nothing changes on the project until a person accepts it.

// ── Enums ─────────────────────────────────────────────────────────────────────

export type BuildAiCapability =
  | "DailyLogCopilot" | "ProgressInterpreter" | "ModelMapper" | "ClientUpdate"
  | "DelayRisk" | "CostRisk" | "DocumentIntelligence" | "WeeklyDigest";
export type BuildAiSuggestionStatus = "Pending" | "Accepted" | "PartiallyAccepted" | "Rejected" | "Expired" | "Failed";
export type BuildAiSource = "Llm" | "Rules";
export type BuildRiskKind = "delay" | "cost";
export type BuildRiskLevel = "none" | "low" | "medium" | "high";
export type BuildDocumentCategory =
  | "Contrato" | "Projeto" | "ArtRrt" | "Alvara" | "NotaFiscal" | "Laudo" | "Manual" | "Garantia" | "Foto" | "Outro";

// ── Suggestion ────────────────────────────────────────────────────────────────

export interface BuildAiSuggestionDto {
  id:              string;
  projectId:       string;
  capability:      BuildAiCapability | string;
  status:          BuildAiSuggestionStatus | string;
  source:          BuildAiSource | string;
  subjectId:       string | null;
  confidence:      number;
  /** Validated output — shape per capability (see the *Output types below). */
  output:          unknown;
  /** Ids of what was read (diary entries, photos, stages, zones…). */
  inputRefs:       unknown;
  /** What the acceptance created or changed. */
  applied:         unknown;
  provider:        string | null;
  model:           string | null;
  promptType:      string | null;
  promptVersion:   string | null;
  promptVersionId: string | null;
  promptHash:      string | null;
  inputTokens:     number;
  outputTokens:    number;
  /** Millionths of a US dollar. */
  costMicros:      number;
  latencyMs:       number;
  createdByName:   string;
  createdAt:       string;
  decidedByName:   string | null;
  decidedAt:       string | null;
  decisionNote:    string | null;
  expiresAt:       string | null;
}

// ── Outputs per capability (BuildAiContracts.cs, camelCase) ───────────────────

export interface CopilotActivity { stageId: string | null; stageName: string | null; description: string; progressHint: number | null; confidence: number }
export interface CopilotWorkforce { role: string; count: number }
export interface DailyLogDraftOutput {
  date: string; activities: CopilotActivity[]; workforce: CopilotWorkforce[]; equipment: string[]; issues: string[];
  weatherNote: string | null; clientSummary: string | null; confidence: number; discardedRefs: number;
}

export interface ProgressSuggestionItem {
  id: string; stageId: string; stageName: string; currentPercent: number; suggestedPercent: number;
  rationale: string; evidenceRefs: string[]; confidence: number;
}
export interface ProgressSuggestionOutput { items: ProgressSuggestionItem[]; discardedRefs: number }

export interface MapperBinding {
  id: string; stageId: string; stageName: string; scope: "Category" | "Zone" | string; category: string | null;
  zoneId: string | null; zoneName: string | null; confidence: number; rationale: string;
}
export interface MapperSuggestionOutput { modelVersionId: string; bindings: MapperBinding[]; discardedRefs: number }

export interface ClientUpdateDraftOutput { kind: string; from: string; to: string; title: string; body: string; highlights: string[] }

export interface RiskExplanationOutput {
  kind: BuildRiskKind; summary: string; items: { key: string; explanation: string }[];
  recommendedActions: { text: string; itemKeys: string[] }[]; analysis?: BuildRiskAnalysisDto; discardedRefs: number;
}

export interface DocumentExtractionOutput {
  documentType: string | null; issuer: string | null; issuerCnpj: string | null; number: string | null;
  issueDate: string | null; dueDate: string | null; validUntil: string | null; totalAmount: number | null;
  items: { description: string; quantity: number | null; unitPrice: number | null; total: number | null }[];
  summary: string | null; confidence: number;
}

// ── Risk (deterministic) ──────────────────────────────────────────────────────

export interface BuildRiskItemDto {
  key:      string;
  kind:     string;
  severity: BuildRiskLevel | string;
  title:    string;
  evidence: string;
  metrics:  Record<string, number | null>;
  refs:     string[];
}

export interface BuildRiskAnalysisDto {
  projectId:   string;
  kind:        BuildRiskKind;
  asOf:        string;
  level:       BuildRiskLevel | string;
  metrics:     Record<string, number | null>;
  items:       BuildRiskItemDto[];
  methodology: string;
}

// ── Documents ─────────────────────────────────────────────────────────────────

export interface BuildDocumentDto {
  id:             string;
  projectId:      string;
  category:       BuildDocumentCategory | string;
  title:          string;
  fileName:       string;
  contentType:    string;
  sizeBytes:      number;
  sha256:         string;
  clientVisible:  boolean;
  validUntil:     string | null;
  /** What an accepted AI analysis confirmed — internal only. */
  extracted:      unknown;
  suggestionId:   string | null;
  uploadedByName: string;
  url:            string | null;
  urlExpiresAt:   string | null;
  createdAt:      string;
}

export interface UploadBuildDocumentInput {
  file:           File;
  category?:      BuildDocumentCategory;
  title?:         string;
  validUntil?:    string;
  clientVisible?: boolean;
}

export interface UpdateBuildDocumentRequest {
  category?:   BuildDocumentCategory;
  title?:      string;
  validUntil?: string | null;
}

// ── Requests ──────────────────────────────────────────────────────────────────

export interface DailyLogCopilotRequest { date?: string; text: string; photoIds?: string[] }
export interface ProgressInterpreterRequest { lookbackDays?: number; photoIds?: string[] }
export interface ModelMapperRequest { modelVersionId?: string }
export interface ClientUpdateAiRequest { from?: string; to?: string }
export interface AcceptSuggestionRequest { itemIds?: string[]; note?: string }

// ── Calls ─────────────────────────────────────────────────────────────────────

const B = "/v1/build";

export const generateDailyLogCopilot = (projectId: string, req: DailyLogCopilotRequest) =>
  apiClient.post<BuildAiSuggestionDto>(`${B}/projects/${projectId}/ai/daily-log-copilot`, req);
export const generateProgressInterpretation = (projectId: string, req: ProgressInterpreterRequest) =>
  apiClient.post<BuildAiSuggestionDto>(`${B}/projects/${projectId}/ai/progress-interpreter`, req);
export const generateModelMapping = (projectId: string, req: ModelMapperRequest) =>
  apiClient.post<BuildAiSuggestionDto>(`${B}/projects/${projectId}/ai/model-mapper`, req);
export const generateClientUpdate = (projectId: string, req: ClientUpdateAiRequest) =>
  apiClient.post<BuildAiSuggestionDto>(`${B}/projects/${projectId}/ai/client-update`, req);
export const generateWeeklyDigest = (projectId: string) =>
  apiClient.post<BuildAiSuggestionDto>(`${B}/projects/${projectId}/ai/weekly-digest`, {});
export const explainRisk = (projectId: string, kind: BuildRiskKind) =>
  apiClient.post<BuildAiSuggestionDto>(`${B}/projects/${projectId}/risk/explain`, { kind });
export const analyzeDocument = (documentId: string) =>
  apiClient.post<BuildAiSuggestionDto>(`${B}/documents/${documentId}/analyze`, {});

export function fetchSuggestions(projectId: string, filters: { capability?: string; status?: string } = {}) {
  const q = new URLSearchParams();
  if (filters.capability) q.set("capability", filters.capability);
  if (filters.status) q.set("status", filters.status);
  const s = q.toString();
  return apiClient.get<BuildAiSuggestionDto[]>(`${B}/projects/${projectId}/ai/suggestions${s ? `?${s}` : ""}`);
}
export const fetchSuggestion = (id: string) => apiClient.get<BuildAiSuggestionDto>(`${B}/ai/suggestions/${id}`);
export const acceptSuggestion = (id: string, req: AcceptSuggestionRequest) =>
  apiClient.post<BuildAiSuggestionDto>(`${B}/ai/suggestions/${id}/accept`, req);
export const rejectSuggestion = (id: string, note?: string) =>
  apiClient.post<BuildAiSuggestionDto>(`${B}/ai/suggestions/${id}/reject`, { note });

export const fetchDelayRisk = (projectId: string) => apiClient.get<BuildRiskAnalysisDto>(`${B}/projects/${projectId}/risk/delay`);
export const fetchCostRisk = (projectId: string) => apiClient.get<BuildRiskAnalysisDto>(`${B}/projects/${projectId}/risk/cost`);

export function fetchDocuments(projectId: string, category?: string) {
  const q = category ? `?category=${encodeURIComponent(category)}` : "";
  return apiClient.get<BuildDocumentDto[]>(`${B}/projects/${projectId}/documents${q}`);
}
export function uploadDocument(projectId: string, input: UploadBuildDocumentInput, onProgress?: UploadProgressHandler) {
  const form = new FormData();
  form.append("file", input.file);
  if (input.category) form.append("category", input.category);
  if (input.title) form.append("title", input.title);
  if (input.validUntil) form.append("validUntil", input.validUntil);
  if (input.clientVisible !== undefined) form.append("clientVisible", String(input.clientVisible));
  return apiClient.upload<BuildDocumentDto>("POST", `${B}/projects/${projectId}/documents`, form, onProgress);
}
export const updateDocument = (id: string, req: UpdateBuildDocumentRequest) =>
  apiClient.put<BuildDocumentDto>(`${B}/documents/${id}`, req);
export const setDocumentVisibility = (id: string, clientVisible: boolean) =>
  apiClient.put<BuildDocumentDto>(`${B}/documents/${id}/client-visibility`, { clientVisible });
export const deleteDocument = (id: string) => apiClient.delete<void>(`${B}/documents/${id}`);
