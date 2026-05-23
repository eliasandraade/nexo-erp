import { Search, Bell, ChevronDown, LogOut, UserCircle } from "lucide-react";
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
import { StoreSwitcher } from "./StoreSwitcher";

function getInitials(name: string): string {
  return name
    .split(" ")
    .filter(Boolean)
    .slice(0, 2)
    .map((w) => w[0].toUpperCase())
    .join("");
}

export function AppHeader() {
  const { session, logout } = useAuth();
  const navigate = useNavigate();

  const initials    = session ? getInitials(session.name) : "?";
  const displayName = session?.name ?? "—";
  const displayRole = session ? (roleLabels[session.role] ?? session.role) : "—";

  return (
    <header className="h-14 border-b border-border bg-background flex items-center justify-between px-6 shrink-0 gap-4">

      {/* Left: store switcher */}
      <div className="flex items-center gap-2 shrink-0">
        <StoreSwitcher />
      </div>

      {/* Center: search */}
      <div className="flex-1 max-w-sm">
        <div className="relative">
          <Search className="absolute left-3 top-1/2 -translate-y-1/2 h-[14px] w-[14px] text-muted-foreground pointer-events-none" />
          <input
            type="text"
            placeholder="Buscar..."
            className="
              w-full h-8 pl-8 pr-10 rounded-lg
              bg-muted border border-transparent
              text-[13px] text-foreground placeholder:text-muted-foreground
              focus:outline-none focus:border-border focus:bg-background
              transition-colors duration-150
            "
          />
          <kbd className="absolute right-2.5 top-1/2 -translate-y-1/2 hidden sm:flex items-center gap-0.5 text-[10px] text-muted-foreground bg-background border border-border px-1.5 py-0.5 rounded leading-none">
            ⌘K
          </kbd>
        </div>
      </div>

      {/* Right: bell + user */}
      <div className="flex items-center gap-1 shrink-0">
        <button
          className="p-2 rounded-lg hover:bg-muted transition-colors text-muted-foreground hover:text-foreground"
          aria-label="Notificações"
        >
          <Bell className="h-[15px] w-[15px]" />
        </button>

        <div className="w-px h-5 bg-border mx-1" />

        <DropdownMenu>
          <DropdownMenuTrigger asChild>
            <button className="flex items-center gap-2 hover:bg-muted rounded-lg px-2 py-1.5 transition-colors">
              <div className="w-7 h-7 rounded-full bg-primary flex items-center justify-center shrink-0">
                <span className="text-xs font-semibold text-primary-foreground leading-none">
                  {initials}
                </span>
              </div>
              <div className="text-left hidden sm:block">
                <p className="text-[12px] font-medium text-foreground leading-none">{displayName}</p>
                <p className="text-[10px] text-muted-foreground leading-none mt-0.5">{displayRole}</p>
              </div>
              <ChevronDown className="h-3 w-3 text-muted-foreground" />
            </button>
          </DropdownMenuTrigger>

          <DropdownMenuContent align="end" className="w-52">
            <DropdownMenuLabel className="font-normal">
              <p className="text-sm font-medium text-foreground">{displayName}</p>
              <p className="text-xs text-muted-foreground">{session?.login}</p>
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
      </div>
    </header>
  );
}
