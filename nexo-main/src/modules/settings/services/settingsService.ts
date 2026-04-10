import type { AppSettings } from "../types";
import { defaultSettings } from "../data/defaultSettings";

const SETTINGS_KEY = "nexo:settings";
const delay = (ms = 400) => new Promise<void>((r) => setTimeout(r, ms));

/**
 * Shallow-merges each top-level section so that adding new fields to
 * defaultSettings in the future doesn't break existing persisted data.
 *
 * Also handles the legacy `store` key (renamed to `operation`) so that
 * data saved by previous versions of the app is migrated transparently.
 */
function mergeWithDefaults(stored: Record<string, unknown>): AppSettings {
  const storedOperation =
    (stored.operation as object | undefined) ??
    (stored.store as object | undefined) ??
    {};

  return {
    company: { ...defaultSettings.company, ...(stored.company as object ?? {}) },
    operation: { ...defaultSettings.operation, ...storedOperation },
    inventory: { ...defaultSettings.inventory, ...(stored.inventory as object ?? {}) },
    commission: { ...defaultSettings.commission, ...(stored.commission as object ?? {}) },
    pos: { ...defaultSettings.pos, ...(stored.pos as object ?? {}) },
    system: { ...defaultSettings.system, ...(stored.system as object ?? {}) },
  };
}

/**
 * Settings service — frontend-only, localStorage-backed.
 *
 * Future integration path:
 *   getSettings    → GET /api/settings
 *   updateSettings → PUT /api/settings  (body: Partial<AppSettings>)
 */
export const settingsService = {
  async getSettings(): Promise<AppSettings> {
    await delay(300);
    try {
      const raw = localStorage.getItem(SETTINGS_KEY);
      if (!raw) return structuredClone(defaultSettings);
      return mergeWithDefaults(JSON.parse(raw) as Record<string, unknown>);
    } catch {
      return structuredClone(defaultSettings);
    }
  },

  async updateSettings(partial: Partial<AppSettings>): Promise<AppSettings> {
    await delay(500);
    const current = await this.getSettings();
    const updated: AppSettings = {
      company: { ...current.company, ...(partial.company ?? {}) },
      operation: { ...current.operation, ...(partial.operation ?? {}) },
      inventory: { ...current.inventory, ...(partial.inventory ?? {}) },
      commission: { ...current.commission, ...(partial.commission ?? {}) },
      pos: { ...current.pos, ...(partial.pos ?? {}) },
      system: { ...current.system, ...(partial.system ?? {}) },
    };
    localStorage.setItem(SETTINGS_KEY, JSON.stringify(updated));
    return updated;
  },
};
