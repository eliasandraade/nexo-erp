import { useState } from "react";
import { Outlet } from "react-router-dom";
import { Menu, X } from "lucide-react";
import { AppSidebar, SidebarContent } from "@/components/shared/AppSidebar";
import { AppHeader } from "@/components/shared/AppHeader";

// ─── Mobile sidebar drawer ────────────────────────────────────────────────────

function MobileDrawer({ open, onClose }: { open: boolean; onClose: () => void }) {
  if (!open) return null;
  return (
    <>
      {/* Backdrop */}
      <div
        className="fixed inset-0 z-40 bg-black/60 md:hidden"
        onClick={onClose}
        aria-hidden
      />
      {/* Drawer panel */}
      <div className="fixed inset-y-0 left-0 z-50 w-64 md:hidden">
        <SidebarContent onNav={onClose} />
      </div>
    </>
  );
}

// ─── Layout ───────────────────────────────────────────────────────────────────

export function MainAppLayout() {
  const [drawerOpen, setDrawerOpen] = useState(false);

  return (
    <div className="flex min-h-screen w-full bg-background">
      {/* Desktop sidebar */}
      <AppSidebar />

      {/* Mobile drawer */}
      <MobileDrawer open={drawerOpen} onClose={() => setDrawerOpen(false)} />

      {/* Content */}
      <div className="flex-1 flex flex-col min-w-0">
        {/* Mobile hamburger — shown on md and below */}
        <div className="md:hidden flex items-center h-14 px-4 border-b border-border bg-card shrink-0">
          <button
            onClick={() => setDrawerOpen(true)}
            className="p-2 rounded-lg hover:bg-muted transition-colors text-muted-foreground hover:text-foreground"
            aria-label="Abrir menu"
          >
            <Menu className="h-5 w-5" />
          </button>
          <span className="ml-3 font-display font-bold text-[16px] text-foreground">
            Ork<span className="text-[#5B4DFF]">en</span>
          </span>
        </div>

        {/* Desktop header */}
        <div className="hidden md:block">
          <AppHeader />
        </div>

        <main className="flex-1 p-4 md:p-6 overflow-auto">
          <Outlet />
        </main>
      </div>
    </div>
  );
}
