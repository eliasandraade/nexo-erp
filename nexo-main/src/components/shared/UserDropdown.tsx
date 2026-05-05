import { LogOut, UserCircle, ChevronDown } from "lucide-react";
import { useNavigate } from "react-router-dom";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";
import { useAuth } from "@/modules/auth/context/AuthContext";
import { roleLabels } from "@/modules/users/types";

function getInitials(name: string): string {
  return name
    .split(" ")
    .filter(Boolean)
    .slice(0, 2)
    .map((w) => w[0].toUpperCase())
    .join("");
}

interface UserDropdownProps {
  /** Visual variant — "dark" for layouts with dark backgrounds (PDV, Cozinha) */
  variant?: "default" | "dark";
}

/**
 * Compact user menu for layouts that don't use MainAppLayout.
 * Shows user initials + name, with logout and profile links.
 */
export function UserDropdown({ variant = "default" }: UserDropdownProps) {
  const { session, logout } = useAuth();
  const navigate = useNavigate();

  if (!session) return null;

  const initials     = getInitials(session.name);
  const displayName  = session.name;
  const displayRole  = roleLabels[session.role] ?? session.role;
  const isDark       = variant === "dark";

  return (
    <DropdownMenu>
      <DropdownMenuTrigger asChild>
        <button
          className={[
            "flex items-center gap-2 rounded-lg px-2 py-1.5 transition-colors",
            isDark
              ? "hover:bg-white/10 text-gray-100"
              : "hover:bg-sidebar-accent/50 text-sidebar-foreground",
          ].join(" ")}
        >
          <div className="w-7 h-7 rounded-full bg-primary flex items-center justify-center shrink-0">
            <span className="text-xs font-semibold text-primary-foreground">{initials}</span>
          </div>
          <div className="text-left hidden sm:block">
            <p className="text-xs font-medium leading-none">{displayName}</p>
            <p className={["text-[10px] leading-none mt-0.5", isDark ? "text-gray-400" : "text-sidebar-muted"].join(" ")}>
              {displayRole}
            </p>
          </div>
          <ChevronDown className={["h-3 w-3", isDark ? "text-gray-400" : "text-sidebar-muted"].join(" ")} />
        </button>
      </DropdownMenuTrigger>

      <DropdownMenuContent align="end" className="w-48">
        <DropdownMenuLabel className="font-normal">
          <p className="text-sm font-medium text-foreground">{displayName}</p>
          <p className="text-xs text-muted-foreground">{session.login}</p>
        </DropdownMenuLabel>
        <DropdownMenuSeparator />
        <DropdownMenuItem onClick={() => navigate("/perfil")} className="cursor-pointer">
          <UserCircle className="h-4 w-4 mr-2" />
          Meu perfil
        </DropdownMenuItem>
        <DropdownMenuSeparator />
        <DropdownMenuItem
          onClick={logout}
          className="text-destructive focus:text-destructive cursor-pointer"
        >
          <LogOut className="h-4 w-4 mr-2" />
          Sair
        </DropdownMenuItem>
      </DropdownMenuContent>
    </DropdownMenu>
  );
}
