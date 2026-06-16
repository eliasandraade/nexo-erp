import { apiClient } from "@/services/api-client";

// ── Enums ─────────────────────────────────────────────────────────────────────

export type MovementDirection = "In" | "Out" | "Internal";
export type MovementNature    = "Expense" | "Transfer" | "Reimbursement" | "Advance";
export type MovementStatus    = "Draft" | "Confirmed" | "Voided";
export type FinancialContextType = "Obra" | "Loja" | "Servico" | "Departamento";

// ── Extraction + Suggestion DTOs ──────────────────────────────────────────────

export interface FieldDto {
  value:      string | null;
  confidence: number;
  status:     string; // "AutoFilled" | "NeedsAttention" | "RequiresInput"
  provider:   string;
}

export interface ExtractionSummaryDto {
  amount:      { value: number | null; confidence: number; status: string; provider: string };
  date:        FieldDto;
  payee:       FieldDto;
  account:     FieldDto;
  analyzerUsed: string;
}

export interface ContextSuggestionDto {
  type:        string | null;
  id:          string | null;
  displayName: string | null;
  source:      string;
}

export interface MovementSuggestionDto {
  direction: { value: string | null; displayValue: string | null; source: string };
  nature:    { value: string | null; displayValue: string | null; source: string };
  category:  { id: string | null; name: string | null; source: string };
  context:   ContextSuggestionDto;
  account:   { id: string | null; name: string | null; source: string };
}

// ── Analyze ───────────────────────────────────────────────────────────────────

export interface AnalyzeMovementRequest {
  text:        string;
  inputSource?: string; // "Text" (default)
}

export interface AnalyzeMovementResponse {
  draftId:      string;
  suggestionId: string;     // needed for OriginalSuggestionId in confirm
  extraction:   ExtractionSummaryDto;
  suggestion:   MovementSuggestionDto;
}

export const analyzeMovement = (req: AnalyzeMovementRequest): Promise<AnalyzeMovementResponse> =>
  apiClient.post("/v1/interpreter/analyze", { ...req, inputSource: req.inputSource ?? "Text" });

// ── Confirm ───────────────────────────────────────────────────────────────────

export interface ConfirmMovementRequest {
  amount:               number;
  date:                 string;           // "yyyy-MM-dd"
  description:          string;
  direction:            MovementDirection;
  nature:               MovementNature;
  categoryId?:          string | null;
  contextType:          FinancialContextType;
  contextId?:           string | null;
  accountId?:           string | null;
  originalSuggestionId: string;
  supplierId?:          string | null;
}

export interface ConfirmMovementResponse {
  id:           string;
  status:       string;
  confirmedAt:  string;
  corrections:  Array<{ field: string; original: string | null; corrected: string | null }>;
}

export const confirmMovement = (
  draftId: string,
  req: ConfirmMovementRequest,
): Promise<ConfirmMovementResponse> =>
  apiClient.post(`/v1/movements/${draftId}/confirm`, req);

// ── List movements by context ─────────────────────────────────────────────────

export interface MovementListItemDto {
  id:          string;
  direction:   MovementDirection;
  nature:      MovementNature;
  amount:      number;
  date:        string;       // "yyyy-MM-dd"
  description: string;
  contextType: string;
  contextId:   string | null;
  supplierId:  string | null;
  status:      MovementStatus;
  createdAt:   string;
}

export interface MovementListResponse {
  items:      MovementListItemDto[];
  totalCount: number;
  page:       number;
  pageSize:   number;
}

export const fetchProjectMovements = (
  projectId: string,
  params?: { status?: string; page?: number; pageSize?: number },
): Promise<MovementListResponse> => {
  const qs = new URLSearchParams({
    contextType: "Obra",
    contextId:   projectId,
    status:      params?.status ?? "Confirmed",
    page:        String(params?.page ?? 1),
    pageSize:    String(params?.pageSize ?? 30),
  });
  return apiClient.get(`/v1/movements?${qs.toString()}`);
};
