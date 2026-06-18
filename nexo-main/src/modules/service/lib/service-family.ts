/**
 * Orken Service is ONE commercial module (decision v1.1): module key `service`, display
 * "Orken Service". The 9 verticals are INTERNAL presets (the "ramo"), chosen via in-app
 * onboarding (PUT /v1/service/settings/preset) — they are NOT keys in `session.modules`.
 */
export const SERVICE_MODULE_KEY = "service";

/** True when the tenant holds the single 'service' commercial module. */
export function hasServiceModule(modules: readonly string[] | undefined): boolean {
  return !!modules && modules.includes(SERVICE_MODULE_KEY);
}

export interface ServicePresetOption {
  key: string;
  label: string;
}

/**
 * The 9 internal presets offered in onboarding. Mirrors the backend `ServicePresetRegistry`
 * (keep in sync). After a preset is chosen, the resolved labels/capabilities come from
 * GET /v1/service/preset.
 */
export const SERVICE_PRESET_OPTIONS: ServicePresetOption[] = [
  { key: "clinica-medica",       label: "Clínicas Médicas e Odontológicas" },
  { key: "personal-trainer",     label: "Personal Trainers" },
  { key: "nutricionista",        label: "Nutricionistas" },
  { key: "oficina-mecanica",     label: "Oficinas Mecânicas" },
  { key: "programador-autonomo", label: "Programadores Autônomos" },
  { key: "autoescola",           label: "Autoescolas" },
  { key: "pet-shop",             label: "Pet Shops e Clínicas Veterinárias" },
  { key: "salao-beleza",         label: "Salões de Beleza" },
  { key: "escola-idiomas",       label: "Escolas de Idiomas" },
];

const PRESET_KEYS = new Set(SERVICE_PRESET_OPTIONS.map((o) => o.key));

/** True when `key` is one of the 9 internal presets. */
export function isValidPresetKey(key: string | undefined): boolean {
  return !!key && PRESET_KEYS.has(key);
}
