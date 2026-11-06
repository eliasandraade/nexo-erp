import { useEffect } from "react";
import { Navigate, Outlet, useLocation, useNavigate } from "react-router-dom";
import { toast } from "sonner";
import { useAuth } from "@/modules/auth/context/AuthContext";
import { canAccessPath, READ_ONLY_SUBSCRIPTION_URL } from "@/modules/auth/hooks/useRoleAccess";
import { isImpersonating, onSubscriptionRequired } from "@/services/api-client";

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
 *
 * A company in read-only is not redirected: it keeps browsing and exporting its data.
 * See docs/engineering/tenant-access-enforcement.md.
 *
 * The 401 + refresh-failure path is handled inside api-client.ts:
 * it calls clearTokens() which triggers AuthContext to set session = null
 * on the next validateSession() rejection, then navigates to /login.
 */
export function ProtectedRoute() {
  const { session, isReady } = useAuth();
  const location = useLocation();
  const navigate = useNavigate();

  // A write refused for lack of a subscription (402): whoever is consulting stays on the screen, and the session stays. The
  // subscription page is for Diretoria and Gerente: for the other roles a link would only send them back home.
  const role = session?.role;
  useEffect(() => onSubscriptionRequired(() => {
    toast.warning("Nada foi registrado: esta área está somente para consulta.", {
      id: "subscription-required",
      action: role && canAccessPath(role, [], "/assinatura")
        ? { label: "Ver assinatura", onClick: () => navigate(READ_ONLY_SUBSCRIPTION_URL) }
        : undefined,
    });
  }), [role, navigate]);

  if (!session) return isReady ? <Navigate to="/login" replace /> : null;

  // A temporary password (set by a manager) is replaced before anything else.
  // (Not in a platform access: the administrator cannot change the user's password there.)
  if (session.requirePasswordChange && !isImpersonating() && !location.pathname.startsWith("/perfil")) {
    return <Navigate to="/perfil?trocar-senha=1" replace />;
  }

  return <Outlet />;
}
