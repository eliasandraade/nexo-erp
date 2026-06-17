import { useAuth } from "@/modules/auth/context/AuthContext";
import { hasServiceModule } from "../lib/service-family";

/**
 * True when the current tenant has any active Service-family module (decision D1).
 * Used to gate the "service" route group / nav. Service screens land in a later PR.
 */
export function useHasServiceModule(): boolean {
  const { session } = useAuth();
  return hasServiceModule(session?.modules);
}
