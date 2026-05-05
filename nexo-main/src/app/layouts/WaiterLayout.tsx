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
      <header className="flex items-center justify-between px-4 py-2 border-b border-border bg-sidebar shrink-0">
        <img src="/orken_darkmode.png" alt="Orken" className="h-5 w-auto object-contain" />
        <UserDropdown />
      </header>

      <Outlet />
    </div>
  );
}
