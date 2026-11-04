import { apiClient } from "@/services/api-client";
import { downloadPdf } from "@/services/pdf.api";
import type { BuildBudgetDto, BuildBudgetStatus, BuildCostKind } from "./build.api";

// Wave 2 — Comercial (internal API). Money in BRL with 2 decimals; enums travel as their string names.

// ── Budgets: terms, duplicate, versions, PDF, templates ──────────────────────

export interface SetBuildBudgetTermsRequest {
  validUntil:    string | null;
  notes:         string | null;
  internalNotes: string | null;
  areaM2:        number | null;
}

export interface BuildBudgetVersionDto {
  id:             string;
  version:        number;
  name:           string;
  status:         BuildBudgetStatus;
  finalPrice:     number;
  parentBudgetId: string | null;
  validUntil:     string | null;
  isExpired:      boolean;
  createdAt:      string;
}

export interface BuildBudgetTemplateItemDto {
  id:            string;
  order:         number;
  name:          string;
  kind:          BuildCostKind;
  category:      string;
  unit:          string;
  quantityPerM2: number | null;
  fixedQuantity: number | null;
  unitCost:      number;
  stageName:     string | null;
}

export interface BuildBudgetTemplateDto {
  id:                   string;
  name:                 string;
  description:          string | null;
  defaultMarginPercent: number;
  items:                BuildBudgetTemplateItemDto[];
  createdAt:            string;
  updatedAt:            string;
}

/** Exactly one of quantityPerM2 / fixedQuantity. */
export interface BuildBudgetTemplateItemRequest {
  name:           string;
  kind?:          BuildCostKind;
  category?:      string;
  unit:           string;
  quantityPerM2?: number | null;
  fixedQuantity?: number | null;
  unitCost:       number;
  stageName?:     string | null;
}

/** PUT replaces everything, items included. */
export interface SaveBuildBudgetTemplateRequest {
  name:                 string;
  description?:         string | null;
  defaultMarginPercent: number;
  items:                BuildBudgetTemplateItemRequest[];
}

export interface SaveBudgetAsTemplateRequest {
  name:            string;
  description?:    string;
  /** Quantities become "per m²" (needs the budget area). */
  perSquareMeter?: boolean;
}

export interface CreateBudgetFromTemplateRequest {
  name:           string;
  projectId?:     string;
  areaM2?:        number;
  marginPercent?: number;
  validUntil?:    string;
  notes?:         string;
}

export const setBudgetTerms = (id: string, req: SetBuildBudgetTermsRequest): Promise<BuildBudgetDto> =>
  apiClient.put(`/v1/build/budgets/${id}/terms`, req);

export const duplicateBudget = (id: string, name?: string): Promise<BuildBudgetDto> =>
  apiClient.post(`/v1/build/budgets/${id}/duplicate`, name ? { name } : undefined);

export const createBudgetVersion = (id: string): Promise<BuildBudgetDto> =>
  apiClient.post(`/v1/build/budgets/${id}/versions`);

export const fetchBudgetVersions = (id: string): Promise<BuildBudgetVersionDto[]> =>
  apiClient.get(`/v1/build/budgets/${id}/versions`);

/** "orcamento-<name>-v<N>.pdf" with the name reduced to safe characters. */
export function budgetPdfFileName(name: string, version: number | undefined): string {
  const safe = name
    .normalize("NFD")
    .toLowerCase()
    .split("")
    .filter((c) => (c >= "a" && c <= "z") || (c >= "0" && c <= "9") || c === " " || c === "-")
    .join("")
    .trim()
    .split(" ")
    .filter(Boolean)
    .join("-");
  return `orcamento-${safe || "orcamento"}-v${version ?? 1}.pdf`;
}

export const downloadBudgetPdf = (budget: Pick<BuildBudgetDto, "id" | "name" | "version">): Promise<void> =>
  downloadPdf(`/v1/build/budgets/${budget.id}/pdf`, budgetPdfFileName(budget.name, budget.version));

export const saveBudgetAsTemplate = (id: string, req: SaveBudgetAsTemplateRequest): Promise<BuildBudgetTemplateDto> =>
  apiClient.post(`/v1/build/budgets/${id}/save-as-template`, req);

export const fetchBudgetTemplates = (): Promise<BuildBudgetTemplateDto[]> =>
  apiClient.get(`/v1/build/budget-templates`);

export const createBudgetTemplate = (req: SaveBuildBudgetTemplateRequest): Promise<BuildBudgetTemplateDto> =>
  apiClient.post(`/v1/build/budget-templates`, req);

export const updateBudgetTemplate = (id: string, req: SaveBuildBudgetTemplateRequest): Promise<BuildBudgetTemplateDto> =>
  apiClient.put(`/v1/build/budget-templates/${id}`, req);

export const deleteBudgetTemplate = (id: string): Promise<void> =>
  apiClient.delete(`/v1/build/budget-templates/${id}`);

export const createBudgetFromTemplate = (templateId: string, req: CreateBudgetFromTemplateRequest): Promise<BuildBudgetDto> =>
  apiClient.post(`/v1/build/budget-templates/${templateId}/budgets`, req);

// ── Change orders (aditivos) ─────────────────────────────────────────────────

export type BuildChangeOrderStatus = "Draft" | "SentToClient" | "Approved" | "Rejected" | "Cancelled";
export type BuildChangeOrderReason = "ClientRequest" | "DesignChange" | "SiteCondition" | "Error" | "Other";
export type BuildDecisionChannel = "Internal" | "Portal";

export interface BuildChangeOrderItemDto {
  id:        string;
  order:     number;
  name:      string;
  kind:      BuildCostKind;
  quantity:  number;
  unit:      string;
  unitCost:  number;
  totalCost: number;
}

export interface BuildChangeOrderDto {
  id:                 string;
  publicId:           string;
  projectId:          string;
  number:             number;
  title:              string;
  description:        string | null;
  reason:             BuildChangeOrderReason;
  /** Signed: negative lowers the contract. */
  costImpact:         number;
  scheduleImpactDays: number;
  stageId:            string | null;
  stageName:          string | null;
  status:             BuildChangeOrderStatus;
  sentAt:             string | null;
  decidedAt:          string | null;
  /** User name, or "Cliente (portal)". */
  decidedBy:          string | null;
  decisionChannel:    BuildDecisionChannel | null;
  clientComment:      string | null;
  signerName:         string | null;
  sourceSelectionId:  string | null;
  appliedToBudget:    boolean;
  createdByName:      string;
  items:              BuildChangeOrderItemDto[];
  createdAt:          string;
  updatedAt:          string;
}

export interface BuildChangeOrderItemRequest {
  name:     string;
  kind?:    BuildCostKind;
  quantity: number;
  unit:     string;
  unitCost: number;
}

/** costImpact null = Σ items (0 without items). Items replace the previous ones. */
export interface SaveBuildChangeOrderRequest {
  title:               string;
  description?:        string | null;
  reason?:             BuildChangeOrderReason;
  costImpact?:         number | null;
  scheduleImpactDays?: number;
  stageId?:            string | null;
  items?:              BuildChangeOrderItemRequest[];
}

export const fetchChangeOrders = (projectId: string, status?: BuildChangeOrderStatus): Promise<BuildChangeOrderDto[]> =>
  apiClient.get(`/v1/build/projects/${projectId}/change-orders${status ? `?status=${status}` : ""}`);

export const createChangeOrder = (projectId: string, req: SaveBuildChangeOrderRequest): Promise<BuildChangeOrderDto> =>
  apiClient.post(`/v1/build/projects/${projectId}/change-orders`, req);

export const updateChangeOrder = (id: string, req: SaveBuildChangeOrderRequest): Promise<BuildChangeOrderDto> =>
  apiClient.put(`/v1/build/change-orders/${id}`, req);

export const sendChangeOrder = (id: string): Promise<BuildChangeOrderDto> =>
  apiClient.post(`/v1/build/change-orders/${id}/send`);

export const approveChangeOrder = (id: string, comment?: string): Promise<BuildChangeOrderDto> =>
  apiClient.post(`/v1/build/change-orders/${id}/approve`, comment ? { comment } : undefined);

export const rejectChangeOrder = (id: string, comment?: string): Promise<BuildChangeOrderDto> =>
  apiClient.post(`/v1/build/change-orders/${id}/reject`, comment ? { comment } : undefined);

export const cancelChangeOrder = (id: string): Promise<BuildChangeOrderDto> =>
  apiClient.post(`/v1/build/change-orders/${id}/cancel`);

// ── Selections (escolhas do cliente) ─────────────────────────────────────────

export type BuildSelectionStatus = "Open" | "Submitted" | "Confirmed" | "Cancelled";

export interface BuildSelectionOptionDto {
  id:           string;
  order:        number;
  name:         string;
  description:  string | null;
  /** Relative to the standard option. */
  priceDelta:   number;
  isStandard:   boolean;
  imageKey:     string | null;
  /** Short-lived signed URL. */
  imageUrl:     string | null;
  leadTimeDays: number | null;
}

export interface BuildSelectionDto {
  id:              string;
  publicId:        string;
  projectId:       string;
  title:           string;
  description:     string | null;
  stageId:         string | null;
  stageName:       string | null;
  deadline:        string | null;
  deadlinePassed:  boolean;
  status:          BuildSelectionStatus;
  clientVisible:   boolean;
  chosenOptionId:  string | null;
  clientComment:   string | null;
  decidedAt:       string | null;
  chosenVia:       string | null;
  confirmedAt:     string | null;
  confirmedByName: string | null;
  /** Draft change order created on confirm (price / lead time changed). */
  changeOrderId:   string | null;
  options:         BuildSelectionOptionDto[];
  createdAt:       string;
  updatedAt:       string;
}

export interface SaveBuildSelectionRequest {
  title:          string;
  description?:   string | null;
  stageId?:       string | null;
  deadline?:      string | null;
  clientVisible?: boolean;
}

export interface SaveBuildSelectionOptionRequest {
  name:          string;
  description?:  string | null;
  priceDelta?:   number;
  isStandard?:   boolean;
  imageKey?:     string | null;
  leadTimeDays?: number | null;
  order?:        number | null;
}

export const fetchSelections = (projectId: string): Promise<BuildSelectionDto[]> =>
  apiClient.get(`/v1/build/projects/${projectId}/selections`);

export const createSelection = (projectId: string, req: SaveBuildSelectionRequest): Promise<BuildSelectionDto> =>
  apiClient.post(`/v1/build/projects/${projectId}/selections`, req);

export const updateSelection = (id: string, req: SaveBuildSelectionRequest): Promise<BuildSelectionDto> =>
  apiClient.put(`/v1/build/selections/${id}`, req);

export const addSelectionOption = (id: string, req: SaveBuildSelectionOptionRequest): Promise<BuildSelectionDto> =>
  apiClient.post(`/v1/build/selections/${id}/options`, req);

export const updateSelectionOption = (optionId: string, req: SaveBuildSelectionOptionRequest): Promise<BuildSelectionDto> =>
  apiClient.put(`/v1/build/selection-options/${optionId}`, req);

export const removeSelectionOption = (optionId: string): Promise<BuildSelectionDto> =>
  apiClient.delete(`/v1/build/selection-options/${optionId}`);

export const confirmSelection = (id: string, optionId?: string): Promise<BuildSelectionDto> =>
  apiClient.post(`/v1/build/selections/${id}/confirm`, optionId ? { optionId } : undefined);

export const cancelSelection = (id: string): Promise<BuildSelectionDto> =>
  apiClient.post(`/v1/build/selections/${id}/cancel`);

// ── Materials ────────────────────────────────────────────────────────────────

export type BuildMaterialStatus =
  | "Requested" | "Quoted" | "Ordered" | "PartiallyDelivered" | "Delivered" | "Cancelled";

export interface BuildMaterialDeliveryDto {
  id:             string;
  quantity:       number;
  date:           string;
  note:           string | null;
  receivedBy:     string | null;
  recordedByName: string;
  createdAt:      string;
}

export interface BuildMaterialRequestDto {
  id:                string;
  projectId:         string;
  stageId:           string | null;
  stageName:         string | null;
  materialName:      string;
  unit:              string;
  quantity:          number;
  neededBy:          string | null;
  status:            BuildMaterialStatus;
  isLate:            boolean;
  supplierId:        string | null;
  supplierName:      string | null;
  estimatedUnitCost: number | null;
  estimatedTotal:    number | null;
  quotedAt:          string | null;
  orderedAt:         string | null;
  deliveredQuantity: number;
  remainingQuantity: number;
  deliveredAt:       string | null;
  movementId:        string | null;
  notes:             string | null;
  createdByName:     string;
  deliveries:        BuildMaterialDeliveryDto[];
  createdAt:         string;
  updatedAt:         string;
}

export interface SaveBuildMaterialRequest {
  materialName: string;
  unit:         string;
  quantity:     number;
  stageId?:     string | null;
  neededBy?:    string | null;
  notes?:       string | null;
}

export interface MaterialFilters {
  status?:  BuildMaterialStatus;
  stageId?: string;
  late?:    boolean;
}

export const fetchMaterials = (projectId: string, f: MaterialFilters = {}): Promise<BuildMaterialRequestDto[]> => {
  const p = new URLSearchParams();
  if (f.status)  p.set("status", f.status);
  if (f.stageId) p.set("stageId", f.stageId);
  if (f.late)    p.set("late", "true");
  const qs = p.toString();
  return apiClient.get(`/v1/build/projects/${projectId}/materials${qs ? `?${qs}` : ""}`);
};

export const createMaterial = (projectId: string, req: SaveBuildMaterialRequest): Promise<BuildMaterialRequestDto> =>
  apiClient.post(`/v1/build/projects/${projectId}/materials`, req);

export const updateMaterial = (id: string, req: SaveBuildMaterialRequest): Promise<BuildMaterialRequestDto> =>
  apiClient.put(`/v1/build/materials/${id}`, req);

export const quoteMaterial = (id: string, req: { supplierId?: string | null; estimatedUnitCost?: number | null }): Promise<BuildMaterialRequestDto> =>
  apiClient.post(`/v1/build/materials/${id}/quote`, req);

export const orderMaterial = (id: string, req: { supplierId?: string | null; unitCost?: number | null }): Promise<BuildMaterialRequestDto> =>
  apiClient.post(`/v1/build/materials/${id}/order`, req);

export const deliverMaterial = (
  id: string,
  req: { quantity: number; date?: string; note?: string; receivedBy?: string },
): Promise<BuildMaterialRequestDto> =>
  apiClient.post(`/v1/build/materials/${id}/deliveries`, req);

export const cancelMaterial = (id: string): Promise<BuildMaterialRequestDto> =>
  apiClient.post(`/v1/build/materials/${id}/cancel`);

export const linkMaterialMovement = (id: string, movementId: string | null): Promise<BuildMaterialRequestDto> =>
  apiClient.put(`/v1/build/materials/${id}/movement`, { movementId });

// ── Measurements (medições) ──────────────────────────────────────────────────

export type BuildMeasurementStatus = "Draft" | "Submitted" | "Approved" | "Rejected";

export interface BuildMeasurementItemDto {
  id:              string;
  stageId:         string;
  stageName:       string | null;
  previousPercent: number;
  measuredPercent: number;
  deltaPercent:    number;
  amount:          number | null;
  /** delta % × the stage's budgeted amount (reference budget). */
  suggestedAmount: number | null;
}

export interface BuildMeasurementDto {
  id:              string;
  projectId:       string;
  number:          number;
  periodStart:     string;
  periodEnd:       string;
  status:          BuildMeasurementStatus;
  notes:           string | null;
  submittedAt:     string | null;
  approvedByName:  string | null;
  approvedAt:      string | null;
  rejectedAt:      string | null;
  rejectedByName:  string | null;
  rejectionReason: string | null;
  totalAmount:     number;
  createdByName:   string;
  items:           BuildMeasurementItemDto[];
  createdAt:       string;
  updatedAt:       string;
}

export interface SaveBuildMeasurementRequest {
  periodStart: string;
  periodEnd:   string;
  notes?:      string | null;
  items:       Array<{ stageId: string; measuredPercent: number; amount?: number | null }>;
}

export const fetchMeasurements = (projectId: string): Promise<BuildMeasurementDto[]> =>
  apiClient.get(`/v1/build/projects/${projectId}/measurements`);

export const createMeasurement = (projectId: string, req: SaveBuildMeasurementRequest): Promise<BuildMeasurementDto> =>
  apiClient.post(`/v1/build/projects/${projectId}/measurements`, req);

export const updateMeasurement = (id: string, req: SaveBuildMeasurementRequest): Promise<BuildMeasurementDto> =>
  apiClient.put(`/v1/build/measurements/${id}`, req);

export const submitMeasurement = (id: string): Promise<BuildMeasurementDto> =>
  apiClient.post(`/v1/build/measurements/${id}/submit`);

export const approveMeasurement = (id: string): Promise<BuildMeasurementDto> =>
  apiClient.post(`/v1/build/measurements/${id}/approve`);

export const rejectMeasurement = (id: string, reason?: string): Promise<BuildMeasurementDto> =>
  apiClient.post(`/v1/build/measurements/${id}/reject`, reason ? { reason } : undefined);
