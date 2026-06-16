import { Outlet, NavLink, Link, useNavigate } from "react-router-dom";
import {
  Building2, LayoutDashboard, LogOut, ShieldCheck,
  Activity, ScrollText, Clock, Flag, Brain,
  FlaskConical, Plug, Radio, DollarSign, FileCode,
  UserCircle,
} from "lucide-react";
import { cn } from "@/lib/utils";
import { useAuth } from "@/modules/auth/context/AuthContext";

// ─── Nav link helper ─────────────────────────────────────────────────────────

function PlatformNavLink({
  to, end = false, icon: Icon, children,
}: {
  to: string;
  end?: boolean;
  icon: React.ElementType;
  children: React.ReactNode;
}) {
  return (
    <NavLink
      to={to}
      end={end}
      className={({ isActive }) =>
        cn(
          "flex items-center gap-3 px-3 py-[7px] rounded-md text-[13px] font-medium transition-all duration-100 border-l-2",
          isActive
            ? "border-[#5B4DFF] bg-white/[0.07] text-white"
            : "border-transparent text-sidebar-foreground hover:bg-white/[0.05] hover:text-white"
        )
      }
    >
      <Icon className="h-[15px] w-[15px] shrink-0" />
      {children}
    </NavLink>
  );
}

// ─── Section label ────────────────────────────────────────────────────────────

function NavSection({ label }: { label: string }) {
  return (
    <p className="px-3 mb-1.5 text-[10px] font-semibold uppercase tracking-[0.12em] text-sidebar-muted select-none">
      {label}
    </p>
  );
}

// ─── Initials ────────────────────────────────────────────────────────────────

function getInitials(name: string): string {
  return name
    .split(" ")
    .filter(Boolean)
    .slice(0, 2)
    .map((w) => w[0].toUpperCase())
    .join("");
}

// ─── Layout ───────────────────────────────────────────────────────────────────

export function PlatformLayout() {
  const { session, logout } = useAuth();
  const navigate = useNavigate();

  const initials    = session ? getInitials(session.name ?? "SU") : "SU";
  const displayName = session?.name ?? session?.email ?? "Super Admin";

  return (
    <div className="flex h-screen bg-background overflow-hidden">

      {/* ── Sidebar ── */}
      <aside className="w-56 shrink-0 flex flex-col bg-sidebar">

        {/* Brand */}
        <div className="px-5 pt-6 pb-5 shrink-0 flex items-center gap-2">
          <ShieldCheck className="h-4 w-4 text-[#5B4DFF] shrink-0" />
          <Link
            to="/platform"
            className="font-display text-[16px] font-bold text-white tracking-tight hover:opacity-80 transition-opacity"
          >
            Ork<span className="text-[#5B4DFF]">en</span>
            <span className="text-sidebar-muted font-normal text-[12px] ml-1.5">admin</span>
          </Link>
        </div>

        {/* Nav */}
        <nav className="flex-1 px-3 overflow-y-auto pb-4 space-y-5">

          {/* Platform */}
          <div>
            <NavSection label="Plataforma" />
            <div className="space-y-0.5">
              <PlatformNavLink to="/platform"          end  icon={LayoutDashboard}>Dashboard</PlatformNavLink>
              <PlatformNavLink to="/platform/tenants"       icon={Building2}>Clientes</PlatformNavLink>
              <PlatformNavLink to="/platform/trial"         icon={Clock}>Trial</PlatformNavLink>
              <PlatformNavLink to="/platform/activity"      icon={ScrollText}>Atividade</PlatformNavLink>
              <PlatformNavLink to="/platform/flags"         icon={Flag}>Flags</PlatformNavLink>
              <PlatformNavLink to="/platform/system"        icon={Activity}>Sistema</PlatformNavLink>
            </div>
          </div>

          {/* AI Operations */}
          <div>
            <NavSection label="AI Operations" />
            <div className="space-y-0.5">
              <PlatformNavLink to="/platform/ai"            end  icon={Brain}>AI Dashboard</PlatformNavLink>
              <PlatformNavLink to="/platform/ai/playground"      icon={FlaskConical}>Playground</PlatformNavLink>
              <PlatformNavLink to="/platform/ai/providers"       icon={Plug}>Providers</PlatformNavLink>
              <PlatformNavLink to="/platform/ai/telemetry"       icon={Radio}>Telemetry</PlatformNavLink>
              <PlatformNavLink to="/platform/ai/costs"           icon={DollarSign}>Custos</PlatformNavLink>
              <PlatformNavLink to="/platform/ai/prompts"         icon={FileCode}>Prompts</PlatformNavLink>
            </div>
          </div>

        </nav>

        {/* User section */}
        <div className="shrink-0 px-3 pb-4 border-t border-sidebar-border pt-3">
          <button
            onClick={() => navigate("/perfil")}
            className="w-full flex items-center gap-3 px-3 py-2.5 rounded-md hover:bg-white/[0.05] transition-colors group"
          >
            <div className="w-7 h-7 rounded-full bg-[#5B4DFF] flex items-center justify-center shrink-0">
              <span className="text-[11px] font-bold text-white leading-none">{initials}</span>
            </div>
            <div className="min-w-0 text-left flex-1">
              <p className="text-[12px] font-medium text-white truncate leading-none">{displayName}</p>
              <p className="text-[10px] text-sidebar-muted mt-0.5 leading-none">super_admin</p>
            </div>
            <UserCircle className="h-3.5 w-3.5 text-sidebar-muted group-hover:text-white transition-colors shrink-0" />
          </button>

          <button
            onClick={logout}
            className="w-full flex items-center gap-3 px-3 py-2 rounded-md text-[12px] font-medium text-sidebar-muted hover:text-red-400 hover:bg-white/[0.04] transition-colors mt-0.5"
          >
            <LogOut className="h-[14px] w-[14px] shrink-0" />
            Sair
          </button>
        </div>
      </aside>

      {/* ── Main content ── */}
      <main className="flex-1 overflow-auto bg-background">
        <Outlet />
      </main>
    </div>
  );
}
