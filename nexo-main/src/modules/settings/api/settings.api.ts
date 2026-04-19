import { apiClient } from "@/services/api-client";
import type { AppSettings } from "../types";

export const fetchSettings = (): Promise<AppSettings> =>
  apiClient.get<AppSettings>("/settings");

export const saveSettings = (settings: AppSettings): Promise<AppSettings> =>
  apiClient.put<AppSettings>("/settings", settings);
