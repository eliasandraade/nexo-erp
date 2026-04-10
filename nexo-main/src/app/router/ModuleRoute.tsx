import { Navigate, Outlet } from "react-router-dom";
import { useAuth } from "@/modules/auth/context/AuthContext";

interface ModuleRouteProps {
  /** The module key that must be active in `session.modules` */
  moduleKey: string;
  /**
   * Where to redirect if the module is not active.
   * Defaults to /dashboard.
   */
  fallback?: string;
}

/**
 * Route guard for tenant-scoped module access.
 *
 * Renders child routes only if `session.modules` contains `moduleKey`.
 * Otherwise redirects to `fallback` (default: /dashboard).
 *
 * Always runs after ProtectedRoute, so session is guaranteed non-null here.
 */
export function ModuleRoute({ moduleKey, fallback = "/dashboard" }: ModuleRouteProps) {
  const { session } = useAuth();

  if (!session?.modules.includes(moduleKey)) {
    return <Navigate to={fallback} replace />;
  }

  return <Outlet />;
}
