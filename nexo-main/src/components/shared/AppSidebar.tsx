import { Link, NavLink, useNavigate } from "react-router-dom";
import { LogOut, ChevronRight } from "lucide-react";
import { cn } from "@/lib/utils";
import { type RouteGroup } from "@/app/router/routes";
import { useAuth } from "@/modules/auth/context/AuthContext";
import { useWorkspace } from "@/modules/workspace/WorkspaceContext";
import { Brand } from "./Brand";
import { useNavigation } from "./useNavigation";
import { WorkspaceSwitcher } from "./WorkspaceSwitcher";

/** Vertical groups are scoped to the active workspace; the rest are shared. */

// ─── Group metadata ───────────────────────────────────────────────────────────

const GROUP_LABELS: Record<RouteGroup, string> = {
  core:        "Operação",
  inventario:  "Inventário",
  varejo:      "Varejo",
  restaurante: "Restaurante",
  build:       "Obras",
  service:     "Serviços",
  admin:       "Administração",
};

const GROUP_ORDER: RouteGroup[] = [
  "core",
  "inventario",
  "varejo",
  "restaurante",
  "build",
  "service",
  "admin",
];

// ─── Helpers ─────────────────────────────────────────────────────────────────

function getInitials(name: string): string {
  return name
    .split(" ")
    .filter(Boolean)
    .slice(0, 2)
    .map((w) => w[0].toUpperCase())
    .join("");
}

// ─── Sidebar content ─────────────────────────────────────────────────────────

export function SidebarContent({ onNav }: { onNav?: () => void }) {
  const { session, logout } = useAuth();
  const { active } = useWorkspace();
  const navigate = useNavigate();

  const visibleRoutes = useNavigation();
  const grouped = GROUP_ORDER.reduce<Record<RouteGroup, typeof visibleRoutes>>(
    (acc, g) => {
      acc[g] = visibleRoutes.filter((r) => r.group === g);
      return acc;
    },
    {} as Record<RouteGroup, typeof visibleRoutes>
  );

  const initials    = session ? getInitials(session.name) : "?";
  const displayName = session?.name ?? "—";
  const displayRole = session?.role ?? "—";

  function handleLogout() { logout(); onNav?.(); }
  function handleProfile() { navigate("/perfil"); onNav?.(); }

  return (
    <div className="flex flex-col h-full bg-sidebar border-r border-sidebar-border">

      {/* ── Wordmark ── */}
      <div className="px-4 pt-5 pb-3 shrink-0">
        <Link
          to={active?.home ?? "/dashboard"}
          onClick={onNav}
          className="inline-flex select-none items-center hover:opacity-80 transition-opacity"
          aria-label="Orken — início"
        >
          <Brand />
        </Link>
      </div>

      {/* ── Workspace switcher ── */}
      <WorkspaceSwitcher onNav={onNav} />

      {/* ── Navigation ── */}
      <nav aria-label="Navegação principal" className="flex-1 px-3 overflow-y-auto pb-3 space-y-4 sidebar-scroll">
        {GROUP_ORDER.map((group) => {
          const routes = grouped[group];
          if (!routes.length) return null;
          return (
            <div key={group}>
              {/* Section label */}
              <p className="px-2 mb-1 text-[11px] font-semibold uppercase tracking-[0.14em] text-sidebar-muted select-none">
                {GROUP_LABELS[group]}
              </p>

              {/* Routes */}
              <div className="space-y-0.5">
                {routes.map((route) => (
                  <NavLink
                    key={route.path}
                    to={route.path}
                    end={route.path === "/restaurante" || route.path === "/service" || route.path === "/estoque"}
                    onClick={onNav}
                    className={({ isActive }) =>
                      cn(
                        "flex items-center gap-2.5 px-2.5 py-2 rounded-md text-sm font-medium transition-colors duration-75",
                        isActive
                          ? "bg-sidebar-accent text-sidebar-accent-foreground"
                          : "text-sidebar-foreground hover:bg-sidebar-accent hover:text-sidebar-accent-foreground"
                      )
                    }
                  >
                    {({ isActive }) => (
                      <>
                        <route.icon
                          className={cn(
                            "h-[14px] w-[14px] shrink-0 transition-colors duration-75",
                            isActive ? "text-primary" : "text-current opacity-70"
                          )}
                        />
                        <span className="flex-1">{route.label}</span>
                        {isActive && (
                          <div className="w-1 h-1 rounded-full bg-primary shrink-0" />
                        )}
                      </>
                    )}
                  </NavLink>
                ))}
              </div>
            </div>
          );
        })}
      </nav>

      {/* ── User footer ── */}
      <div className="shrink-0 px-3 pb-3 pt-2 border-t border-sidebar-border">
        {/* Profile */}
        <button
          onClick={handleProfile}
          className="w-full flex items-center gap-2.5 px-2.5 py-2 rounded-md hover:bg-sidebar-accent transition-colors group"
        >
          <div className="w-6 h-6 rounded-full bg-primary/80 flex items-center justify-center shrink-0">
            <span className="text-xs font-bold text-primary-foreground leading-none">{initials}</span>
          </div>
          <div className="min-w-0 text-left flex-1">
            <p className="text-sm font-medium text-sidebar-accent-foreground truncate leading-tight">{displayName}</p>
            <p className="text-xs text-sidebar-muted leading-tight capitalize">{displayRole}</p>
          </div>
          <ChevronRight className="h-3 w-3 text-sidebar-muted group-hover:text-sidebar-accent-foreground transition-colors shrink-0" />
        </button>

        {/* Logout */}
        <button
          onClick={handleLogout}
          className="w-full flex items-center gap-2.5 px-2.5 py-1.5 mt-0.5 rounded-md text-[12px] text-sidebar-muted hover:text-red-400/90 hover:bg-red-500/[0.06] transition-colors"
        >
          <LogOut className="h-3.5 w-3.5 shrink-0" />
          <span>Sair da conta</span>
        </button>
      </div>
    </div>
  );
}

// ─── Desktop sidebar ─────────────────────────────────────────────────────────

export function AppSidebar() {
  return (
    <aside className="sticky top-0 hidden h-dvh w-60 shrink-0 flex-col lg:flex">
      <SidebarContent />
    </aside>
  );
}
