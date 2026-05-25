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
  return name.split(" ").filter(Boolean).slice(0, 2).map((w) => w[0].toUpperCase()).join("");
}

export function AppHeader() {
  const { session, logout } = useAuth();
  const navigate = useNavigate();

  const initials    = session ? getInitials(session.name) : "?";
  const displayName = session?.name ?? "—";
  const displayRole = session ? (roleLabels[session.role] ?? session.role) : "—";

  return (
    <header className="h-12 border-b border-border bg-card flex items-center justify-between px-5 shrink-0 gap-4">

      {/* Left: store switcher */}
      <div className="flex items-center gap-2 shrink-0">
        <StoreSwitcher />
      </div>

      {/* Center: search */}
      <div className="flex-1 max-w-xs">
        <div className="relative">
          <Search className="absolute left-2.5 top-1/2 -translate-y-1/2 h-3.5 w-3.5 text-muted-foreground pointer-events-none" />
          <input
            type="text"
            placeholder="Buscar..."
            className="w-full h-7 pl-8 pr-9 rounded-md bg-muted border border-transparent text-[12.5px] text-foreground placeholder:text-muted-foreground focus:outline-none focus:border-border focus:bg-background transition-colors"
          />
          <kbd className="absolute right-2 top-1/2 -translate-y-1/2 hidden sm:flex items-center text-[9px] text-muted-foreground/60 font-mono">
            ⌘K
          </kbd>
        </div>
      </div>

      {/* Right: bell + user */}
      <div className="flex items-center gap-0.5 shrink-0">
        <button
          className="p-1.5 rounded-md hover:bg-muted transition-colors text-muted-foreground hover:text-foreground"
          aria-label="Notificações"
        >
          <Bell className="h-3.5 w-3.5" />
        </button>

        <div className="w-px h-4 bg-border mx-1.5" />

        <DropdownMenu>
          <DropdownMenuTrigger asChild>
            <button className="flex items-center gap-2 hover:bg-muted rounded-md px-1.5 py-1 transition-colors">
              <div className="w-6 h-6 rounded-full bg-primary flex items-center justify-center shrink-0">
                <span className="text-[10px] font-semibold text-primary-foreground leading-none">{initials}</span>
              </div>
              <div className="text-left hidden sm:block">
                <p className="text-[11.5px] font-medium text-foreground leading-none">{displayName}</p>
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
            <DropdownMenuItem onClick={logout} className="text-destructive focus:text-destructive cursor-pointer">
              <LogOut className="h-4 w-4 mr-2" />
              Sair
            </DropdownMenuItem>
          </DropdownMenuContent>
        </DropdownMenu>
      </div>
    </header>
  );
}
