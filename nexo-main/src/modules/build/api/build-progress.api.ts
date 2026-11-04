import { apiClient } from "@/services/api-client";

// ── Progress (weighted stages, baseline, snapshots, curves) ───────────────────

/** Known stage flags; the server may add others (shown as-is). */
export type BuildProgressStageFlag = "missing_planned_dates" | (string & {});

export interface BuildProgressStageDto {
  stageId:        string;
  name:           string;
  order:          number;
  weight:         number;
  status:         string;
  plannedPercent: number | null;
  actualPercent:  number;
  plannedStart:   string | null;
  plannedEnd:     string | null;
  actualStart:    string | null;
  actualEnd:      string | null;
  forecastEnd:    string | null;
  delayDays:      number | null;
  flags:          BuildProgressStageFlag[];
}

export interface BuildProgressPointDto {
  date:    string;   // yyyy-MM-dd
  percent: number;
}

export interface BuildProgressCurvesDto {
  planned:  BuildProgressPointDto[];
  actual:   BuildProgressPointDto[];
  forecast: BuildProgressPointDto[];
}

export type BuildPlanSource = "baseline" | "revision" | "current";

export interface BuildProgressDto {
  projectId:          string;
  date:               string;
  plannedPercent:     number | null;
  actualPercent:      number;
  forecastPercent:    number | null;
  plannedDelivery:    string | null;
  forecastDelivery:   string | null;
  methodology:        string;
  planSource:         BuildPlanSource;
  planRevisionNumber: number | null;
  stages:             BuildProgressStageDto[];
  curves:             BuildProgressCurvesDto;
  warnings:           string[];
}

export interface BuildProgressSnapshotStageDto {
  stageId:        string;
  name:           string;
  order:          number;
  weight:         number;
  status:         string;
  plannedPercent: number | null;
  actualPercent:  number;
  plannedEnd:     string | null;
  forecastEnd:    string | null;
  delayDays:      number | null;
}

/** "Como estava a obra em X": the latest daily snapshot on or before `requestedDate`. */
export interface BuildProgressSnapshotDto {
  projectId:            string;
  requestedDate:        string;
  date:                 string;
  plannedPercent:       number | null;
  actualPercent:        number;
  forecastPercent:      number | null;
  forecastDeliveryDate: string | null;
  methodology:          string;
  stages:               BuildProgressSnapshotStageDto[];
  updatedAt:            string;
}

export interface BuildScheduleItemDto {
  stageId:      string;
  name:         string;
  plannedStart: string | null;
  plannedEnd:   string | null;
  weight:       number;
}

export interface BuildScheduleRevisionDto {
  id:            string;
  number:        number;
  kind:          string;   // "Baseline" | "Revision"
  reason:        string | null;
  items:         BuildScheduleItemDto[];
  createdByName: string;
  createdAt:     string;
}

// ── Cost per stage ────────────────────────────────────────────────────────────

export interface BuildStageCostDto {
  stageId:       string | null;   // null = "Sem etapa"
  stageName:     string;
  order:         number | null;
  budgeted:      number;
  realized:      number;
  /** Budgeted − realized (positive = under budget). */
  variance:      number;
  movementCount: number;
}

export interface BuildCostByStageDto {
  projectId:     string;
  totalBudgeted: number;
  totalRealized: number;
  totalVariance: number;
  stages:        BuildStageCostDto[];
}

export interface BuildMovementStageDto {
  movementId: string;
  projectId:  string;
  stageId:    string | null;
  stageName:  string | null;
}

// ── API functions ─────────────────────────────────────────────────────────────

export const fetchProjectProgress = (projectId: string): Promise<BuildProgressDto> =>
  apiClient.get(`/v1/build/projects/${projectId}/progress`);

/** 404 when there is no snapshot on or before the date. */
export const fetchProjectProgressAt = (projectId: string, date: string): Promise<BuildProgressSnapshotDto> =>
  apiClient.get(`/v1/build/projects/${projectId}/progress/at?date=${encodeURIComponent(date)}`);

/** First call freezes the baseline; later ones are revisions and need a reason (422 otherwise). */
export const freezeScheduleBaseline = (projectId: string, reason?: string): Promise<BuildScheduleRevisionDto> =>
  apiClient.post(`/v1/build/projects/${projectId}/schedule/baseline`, reason ? { reason } : {});

export const fetchScheduleRevisions = (projectId: string): Promise<BuildScheduleRevisionDto[]> =>
  apiClient.get(`/v1/build/projects/${projectId}/schedule/revisions`);

export const fetchCostByStage = (projectId: string): Promise<BuildCostByStageDto> =>
  apiClient.get(`/v1/build/projects/${projectId}/cost-by-stage`);

export const assignMovementStage = (
  projectId: string,
  movementId: string,
  stageId: string | null,
): Promise<BuildMovementStageDto> =>
  apiClient.put(`/v1/build/projects/${projectId}/movements/${movementId}/stage`, { stageId });
