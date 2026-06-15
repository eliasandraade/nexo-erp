import { Navigate, Outlet, useLocation } from "react-router-dom";
import { useAuth } from "@/modules/auth/context/AuthContext";

/**
 * Wraps all authenticated routes.
 *
 * Boot flow (optimistic render):
 * 1. Cached session present (read synchronously from localStorage) → render the
 *    app IMMEDIATELY. `/auth/me` validation runs in the background and only
 *    redirects to /login if the server rejects the token. This keeps the
 *    cold-backend round-trip OFF the first-paint critical path — the #1 cause
 *    of the long white screen after login. Security is unchanged: the API still
 *    enforces every request, and a revoked/expired token still bounces to login.
 * 2. No cached session + validation still pending (`!isReady`) → render null
 *    briefly (resolves synchronously on next tick) to avoid flashing the app.
 * 3. No cached session + ready → redirect to /login.
 *    3a. Trial expired + no active modules → redirect to /assinatura.
 *
 * The 401 + refresh-failure path is handled inside api-client.ts:
 * it calls clearTokens() which triggers AuthContext to set session = null
 * on the next validateSession() rejection, then navigates to /login.
 */
export function ProtectedRoute() {
  const { session, isReady } = useAuth();
  const location = useLocation();

  if (!session) return isReady ? <Navigate to="/login" replace /> : null;

  // If trial has expired and no paid modules are active, force the upgrade page.
  // Allow /assinatura and /perfil so the user can still manage their account.
  const BYPASS = ["/assinatura", "/perfil"];
  const trialExpired =
    !!session.trialEndsAt &&
    new Date(session.trialEndsAt) <= new Date() &&
    session.modules.length === 0;

  if (trialExpired && !BYPASS.some((p) => location.pathname.startsWith(p))) {
    return <Navigate to="/assinatura" replace />;
  }

  return <Outlet />;
}
