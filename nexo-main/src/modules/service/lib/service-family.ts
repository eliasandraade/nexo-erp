/**
 * The Orken Service module family (decision D1): per-vertical billable SKUs that all unlock
 * the SAME Service engine. Mirrors the backend source of truth
 * `Nexo.Domain.Modules.Service.ServicePresetRegistry` — keep the two lists in sync.
 */
export const SERVICE_FAMILY_KEYS = [
  "clinica-medica",
  "personal-trainer",
  "nutricionista",
  "oficina-mecanica",
  "programador-autonomo",
  "autoescola",
  "pet-shop",
  "salao-beleza",
  "escola-idiomas",
] as const;

const FAMILY = new Set<string>(SERVICE_FAMILY_KEYS);

/** True when `key` is one of the service-family verticals. */
export function isServiceFamilyKey(key: string | undefined): boolean {
  return !!key && FAMILY.has(key);
}

/** True when the tenant holds at least one active service-family module. */
export function hasServiceModule(modules: readonly string[] | undefined): boolean {
  return !!modules && modules.some(isServiceFamilyKey);
}
