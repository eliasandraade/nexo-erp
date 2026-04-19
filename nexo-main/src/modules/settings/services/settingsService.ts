import type { AppSettings } from "../types";
import { fetchSettings, saveSettings } from "../api/settings.api";

/**
 * Settings service — backed by /api/settings.
 * localStorage is no longer used.
 */
export const settingsService = {
  async getSettings(): Promise<AppSettings> {
    return fetchSettings();
  },

  async updateSettings(partial: Partial<AppSettings>): Promise<AppSettings> {
    const current = await fetchSettings();
    const merged: AppSettings = {
      company:    { ...current.company,    ...(partial.company    ?? {}) },
      operation:  { ...current.operation,  ...(partial.operation  ?? {}) },
      inventory:  { ...current.inventory,  ...(partial.inventory  ?? {}) },
      commission: { ...current.commission, ...(partial.commission ?? {}) },
      pos:        { ...current.pos,        ...(partial.pos        ?? {}) },
      system:     { ...current.system,     ...(partial.system     ?? {}) },
    };
    return saveSettings(merged);
  },
};
