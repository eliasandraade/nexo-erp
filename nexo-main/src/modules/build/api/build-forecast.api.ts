import { apiClient } from "@/services/api-client";

// Wave 6 — delivery forecast (productivity/1) and weather per activity (docs/build/forecast.md, internal-api.md "# Wave 6").

/** 403 code of the forecast routes when BuildPredict is off for the company. */
export const PREDICT_DISABLED_CODE = "build_predict_disabled";

export type BuildForecastSource = "Scheduled" | "Manual" | "OnTheFly" | (string & {});
export type BuildForecastMethod = "completed" | "window" | "overall" | "planned" | "none" | (string & {});
export type BuildForecastSeverity = "low" | "medium" | "high" | (string & {});
export type BuildForecastFactorKind =
  | "delivery_after_plan" | "low_productivity" | "inherited_delay" | "pending_change_order" | "weather_risk"
  | "no_productivity_data" | "no_forecast" | (string & {});

export interface BuildForecastStageDto {
  stageId:                  string;
  name:                     string;
  order:                    number;
  weight:                   number;
  status:                   string;
  actualPercent:            number;
  method:                   BuildForecastMethod;
  /** Productivity used: % per working day. */
  ratePerWorkingDay:        number | null;
  plannedRatePerWorkingDay: number | null;
  dataPoints:               number;
  plannedStart:             string | null;
  plannedEnd:               string | null;
  forecastStart:            string | null;
  forecastEnd:              string | null;
  delayDays:                number | null;
  inheritedDelayDays:       number;
  confidence:               number;
}

export interface BuildForecastFactorDto {
  key:      string;
  kind:     BuildForecastFactorKind;
  severity: BuildForecastSeverity;
  title:    string;
  evidence: string;
  stageId:  string | null;
  days:     number | null;
}

export interface BuildForecastCalendarDto {
  workingDays: string[];
  holidays:    string;
  windowDays:  number;
}

export interface BuildForecastDto {
  id:                 string | null;
  projectId:          string;
  date:               string;
  generatedAt:        string;
  source:             BuildForecastSource;
  stored:             boolean;
  methodology:        string;
  version:            number;
  forecastDelivery:   string | null;
  plannedDelivery:    string | null;
  /** Forecast − planned delivery (calendar days; positive = late). */
  delayDays:          number | null;
  expectedEnd:        string | null;
  /** Forecast − contractual end (calendar days; positive = late). */
  daysLateVsContract: number | null;
  /** 0..1 */
  confidence:         number;
  stages:             BuildForecastStageDto[];
  factors:            BuildForecastFactorDto[];
  calendar:           BuildForecastCalendarDto;
  createdByName:      string | null;
}

export interface BuildForecastHistoryItemDto {
  id:               string;
  date:             string;
  generatedAt:      string;
  source:           BuildForecastSource;
  methodology:      string;
  version:          number;
  forecastDelivery: string | null;
  plannedDelivery:  string | null;
  delayDays:        number | null;
  confidence:       number;
  createdByName:    string | null;
}

// ── Weather per activity ──────────────────────────────────────────────────────

export type BuildWeatherLevel = "none" | "medium" | "high" | (string & {});
export type BuildWeatherRiskKind = "heavy_rain" | "rain" | "strong_wind" | (string & {});
export type BuildWeatherUnavailableReason = "no_coordinates" | "weather_disabled" | "provider_unavailable" | (string & {});

export interface BuildWeatherRiskDto {
  stageId:   string;
  stageName: string;
  kind:      BuildWeatherRiskKind;
  level:     BuildWeatherLevel;
  message:   string;
}

export interface BuildWeatherDayDto {
  date:                     string;
  temperatureMax:           number | null;
  temperatureMin:           number | null;
  precipitationMm:          number | null;
  precipitationProbability: number | null;
  windSpeedMaxKmh:          number | null;
  description:              string | null;
  level:                    BuildWeatherLevel;
  risks:                    BuildWeatherRiskDto[];
}

export interface BuildWeatherStageDto {
  stageId:         string;
  name:            string;
  sensitive:       boolean;
  /** explicit (set by the company), keyword (name matched) or none. */
  basis:           "explicit" | "keyword" | "none" | (string & {});
  keyword:         string | null;
  explicitSetting: boolean | null;
  activeFrom:      string | null;
  activeTo:        string | null;
}

export interface BuildWeatherDto {
  projectId:  string;
  available:  boolean;
  reason:     BuildWeatherUnavailableReason | null;
  message:    string | null;
  source:     string;
  disclaimer: string;
  today:      BuildWeatherDayDto | null;
  days:       BuildWeatherDayDto[];
  stages:     BuildWeatherStageDto[];
}

// ── API functions ─────────────────────────────────────────────────────────────

const B = "/v1/build";

/** Today's stored forecast, or one computed on the fly (stored false). 403 build_predict_disabled when off. */
export const fetchForecast = (projectId: string) => apiClient.get<BuildForecastDto>(`${B}/projects/${projectId}/forecast`);

/** Recomputes and stores (source Manual); the history is kept. */
export const recomputeForecast = (projectId: string) => apiClient.post<BuildForecastDto>(`${B}/projects/${projectId}/forecast`);

export const fetchForecastHistory = (projectId: string, take = 60) =>
  apiClient.get<BuildForecastHistoryItemDto[]>(`${B}/projects/${projectId}/forecast/history?take=${take}`);

/** 200 with available false and a reason when there is no forecast for the site. */
export const fetchProjectWeather = (projectId: string) => apiClient.get<BuildWeatherDto>(`${B}/projects/${projectId}/weather`);

/** true/false sets the stage's sensitivity; null returns it to the keyword rule. */
export const setStageWeatherSensitivity = (stageId: string, weatherSensitive: boolean | null) =>
  apiClient.put<BuildWeatherStageDto>(`${B}/stages/${stageId}/weather-sensitivity`, { weatherSensitive });
