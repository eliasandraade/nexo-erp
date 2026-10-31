import { ApiError } from "@/services/api-client";

/** 422 code of the quality rule: a stage with an open Critical non-conformity cannot reach 100%. */
export const STAGE_BLOCKED_CODE = "stage_has_critical_nonconformity";

/** Codes whose `details` are item names: shown after the server's own sentence. */
const LISTS_ITEMS = new Set(["inspection_required_items_pending", "inspection_photo_required"]);

/** Message of a failed mutation for a toast — the server's own reason when it sends one. */
export function errorMessage(e: unknown, fallback: string): string {
  if (e instanceof ApiError) {
    const own = e.serverMessage ?? e.message;
    if (e.code === STAGE_BLOCKED_CODE)
      return `${own || "Etapa com não conformidade crítica aberta."} Resolva ou cancele a não conformidade crítica `
        + "(aba Qualidade) para concluir a etapa.";
    if (e.code && LISTS_ITEMS.has(e.code) && e.serverMessage && e.details?.length)
      return `${e.serverMessage} ${e.details.join("; ")}.`;
  }
  return e instanceof Error && e.message ? e.message : fallback;
}
