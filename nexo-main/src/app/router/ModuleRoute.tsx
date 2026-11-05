import { Navigate, Outlet } from "react-router-dom";
import { useAuth } from "@/modules/auth/context/AuthContext";
import { readableModules } from "@/modules/auth/hooks/useRoleAccess";

interface ModuleRouteProps {
  /** The module key that must be active or expired (read-only) in the session */
  moduleKey: string;
  /**
   * Where to redirect if the company never had the module.
   * Defaults to /dashboard.
   */
  fallback?: string;
}

/**
 * Route guard for tenant-scoped module access.
 *
 * Renders child routes if the module has access or expired: an expired module stays open, read-only.
 * Otherwise redirects to `fallback` (default: /dashboard).
 *
 * Always runs after ProtectedRoute, so session is guaranteed non-null here.
 */
export function ModuleRoute({ moduleKey, fallback = "/dashboard" }: ModuleRouteProps) {
  const { session } = useAuth();

  if (!session || !readableModules(session).includes(moduleKey)) {
    return <Navigate to={fallback} replace />;
  }

  return <Outlet />;
}
