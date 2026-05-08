import { apiClient } from "@/services/api-client";

// ── Enums ─────────────────────────────────────────────────────────────────────

export type BuildProjectStatus =
  | "Planning"
  | "InProgress"
  | "Paused"
  | "Completed"
  | "Cancelled";

export type BuildProjectType =
  | "House"
  | "Commercial"
  | "Renovation"
  | "Building"
  | "Other";

export type BuildStageStatus =
  | "Pending"
  | "InProgress"
  | "Completed";

export type BuildBudgetStatus =
  | "Draft"
  | "Sent"
  | "Approved"
  | "Rejected"
  | "Converted";

// ── DTOs ──────────────────────────────────────────────────────────────────────

export interface BuildProjectDto {
  id:                   string;
  name:                 string;
  clientName:           string;
  location:             string | null;
  status:               BuildProjectStatus;
  type:                 BuildProjectType;
  startDate:            string | null;
  expectedEndDate:      string | null;
  actualEndDate:        string | null;
  budgetEstimated:      number | null;
  budgetApproved:       number | null;
  stageCount:           number;
  completedStageCount:  number;
  logCount:             number;
  createdAt:            string;
  updatedAt:            string;
}

export interface BuildStageDto {
  id:               string;
  projectId:        string;
  name:             string;
  description:      string | null;
  order:            number;
  status:           BuildStageStatus;
  plannedStartDate: string | null;
  plannedEndDate:   string | null;
  actualStartDate:  string | null;
  actualEndDate:    string | null;
  progressPercent:  number;
}

export interface BuildBudgetItemDto {
  id:        string;
  budgetId:  string;
  stageId:   string | null;
  name:      string;
  category:  string;
  quantity:  number;
  unit:      string;
  unitCost:  number;
  totalCost: number;
}

export interface BuildBudgetDto {
  id:            string;
  projectId:     string | null;
  name:          string;
  status:        BuildBudgetStatus;
  totalCost:     number;
  marginPercent: number;
  finalPrice:    number;
  createdAt:     string;
  updatedAt:     string;
  items:         BuildBudgetItemDto[];
}

export interface BuildDailyLogPhotoDto {
  id:         string;
  dailyLogId: string;
  storageKey: string;
  caption:    string | null;
  createdAt:  string;
}

export interface BuildDailyLogDto {
  id:             string;
  projectId:      string;
  date:           string;  // yyyy-MM-dd
  weatherSummary: string | null;
  notes:          string;
  createdBy:      string;
  createdAt:      string;
  updatedAt:      string;
  photos:         BuildDailyLogPhotoDto[];
}

export interface BuildProjectDetailsDto extends BuildProjectDto {
  stages:         BuildStageDto[];
  recentDailyLogs: BuildDailyLogDto[];
}

export interface BuildProjectFinancialSummaryDto {
  projectId:             string;
  budgetEstimated:       number | null;
  budgetApproved:        number | null;
  totalRealizedExpenses: number;
  movementCount:         number;
  lastMovementDate:      string | null;
  varianceAmount:        number;
  variancePercent:       number;
}

export interface BuildPagedResult<T> {
  items:    T[];
  total:    number;
  page:     number;
  pageSize: number;
}

// ── Request types ─────────────────────────────────────────────────────────────

export interface CreateBuildProjectRequest {
  name:            string;
  clientName:      string;
  location?:       string;
  type:            number;  // 0=Residential 1=Commercial 2=Industrial 3=Infrastructure
  budgetEstimated?: number;
  startDate?:       string;
  expectedEndDate?: string;
}

export interface UpdateBuildProjectRequest {
  name:             string;
  clientName:       string;
  location?:        string;
  budgetEstimated?: number;
  startDate?:       string;
  expectedEndDate?: string;
}

export interface CreateBuildStageRequest {
  name:             string;
  description?:     string;
  plannedStartDate?: string;
  plannedEndDate?:  string;
}

export interface UpdateBuildStageProgressRequest {
  progressPercent: number;
  status?:         BuildStageStatus;
}

export interface ReorderBuildStagesRequest {
  items: Array<{ stageId: string; order: number }>;
}

export interface CreateBuildBudgetRequest {
  name:          string;
  projectId?:    string;
  marginPercent?: number;
}

export interface AddBuildBudgetItemRequest {
  name:      string;
  category:  string;
  quantity:  number;
  unit:      string;
  unitCost:  number;
  stageId?:  string;
}

export interface UpdateBuildBudgetItemRequest {
  name:     string;
  category: string;
  quantity: number;
  unit:     string;
  unitCost: number;
}

export interface SetBudgetMarginRequest {
  marginPercent: number;
}

export interface ConvertBudgetToProjectRequest {
  projectId: string;
}

export interface CreateDailyLogRequest {
  date:            string;  // yyyy-MM-dd
  notes:           string;
  weatherSummary?: string;
}

export interface UpdateDailyLogRequest {
  notes:           string;
  weatherSummary?: string;
}

export interface AddDailyLogPhotoRequest {
  storageKey: string;
  caption?:   string;
}

// ── API functions ─────────────────────────────────────────────────────────────

// Projects
export const fetchProjects = (params?: {
  status?: BuildProjectStatus;
  page?: number;
  pageSize?: number;
}): Promise<BuildPagedResult<BuildProjectDto>> => {
  const p = new URLSearchParams();
  if (params?.status)   p.set("status",   params.status);
  if (params?.page)     p.set("page",     String(params.page));
  if (params?.pageSize) p.set("pageSize", String(params.pageSize));
  const qs = p.toString();
  return apiClient.get(`/v1/build/projects${qs ? `?${qs}` : ""}`);
};

export const fetchProject = (id: string): Promise<BuildProjectDto> =>
  apiClient.get(`/v1/build/projects/${id}`);

export const fetchProjectDetails = (id: string): Promise<BuildProjectDetailsDto> =>
  apiClient.get(`/v1/build/projects/${id}/details`);

export const fetchProjectFinancialSummary = (id: string): Promise<BuildProjectFinancialSummaryDto> =>
  apiClient.get(`/v1/build/projects/${id}/financial-summary`);

export const createProject = (req: CreateBuildProjectRequest): Promise<BuildProjectDto> =>
  apiClient.post(`/v1/build/projects`, req);

export const updateProject = (id: string, req: UpdateBuildProjectRequest): Promise<BuildProjectDto> =>
  apiClient.put(`/v1/build/projects/${id}`, req);

export const startProject    = (id: string): Promise<BuildProjectDto> =>
  apiClient.post(`/v1/build/projects/${id}/start`);
export const pauseProject    = (id: string): Promise<BuildProjectDto> =>
  apiClient.post(`/v1/build/projects/${id}/pause`);
export const completeProject = (id: string): Promise<BuildProjectDto> =>
  apiClient.post(`/v1/build/projects/${id}/complete`);
export const cancelProject   = (id: string): Promise<BuildProjectDto> =>
  apiClient.post(`/v1/build/projects/${id}/cancel`);

// Stages
export const fetchStages = (projectId: string): Promise<BuildStageDto[]> =>
  apiClient.get(`/v1/build/projects/${projectId}/stages`);

export const createStage = (projectId: string, req: CreateBuildStageRequest): Promise<BuildStageDto> =>
  apiClient.post(`/v1/build/projects/${projectId}/stages`, req);

export const updateStageProgress = (
  id: string,
  req: UpdateBuildStageProgressRequest,
): Promise<BuildStageDto> =>
  apiClient.put(`/v1/build/stages/${id}/progress`, req);

export const reorderStages = (projectId: string, req: ReorderBuildStagesRequest): Promise<void> =>
  apiClient.put(`/v1/build/projects/${projectId}/stages/reorder`, req);

export const deleteStage = (id: string): Promise<void> =>
  apiClient.delete(`/v1/build/stages/${id}`);

// Budgets
export const fetchBudgets = (params?: {
  projectId?: string;
  status?: BuildBudgetStatus;
  page?: number;
  pageSize?: number;
}): Promise<BuildPagedResult<BuildBudgetDto>> => {
  const p = new URLSearchParams();
  if (params?.projectId) p.set("projectId", params.projectId);
  if (params?.status)    p.set("status",    params.status);
  if (params?.page)      p.set("page",      String(params.page));
  if (params?.pageSize)  p.set("pageSize",  String(params.pageSize));
  const qs = p.toString();
  return apiClient.get(`/v1/build/budgets${qs ? `?${qs}` : ""}`);
};

export const fetchBudget = (id: string): Promise<BuildBudgetDto> =>
  apiClient.get(`/v1/build/budgets/${id}`);

export const createBudget = (req: CreateBuildBudgetRequest): Promise<BuildBudgetDto> =>
  apiClient.post(`/v1/build/budgets`, req);

export const sendBudget     = (id: string): Promise<BuildBudgetDto> =>
  apiClient.post(`/v1/build/budgets/${id}/send`);
export const approveBudget  = (id: string): Promise<BuildBudgetDto> =>
  apiClient.post(`/v1/build/budgets/${id}/approve`);
export const rejectBudget   = (id: string): Promise<BuildBudgetDto> =>
  apiClient.post(`/v1/build/budgets/${id}/reject`);
export const convertBudget  = (id: string, req: ConvertBudgetToProjectRequest): Promise<BuildBudgetDto> =>
  apiClient.post(`/v1/build/budgets/${id}/convert`, req);
export const setBudgetMargin = (id: string, req: SetBudgetMarginRequest): Promise<BuildBudgetDto> =>
  apiClient.put(`/v1/build/budgets/${id}/margin`, req);

export const addBudgetItem    = (budgetId: string, req: AddBuildBudgetItemRequest): Promise<BuildBudgetDto> =>
  apiClient.post(`/v1/build/budgets/${budgetId}/items`, req);
export const updateBudgetItem = (id: string, req: UpdateBuildBudgetItemRequest): Promise<BuildBudgetDto> =>
  apiClient.put(`/v1/build/budget-items/${id}`, req);
export const removeBudgetItem = (id: string): Promise<BuildBudgetDto> =>
  apiClient.delete(`/v1/build/budget-items/${id}`);

// Daily logs
export const fetchDailyLogs = (
  projectId: string,
  params?: { from?: string; to?: string; page?: number; pageSize?: number },
): Promise<BuildPagedResult<BuildDailyLogDto>> => {
  const p = new URLSearchParams();
  if (params?.from)     p.set("from",     params.from);
  if (params?.to)       p.set("to",       params.to);
  if (params?.page)     p.set("page",     String(params.page));
  if (params?.pageSize) p.set("pageSize", String(params.pageSize));
  const qs = p.toString();
  return apiClient.get(`/v1/build/projects/${projectId}/daily-logs${qs ? `?${qs}` : ""}`);
};

export const fetchDailyLog = (id: string): Promise<BuildDailyLogDto> =>
  apiClient.get(`/v1/build/daily-logs/${id}`);

export const createDailyLog = (projectId: string, req: CreateDailyLogRequest): Promise<BuildDailyLogDto> =>
  apiClient.post(`/v1/build/projects/${projectId}/daily-logs`, req);

export const updateDailyLog = (id: string, req: UpdateDailyLogRequest): Promise<BuildDailyLogDto> =>
  apiClient.put(`/v1/build/daily-logs/${id}`, req);

export const addDailyLogPhoto = (id: string, req: AddDailyLogPhotoRequest): Promise<BuildDailyLogDto> =>
  apiClient.post(`/v1/build/daily-logs/${id}/photos`, req);

export const removeDailyLogPhoto = (photoId: string): Promise<BuildDailyLogDto> =>
  apiClient.delete(`/v1/build/daily-log-photos/${photoId}`);
