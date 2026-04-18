import { useQuery } from "@tanstack/react-query";
import { apiClient } from "@/services/api-client";
import { useAuth } from "@/modules/auth/context/AuthContext";

/**
 * Fetches resolved feature flags for the authenticated tenant.
 * Returns a flat dictionary: { "pdv-desconto-gerente": true, ... }
 *
 * Cache: 2 minutes (server-side Redis) + 2 minutes staleTime client-side.
 * When a platform admin toggles a flag, the Redis cache is invalidated immediately.
 * The next refetch (on staleTime expiry or window focus) picks up the change.
 *
 * Usage:
 *   const { isEnabled } = useFeatureFlags();
 *   if (isEnabled("pdv-desconto-gerente")) { ... }
 */
export function useFeatureFlags() {
  const { session } = useAuth();
  const isAuthenticated = !!session && session.type === "tenant";

  const query = useQuery({
    queryKey: ["features"],
    queryFn: () => apiClient.get<Record<string, boolean>>("/features"),
    enabled: isAuthenticated,
    staleTime: 2 * 60 * 1000,   // 2 minutes — matches server-side Redis TTL
    gcTime: 10 * 60 * 1000,     // keep in memory for 10 minutes
  });

  const flags = query.data ?? {};

  return {
    /** Returns the resolved value of a flag. Defaults to false if not yet loaded. */
    isEnabled: (key: string): boolean => flags[key] ?? false,
    /** The raw flags dictionary, for bulk inspection. */
    flags,
    isLoading: query.isLoading,
  };
}
