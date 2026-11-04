import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { ApiError } from "@/services/api-client";
import {
  PREDICT_DISABLED_CODE, fetchForecast, fetchForecastHistory, fetchProjectWeather, recomputeForecast, setStageWeatherSensitivity,
} from "../api/build-forecast.api";
import { BUILD_KEYS } from "./use-build";

export const FORECAST_KEYS = {
  forecast: (projectId: string) => ["build", "forecast", projectId, "current"] as const,
  history:  (projectId: string) => ["build", "forecast", projectId, "history"] as const,
  weather:  (projectId: string) => ["build", "weather", projectId] as const,
} as const;

/** True for the 403 the forecast routes answer when BuildPredict is off for the company. */
export function isPredictDisabled(e: unknown): boolean {
  return e instanceof ApiError && e.status === 403 && e.code === PREDICT_DISABLED_CODE;
}

export function useForecast(projectId: string) {
  return useQuery({
    queryKey: FORECAST_KEYS.forecast(projectId),
    queryFn:  () => fetchForecast(projectId),
    enabled:  !!projectId,
    staleTime: 60_000,
    retry:    false,
  });
}

export function useForecastHistory(projectId: string, enabled = true) {
  return useQuery({
    queryKey: FORECAST_KEYS.history(projectId),
    queryFn:  () => fetchForecastHistory(projectId),
    enabled:  !!projectId && enabled,
    staleTime: 60_000,
    retry:    false,
  });
}

/** POST: recomputes and stores today's forecast; the history and the progress (which uses it) are refreshed. */
export function useRecomputeForecast(projectId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: () => recomputeForecast(projectId),
    onSuccess: (data) => {
      qc.setQueryData(FORECAST_KEYS.forecast(projectId), data);
      void qc.invalidateQueries({ queryKey: FORECAST_KEYS.history(projectId) });
      void qc.invalidateQueries({ queryKey: BUILD_KEYS.progressAll(projectId) });
      void qc.invalidateQueries({ queryKey: BUILD_KEYS.dashboard() });
    },
  });
}

export function useProjectWeather(projectId: string) {
  return useQuery({
    queryKey: FORECAST_KEYS.weather(projectId),
    queryFn:  () => fetchProjectWeather(projectId),
    enabled:  !!projectId,
    staleTime: 10 * 60_000,
    retry:    false,
  });
}

export function useSetWeatherSensitivity(projectId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ stageId, value }: { stageId: string; value: boolean | null }) => setStageWeatherSensitivity(stageId, value),
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: FORECAST_KEYS.weather(projectId) });
      void qc.invalidateQueries({ queryKey: ["build", "forecast", projectId] });
    },
  });
}
