import { Brand } from "@/components/shared/Brand";
import { Outlet } from "react-router-dom";
import { UserDropdown } from "@/components/shared/UserDropdown";

export function PosLayout() {
  return (
    <div className="h-dvh flex flex-col bg-background overflow-hidden">
      {/* Minimal top bar */}
      <header className="flex items-center justify-between px-4 py-2 border-b border-border bg-sidebar shrink-0">
        <div className="flex min-w-0 flex-wrap items-center gap-3">
          <Brand />
          <span className="hidden text-xs text-sidebar-muted sm:block">Frente de caixa</span>
        </div>
        <UserDropdown />
      </header>

      {/* Main POS content */}
      <main className="flex-1 overflow-hidden">
        <Outlet />
      </main>
    </div>
  );
}
