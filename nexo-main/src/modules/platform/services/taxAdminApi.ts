import { apiClient } from "@/services/api-client";
import {
  qs, type ClassificationKind, type ClassificationStatus, type EvidenceType, type Page, type ProfessionalReviewStatus, type RetentionStart,
  type TaxApprovalMode, type TaxClassificationCodeDto, type TaxLegalSourceDto, type TaxModuleStatus, type TaxObligationDefinitionDto,
  type TaxRegime, type TaxRetentionPolicyDto, type TaxRuleDto, type TaxRuleVersionDto, type TaxStoredFileDto,
} from "@/modules/tax/api/tax.api";

/**
 * Platform administration of Tax Intelligence: module state and activation, readiness, the pilot scope, the rule catalogue
 * (internal pilot approval and professional reviews as recorded), legal sources, classification and obligation catalogues,
 * retention defaults, documents, refusals and audit. Reading needs a platform session; changing needs a platform super admin.
 */

// ── Module state, activation and readiness ──────────────────────────────────

export interface TaxModuleActivationDto {
  id: string; status: TaxModuleStatus; mode: TaxApprovalMode; decisionReference: string; reason: string; actorName: string; actorRole: string; at: string;
}
export interface TaxModuleStateDto {
  configEnabled: boolean;
  configMode: TaxApprovalMode;
  configurationProblems: string[];
  status: TaxModuleStatus;
  activatedMode: TaxApprovalMode | null;
  latest: TaxModuleActivationDto | null;
  newWorkRuns: boolean;
  summary: string;
  history: TaxModuleActivationDto[];
}
export type TaxReadinessLevel = "Ok" | "Info" | "Warning" | "Blocking";
export interface TaxReadinessItemDto { key: string; area: string; label: string; level: TaxReadinessLevel; detail: string }
export interface TaxAuditEventDto {
  id: string; action: string; severity: string; entityType: string | null; entityId: string | null; description: string; actorName: string; at: string;
  tenantId: string | null;
}
export interface TaxReadinessDto {
  generatedAt: string; engineVersion: string; canonicalVersion: string; coveragePolicy: string; module: TaxModuleStateDto;
  items: TaxReadinessItemDto[]; rulesEligible: string[]; rulesBlocking: string[]; pendingRegulation: string[]; lastAuditEvent: TaxAuditEventDto | null;
}
export interface TaxActivationChecklistDto {
  mode: TaxApprovalMode; canActivate: boolean; confirmationPhrase: string; items: TaxReadinessItemDto[]; rulesMissingApproval: string[]; caveats: string[];
}
export interface TaxRefusalDto { action: string; code: string | null; description: string; actorName: string; at: string; tenantId: string | null }

export const PLATFORM_TAX_KEY = ["platform", "tax"] as const;
export const PLATFORM_TAX_KEYS = {
  module: [...PLATFORM_TAX_KEY, "module"] as const,
  checklist: (mode: TaxApprovalMode) => [...PLATFORM_TAX_KEY, "checklist", mode] as const,
  readiness: [...PLATFORM_TAX_KEY, "readiness"] as const,
  pilot: [...PLATFORM_TAX_KEY, "pilot"] as const,
  rules: [...PLATFORM_TAX_KEY, "rules"] as const,
  reviews: (versionId: string) => [...PLATFORM_TAX_KEY, "reviews", versionId] as const,
  sources: [...PLATFORM_TAX_KEY, "sources"] as const,
  classifications: (f: ClassificationFilter) => [...PLATFORM_TAX_KEY, "classifications", f] as const,
  obligations: [...PLATFORM_TAX_KEY, "obligations"] as const,
  retention: [...PLATFORM_TAX_KEY, "retention"] as const,
  documents: (purpose: string) => [...PLATFORM_TAX_KEY, "documents", purpose] as const,
  refusals: [...PLATFORM_TAX_KEY, "refusals"] as const,
  audit: (action: string, skip: number) => [...PLATFORM_TAX_KEY, "audit", action, skip] as const,
};

export const getModuleState = () => apiClient.get<TaxModuleStateDto>("/platform/tax/module");
export const getActivationChecklist = (mode: TaxApprovalMode) =>
  apiClient.get<TaxActivationChecklistDto>(`/platform/tax/module/checklist${qs({ mode })}`);
export const activateModule = (body: { mode: TaxApprovalMode; decisionReference: string; reason: string; confirmation: string; acknowledgeShadowMode: boolean }) =>
  apiClient.post<TaxModuleStateDto>("/platform/tax/module/activate", body);
export const suspendModule = (reason: string, confirmation: string) =>
  apiClient.post<TaxModuleStateDto>("/platform/tax/module/suspend", { reason, confirmation });
export const deactivateModule = (reason: string, confirmation: string) =>
  apiClient.post<TaxModuleStateDto>("/platform/tax/module/deactivate", { reason, confirmation });
export const getReadiness = () => apiClient.get<TaxReadinessDto>("/platform/tax/readiness");
export const getRefusals = (take = 50) => apiClient.get<TaxRefusalDto[]>(`/platform/tax/refusals${qs({ take })}`);
export const getTaxAudit = (action: string, skip = 0, take = 50) =>
  apiClient.get<Page<TaxAuditEventDto>>(`/platform/tax/audit${qs({ action, skip, take })}`);

/** The phrases the server requires — typed by the operator, never pre-filled. */
export const SUSPEND_PHRASE = "SUSPENDER";
export const DEACTIVATE_PHRASE = "DESATIVAR";

// ── Pilot scope ─────────────────────────────────────────────────────────────

export interface TaxPilotParticipantDto {
  id: string; tenantId: string; storeId: string | null; active: boolean; addedByName: string; addedAt: string;
  removedAt: string | null; removedByName: string | null; removalReason: string | null;
}
export interface TaxPilotScopeDto {
  id: string | null; enabled: boolean; validFrom: string | null; validTo: string | null; decisionReference: string | null;
  activatedByName: string | null; activatedAt: string | null; participants: TaxPilotParticipantDto[];
}
export const getPilotScope = () => apiClient.get<TaxPilotScopeDto>("/platform/tax/pilot");
export const configurePilotScope = (body: { enabled: boolean; validFrom: string; validTo: string | null; decisionReference: string }) =>
  apiClient.put<TaxPilotScopeDto>("/platform/tax/pilot", body);
export const addPilotParticipant = (tenantId: string, storeId: string | null) =>
  apiClient.post<TaxPilotScopeDto>("/platform/tax/pilot/participants", { tenantId, storeId });
export const removePilotParticipant = (id: string, reason: string) =>
  apiClient.post<TaxPilotScopeDto>(`/platform/tax/pilot/participants/${id}/remove`, { reason });

// ── Rules, internal approval and professional reviews ───────────────────────

export interface TaxProfessionalReviewRecordDto {
  id: string; ruleVersionId: string; status: ProfessionalReviewStatus; previousStatus: ProfessionalReviewStatus;
  /** The professional as informed by the operator — the platform never verifies or invents it. */
  reviewer: string;
  professionalReference: string | null; reviewedOn: string | null; scope: string | null; coveredRegimes: string[];
  validFrom: string | null; validTo: string | null; assumptions: string | null; caveats: string | null; notes: string | null;
  supportingDocument: TaxStoredFileDto | null;
  /** Who recorded it in the platform, and when (server time) — never the professional. */
  recordedByName: string; recordedByRole: string; recordedAt: string;
}
export interface RecordProfessionalReviewRequest {
  status: ProfessionalReviewStatus; reviewer: string; notes: string | null; professionalReference: string | null; scope: string | null;
  validFrom: string | null; validTo: string | null; assumptions: string | null; caveats: string | null; coveredRegimes: TaxRegime[];
  reviewedOn: string | null; supportingDocumentId: string | null;
}
export const listRules = () => apiClient.get<TaxRuleDto[]>("/platform/tax/rules");
export const changeRuleVersionStatus = (id: string, status: "Active" | "Revoked") =>
  apiClient.post<TaxRuleVersionDto>(`/platform/tax/rule-versions/${id}/status`, { status });
export const recordProfessionalReview = (id: string, body: RecordProfessionalReviewRequest) =>
  apiClient.post<TaxRuleVersionDto>(`/platform/tax/rule-versions/${id}/professional-review`, body);
export const listProfessionalReviews = (id: string) =>
  apiClient.get<TaxProfessionalReviewRecordDto[]>(`/platform/tax/rule-versions/${id}/professional-reviews`);
export const approveForPilot = (id: string, body: { scope: string; validFrom: string; validTo: string | null; caveats: string; decisionReference: string }) =>
  apiClient.post<TaxRuleVersionDto>(`/platform/tax/rule-versions/${id}/pilot-approval`, body);
export const withdrawPilotApproval = (id: string, reason: string) =>
  apiClient.post<TaxRuleVersionDto>(`/platform/tax/rule-versions/${id}/pilot-approval/withdraw`, { reason });

// ── Legal sources ───────────────────────────────────────────────────────────

export interface CreateLegalSourceRequest {
  code: string; kind: string; title: string; reference: string; url: string; excerpt: string; publishedOn: string | null; notes: string | null;
}
export const listLegalSources = () => apiClient.get<TaxLegalSourceDto[]>("/platform/tax/legal-sources");
export const createLegalSource = (body: CreateLegalSourceRequest) => apiClient.post<TaxLegalSourceDto>("/platform/tax/legal-sources", body);
/** Records that a person compared the excerpt with the official text; who did it is informed, who recorded it is the session. */
export const verifyLegalSource = (code: string, verifiedBy: string) =>
  apiClient.post<TaxLegalSourceDto>(`/platform/tax/legal-sources/${encodeURIComponent(code)}/verify`, { verifiedBy });

// ── Classification and obligation catalogues ─────────────────────────────────

export interface ClassificationFilter { kind?: ClassificationKind | ""; status?: ClassificationStatus | ""; search?: string; skip?: number; take?: number }
export interface ImportClassificationItem { code: string; description: string; validFrom: string; validTo: string | null; notes: string | null }
/** Parses "código;descrição;início;fim(opcional)" lines. Lines that do not parse are reported, never guessed. */
export function parseClassificationLines(raw: string): { items: ImportClassificationItem[]; errors: string[] } {
  const items: ImportClassificationItem[] = [];
  const errors: string[] = [];
  const iso = /^\d{4}-\d{2}-\d{2}$/;
  raw.split(/\r?\n/).forEach((text, i) => {
    const line = text.trim();
    if (!line) return;
    // "Linha N" counts every line as pasted, blank ones included.
    const [code = "", description = "", validFrom = "", validTo = ""] = line.split(";").map((x) => x.trim());
    if (!code || !description || !iso.test(validFrom) || (validTo && !iso.test(validTo))) errors.push(`Linha ${i + 1}: use código;descrição;AAAA-MM-DD;AAAA-MM-DD(opcional).`);
    else items.push({ code, description, validFrom, validTo: validTo || null, notes: null });
  });
  return { items, errors };
}

export const listPlatformClassifications = (f: ClassificationFilter) =>
  apiClient.get<Page<TaxClassificationCodeDto>>(`/platform/tax/classifications${qs(f)}`);
export const importClassifications = (body: {
  kind: ClassificationKind; source: string; sourceVersion: string | null; sourceDocumentId: string | null; items: ImportClassificationItem[];
}) => apiClient.post<{ batchId: string; imported: number; skipped: string[] }>("/platform/tax/classifications/import", body);
export const changeClassificationStatus = (id: string, status: ClassificationStatus, reason: string) =>
  apiClient.post<TaxClassificationCodeDto>(`/platform/tax/classifications/${id}/status`, { status, reason });
export const recordClassificationReview = (id: string, status: ProfessionalReviewStatus, notes: string) =>
  apiClient.post<TaxClassificationCodeDto>(`/platform/tax/classifications/${id}/professional-review`, { status, notes });

export const listPlatformObligations = () => apiClient.get<TaxObligationDefinitionDto[]>("/platform/tax/obligations");
export const defineObligation = (body: { code: string; name: string; description: string | null; reference: string }) =>
  apiClient.post<TaxObligationDefinitionDto>("/platform/tax/obligations", body);
export const setObligationActive = (id: string, active: boolean) =>
  apiClient.post<TaxObligationDefinitionDto>(`/platform/tax/obligations/${id}/active`, { active });

// ── Retention defaults and documents ────────────────────────────────────────

export const listPlatformRetention = () => apiClient.get<TaxRetentionPolicyDto[]>("/platform/tax/retention-policies");
export const configurePlatformRetention = (body: {
  evidenceType: EvidenceType | null; retentionMonths: number; startsAt: RetentionStart; enabled: boolean; allowTenantOverride: boolean;
  rationale: string; reference: string;
}) => apiClient.put<TaxRetentionPolicyDto>("/platform/tax/retention-policies", body);

export type DocumentPurpose = "professional-review" | "classification-source";
export const listDocuments = (purpose: DocumentPurpose | "" = "") =>
  apiClient.get<TaxStoredFileDto[]>(`/platform/tax/documents${qs({ purpose })}`);
export function uploadDocument(purpose: DocumentPurpose, file: File, description: string | null) {
  const form = new FormData();
  form.append("file", file);
  form.append("purpose", purpose);
  if (description) form.append("description", description);
  return apiClient.postForm<TaxStoredFileDto>("/platform/tax/documents", form);
}
export const documentUrl = (id: string) => `/platform/tax/documents/${id}/content`;
export const revokeDocument = (id: string, reason: string) =>
  apiClient.post<TaxStoredFileDto>(`/platform/tax/documents/${id}/revoke`, { reason });
