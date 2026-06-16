import { Navigate, Outlet } from "react-router-dom";
import { useAuth } from "@/modules/auth/context/AuthContext";

/**
 * Allows only platform users (type: "platform"). Redirects others to /login.
 *
 * Optimistic render (mirrors ProtectedRoute): a cached platform session renders
 * the admin app immediately; `/auth/me` validation runs in the background and
 * only bounces to /login if the server rejects the token. Keeps the cold-backend
 * round-trip off the first-paint path.
 */
export function PlatformRoute() {
  const { session, isReady } = useAuth();

  // Cached platform session → render now, validate in background.
  if (session?.type === "platform") return <Outlet />;

  // A non-platform cached session can never access platform routes.
  if (session) return <Navigate to="/login" replace />;

  // No session yet: wait for the (synchronous) validation tick, then redirect.
  return isReady ? <Navigate to="/login" replace /> : null;
}
