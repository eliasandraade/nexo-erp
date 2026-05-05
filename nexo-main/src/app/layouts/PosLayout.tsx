import { Outlet } from "react-router-dom";
import { UserDropdown } from "@/components/shared/UserDropdown";

export function PosLayout() {
  return (
    <div className="h-screen flex flex-col bg-background overflow-hidden">
      {/* Minimal top bar */}
      <header className="flex items-center justify-between px-4 py-2 border-b border-border bg-sidebar shrink-0">
        <div className="flex items-center gap-3">
          <img src="/orken_darkmode.png" alt="Orken" className="h-5 w-auto object-contain" />
          <span className="text-xs text-sidebar-muted">PDV — Ponto de Venda</span>
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
