import { Navigate, Outlet } from "react-router-dom";
import { useAuth } from "@/modules/auth/context/AuthContext";
import { canAccessPath, homeRoute } from "@/modules/auth/hooks/useRoleAccess";

interface RoleRouteProps {
  /**
   * The path this guard protects (checked against canAccessPath).
   * Use the most specific prefix that identifies the section,
   * e.g. "/pdv", "/dashboard", "/restaurante/cozinha".
   */
  path: string;
}

/**
 * Route guard for role-based access control.
 *
 * Always runs after ProtectedRoute (session is guaranteed non-null here).
 * If the active role cannot access `path`, redirects to that role's home route
 * instead of a generic 403 page — keeps the UX seamless.
 */
export function RoleRoute({ path }: RoleRouteProps) {
  const { session } = useAuth();

  if (!session) return <Navigate to="/login" replace />;

  if (!canAccessPath(session.role, session.modules, path)) {
    return <Navigate to={homeRoute(session)} replace />;
  }

  return <Outlet />;
}
