import { useQuery } from "@tanstack/react-query";
import { useAuth } from "@/modules/auth/context/AuthContext";
import { fetchPlatformSecurity } from "../services/platformSecurityApi";

export const PLATFORM_SECURITY_KEY = ["platform", "auth", "security"] as const;

export function usePlatformSecurity() {
  return useQuery({
    queryKey: PLATFORM_SECURITY_KEY,
    queryFn: fetchPlatformSecurity,
    staleTime: 30_000,
  });
}

/**
 * Whether this administrator can perform administrative acts right now: role super_admin AND a sign-in that
 * passed two-step verification. `null` while unknown (still loading / could not check).
 */
export function useAdminActionsAllowed(): boolean | null {
  const { session } = useAuth();
  const security = usePlatformSecurity();
  if ((session?.role as string | undefined) === "support") return false;
  if (!security.data) return null;
  return security.data.currentSessionMfaVerified === true;
}
