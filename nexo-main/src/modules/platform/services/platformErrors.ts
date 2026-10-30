import { toast } from "sonner";
import { ApiError } from "@/services/api-client";

/**
 * Administrative acts of the platform (tenants, modules, impersonation, password resets, sessions, AI providers…)
 * require a super_admin signed in WITH two-step verification. The server refuses anything else with an empty 403.
 */
export const ADMIN_ACTION_FORBIDDEN =
  "Esta ação exige um super administrador conectado com verificação em duas etapas.";

/** The message to show for a failed platform call (one place for every page). */
export function platformErrorMessage(e: unknown, fallback = "Erro ao executar a ação."): string {
  if (e instanceof ApiError) {
    // empty 403 = the policy (role or two-step verification); a 403 with a body keeps its own explanation
    if (e.status === 403 && !e.serverMessage) return ADMIN_ACTION_FORBIDDEN;
    return e.serverMessage ?? (e.message || fallback);
  }
  if (e instanceof Error && e.message) return e.message;
  return fallback;
}

/** Shared `onError` of platform mutations. */
export function toastPlatformError(e: unknown): void {
  toast.error(platformErrorMessage(e));
}
