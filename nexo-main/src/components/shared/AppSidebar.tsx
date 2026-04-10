import { NavLink } from "react-router-dom";
import { cn } from "@/lib/utils";
import { appRoutes } from "@/app/router/routes";
import { useAuth } from "@/modules/auth/context/AuthContext";

export function AppSidebar() {
  const { session } = useAuth();

  const visibleRoutes = appRoutes.filter((route) =>
    !route.moduleKey || session?.modules.includes(route.moduleKey)
  );

  return (
    <aside className="w-60 min-h-screen bg-sidebar flex flex-col shrink-0">
      {/* Brand */}
      <div className="px-5 pt-6 pb-4">
        <h1 className="text-xl font-bold tracking-tight text-sidebar-primary-foreground">
          NEXO
        </h1>
        <p className="text-[11px] text-sidebar-muted mt-0.5 leading-tight">
          Gestão inteligente para empresas reais.
        </p>
      </div>

      {/* Navigation */}
      <nav className="flex-1 px-3 py-2 space-y-0.5 overflow-y-auto">
        {visibleRoutes.map((route) => (
          <NavLink
            key={route.path}
            to={route.path}
            className={({ isActive }) =>
              cn(
                "w-full flex items-center gap-3 px-3 py-2 rounded-md text-[13px] font-medium transition-colors",
                isActive
                  ? "bg-sidebar-accent text-sidebar-accent-foreground"
                  : "text-sidebar-foreground hover:bg-sidebar-accent/50 hover:text-sidebar-accent-foreground"
              )
            }
          >
            <route.icon className="h-4 w-4 shrink-0" />
            <span>{route.label}</span>
          </NavLink>
        ))}
      </nav>

      {/* Footer */}
      <div className="px-5 py-4 border-t border-sidebar-border">
        <p className="text-[10px] text-sidebar-muted">
          Andrade Systems © 2026
        </p>
      </div>
    </aside>
  );
}
