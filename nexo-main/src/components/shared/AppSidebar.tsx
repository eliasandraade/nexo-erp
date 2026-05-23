import { NavLink, useNavigate } from "react-router-dom";
import { LogOut, UserCircle } from "lucide-react";
import { cn } from "@/lib/utils";
import { appRoutes, type RouteGroup } from "@/app/router/routes";
import { useAuth } from "@/modules/auth/context/AuthContext";

// ─── Group metadata ───────────────────────────────────────────────────────────

const GROUP_LABELS: Record<RouteGroup, string> = {
  core:        "Operação",
  inventario:  "Inventário",
  varejo:      "Varejo",
  restaurante: "Restaurante",
  build:       "Obras",
  admin:       "Administração",
};

const GROUP_ORDER: RouteGroup[] = [
  "core",
  "inventario",
  "varejo",
  "restaurante",
  "build",
  "admin",
];

// ─── Initials helper ─────────────────────────────────────────────────────────

function getInitials(name: string): string {
  return name
    .split(" ")
    .filter(Boolean)
    .slice(0, 2)
    .map((w) => w[0].toUpperCase())
    .join("");
}

// ─── Sidebar content — used by both desktop and mobile drawer ────────────────

export function SidebarContent({ onNav }: { onNav?: () => void }) {
  const { session, logout } = useAuth();
  const navigate = useNavigate();

  const visibleRoutes = appRoutes.filter((route) => {
    if (route.moduleKey && !session?.modules.includes(route.moduleKey)) return false;
    if (route.roles && session?.role && !route.roles.includes(session.role)) return false;
    return true;
  });

  // Group visible routes preserving GROUP_ORDER
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

  function handleLogout() {
    logout();
    onNav?.();
  }

  function handleProfile() {
    navigate("/perfil");
    onNav?.();
  }

  return (
    <div className="flex flex-col h-full bg-sidebar">
      {/* ── Brand ── */}
      <div className="px-5 pt-6 pb-5 shrink-0">
        <a
          href="/dashboard"
          className="inline-block font-display text-[18px] font-bold text-white tracking-tight select-none hover:opacity-80 transition-opacity"
        >
          Ork<span className="text-[#5B4DFF]">en</span>
        </a>
      </div>

      {/* ── Navigation ── */}
      <nav className="flex-1 px-3 overflow-y-auto pb-4 space-y-5">
        {GROUP_ORDER.map((group) => {
          const routes = grouped[group];
          if (!routes.length) return null;
          return (
            <div key={group}>
              {/* Section label */}
              <p className="px-3 mb-1.5 text-[10px] font-semibold uppercase tracking-[0.12em] text-sidebar-muted select-none">
                {GROUP_LABELS[group]}
              </p>

              {/* Routes */}
              <div className="space-y-0.5">
                {routes.map((route) => (
                  <NavLink
                    key={route.path}
                    to={route.path}
                    end={route.path === "/restaurante"}
                    onClick={onNav}
                    className={({ isActive }) =>
                      cn(
                        "flex items-center gap-3 px-3 py-[7px] rounded-md text-[13px] font-medium transition-all duration-100",
                        "border-l-2",
                        isActive
                          ? "border-[#5B4DFF] bg-white/[0.07] text-white"
                          : "border-transparent text-sidebar-foreground hover:bg-white/[0.05] hover:text-white"
                      )
                    }
                  >
                    <route.icon className="h-[15px] w-[15px] shrink-0" />
                    <span>{route.label}</span>
                  </NavLink>
                ))}
              </div>
            </div>
          );
        })}
      </nav>

      {/* ── User section ── */}
      <div className="shrink-0 px-3 pb-4 border-t border-sidebar-border pt-3">
        {/* Profile button */}
        <button
          onClick={handleProfile}
          className="w-full flex items-center gap-3 px-3 py-2.5 rounded-md hover:bg-white/[0.05] transition-colors group"
        >
          <div className="w-7 h-7 rounded-full bg-[#5B4DFF] flex items-center justify-center shrink-0">
            <span className="text-[11px] font-bold text-white leading-none">{initials}</span>
          </div>
          <div className="min-w-0 text-left flex-1">
            <p className="text-[12px] font-medium text-white truncate leading-none">{displayName}</p>
            <p className="text-[10px] text-sidebar-muted mt-0.5 leading-none capitalize">{displayRole}</p>
          </div>
          <UserCircle className="h-3.5 w-3.5 text-sidebar-muted group-hover:text-white transition-colors shrink-0" />
        </button>

        {/* Logout */}
        <button
          onClick={handleLogout}
          className="w-full flex items-center gap-3 px-3 py-2 rounded-md text-[12px] font-medium text-sidebar-muted hover:text-red-400 hover:bg-white/[0.04] transition-colors mt-0.5"
        >
          <LogOut className="h-[14px] w-[14px] shrink-0" />
          <span>Sair</span>
        </button>
      </div>
    </div>
  );
}

// ─── Desktop sidebar ─────────────────────────────────────────────────────────

export function AppSidebar() {
  return (
    <aside className="hidden md:flex w-60 min-h-screen shrink-0 flex-col">
      <SidebarContent />
    </aside>
  );
}
