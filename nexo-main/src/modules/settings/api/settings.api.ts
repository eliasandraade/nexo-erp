import { useQuery } from "@tanstack/react-query";
import { apiClient } from "@/services/api-client";
import type { AppSettings } from "../types";

export const SETTINGS_KEY = ["settings"] as const;

export const fetchSettings = (): Promise<AppSettings> => apiClient.get<AppSettings>("/settings");

export const saveSettings = (settings: AppSettings): Promise<AppSettings> =>
  apiClient.put<AppSettings>("/settings", settings);

/** The company's settings (any signed-in role may read them — the PDV needs the discount rules). */
export function useSettings() {
  return useQuery({ queryKey: SETTINGS_KEY, queryFn: fetchSettings, staleTime: 5 * 60_000 });
}
