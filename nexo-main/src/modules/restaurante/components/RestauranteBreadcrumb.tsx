import { useNavigate, useLocation, Link } from "react-router-dom";
import { ChevronLeft, ChevronRight } from "lucide-react";
import { useAuth } from "@/modules/auth/context/AuthContext";
import { homeRoute, canAccessPath } from "@/modules/auth/hooks/useRoleAccess";

interface Crumb {
  section: string;
  parent?: { label: string; to: string };
}

/** Maps the current restaurante route to its breadcrumb trail under "Orken Menu". */
function resolveCrumb(pathname: string): Crumb {
  if (pathname.startsWith("/restaurante/delivery")) return { section: "Entregas" };
  if (pathname.startsWith("/restaurante/cozinha")) return { section: "Cozinha" };
  if (pathname.startsWith("/restaurante/mesa"))
    return { section: "Mesa", parent: { label: "Salão", to: "/restaurante" } };
  if (pathname.startsWith("/restaurante/comanda"))
    return { section: "Comanda", parent: { label: "Salão", to: "/restaurante" } };
  return { section: "Salão" };
}

/**
 * Context + back affordance for the full-screen Orken Menu operational pages
 * (Salão, Entregas, Cozinha, comandas) that have no sidebar. Shows where the
 * user is ("Orken Menu › Salão") and an explicit way out — never relying on the
 * browser back button. Role-aware: the back target and links resolve to routes
 * the active role can actually reach, so it never bounces off a guard.
 */
export function RestauranteBreadcrumb() {
  const { pathname } = useLocation();
  const navigate = useNavigate();
  const { session } = useAuth();

  const crumb = resolveCrumb(pathname);
  const menuHome = session ? homeRoute(session) : "/restaurante";
  const backTo = crumb.parent?.to ?? menuHome;

  const canGoBack =
    backTo !== pathname &&
    !!session &&
    canAccessPath(session.role, session.modules, backTo);

  return (
    <div className="flex min-w-0 items-center gap-1.5">
      {canGoBack && (
        <button
          type="button"
          onClick={() => navigate(backTo)}
          aria-label="Voltar"
          className="-ml-1 flex h-8 w-8 shrink-0 items-center justify-center rounded-md text-white/70 transition-colors hover:bg-white/10 hover:text-white focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-white/30"
        >
          <ChevronLeft className="h-[18px] w-[18px]" />
        </button>
      )}

      <nav aria-label="Trilha" className="flex min-w-0 items-center gap-1 text-[12.5px]">
        <Link
          to={menuHome}
          className="shrink-0 font-medium text-white/55 transition-colors hover:text-white"
        >
          Orken Menu
        </Link>
        {crumb.parent && (
          <>
            <ChevronRight className="h-3 w-3 shrink-0 text-white/30" />
            <Link
              to={crumb.parent.to}
              className="shrink-0 font-medium text-white/55 transition-colors hover:text-white"
            >
              {crumb.parent.label}
            </Link>
          </>
        )}
        <ChevronRight className="h-3 w-3 shrink-0 text-white/30" />
        <span className="truncate font-semibold text-white">{crumb.section}</span>
      </nav>
    </div>
  );
}
