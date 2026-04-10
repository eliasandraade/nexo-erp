import { Navigate, Outlet } from "react-router-dom";
import { useAuth } from "@/modules/auth/context/AuthContext";

/**
 * Wraps all authenticated routes.
 *
 * Boot flow:
 * 1. `isReady = false` — localStorage session populated synchronously; background
 *    /auth/me validation is in flight. Return null to avoid flashing the login page.
 * 2. `isReady = true, session = null` — no valid session → redirect to /login.
 * 3. `isReady = true, session != null` — validated → render child routes.
 *
 * The 401 + refresh-failure path is handled inside api-client.ts:
 * it calls clearTokens() which triggers AuthContext to set session = null
 * on the next validateSession() rejection, then navigates to /login.
 */
export function ProtectedRoute() {
  const { session, isReady } = useAuth();

  if (!isReady) {
    return null;
  }

  if (!session) {
    return <Navigate to="/login" replace />;
  }

  return <Outlet />;
}
