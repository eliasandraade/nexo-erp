import { Outlet } from "react-router-dom";
import { UserDropdown } from "@/components/shared/UserDropdown";

/**
 * Mobile-first layout for waiter-facing pages (FloorPage, OrderPage, DeliveryPage).
 * Minimal top bar with logo + user menu. No sidebar.
 * Min-width 375px. Touch targets ≥ 44px enforced via Tailwind classes in child components.
 */
export function WaiterLayout() {
  return (
    <div className="min-h-screen bg-background flex flex-col">
      {/* Minimal header — logo + user menu */}
      <header className="flex items-center justify-between px-4 py-3 border-b border-sidebar-border bg-sidebar shrink-0">
        <span className="font-display text-[16px] font-bold text-white tracking-tight select-none">
          Ork<span className="text-[#5B4DFF]">en</span>
        </span>
        <UserDropdown />
      </header>

      <Outlet />
    </div>
  );
}
