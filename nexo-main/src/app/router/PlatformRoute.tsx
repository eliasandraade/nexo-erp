import { Navigate, Outlet } from "react-router-dom";
import { useAuth } from "@/modules/auth/context/AuthContext";

/** Allows only platform users (type: "platform"). Redirects others to /login. */
export function PlatformRoute() {
  const { session, isReady } = useAuth();

  if (!isReady) return null;

  if (!session || session.type !== "platform") {
    return <Navigate to="/login" replace />;
  }

  return <Outlet />;
}
