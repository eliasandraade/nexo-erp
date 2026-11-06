import { useAuth } from "@/modules/auth/context/AuthContext";
import { readableModules } from "@/modules/auth/hooks/useRoleAccess";
import { hasServiceModule } from "../lib/service-family";

/**
 * True when the current tenant has the Service module, active or expired (read-only) (decision D1).
 * Used to gate the "service" route group / nav. Service screens land in a later PR.
 */
export function useHasServiceModule(): boolean {
  const { session } = useAuth();
  return hasServiceModule(session ? readableModules(session) : undefined);
}
