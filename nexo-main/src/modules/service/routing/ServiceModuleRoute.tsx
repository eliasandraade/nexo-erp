import { Navigate, Outlet } from "react-router-dom";
import { useHasServiceModule } from "../hooks/useHasServiceModule";

interface ServiceModuleRouteProps {
  /** Where to redirect when the tenant has no Service-family module. Defaults to /dashboard. */
  fallback?: string;
}

/**
 * Family-aware module guard for the Service engine (decision D1). The frontend counterpart of
 * the backend `RequireServiceModuleAttribute`: any active Service-family vertical unlocks the
 * engine, so a single `moduleKey` check (as in `ModuleRoute`) is not enough.
 *
 * Always runs after ProtectedRoute, so the session is guaranteed non-null here.
 */
export function ServiceModuleRoute({ fallback = "/dashboard" }: ServiceModuleRouteProps) {
  const hasService = useHasServiceModule();
  return hasService ? <Outlet /> : <Navigate to={fallback} replace />;
}
