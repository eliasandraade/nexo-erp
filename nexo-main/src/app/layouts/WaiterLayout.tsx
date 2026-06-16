import { Outlet } from "react-router-dom";
import { UserDropdown } from "@/components/shared/UserDropdown";
import { RestauranteBreadcrumb } from "@/modules/restaurante/components/RestauranteBreadcrumb";

/**
 * Mobile-first layout for waiter-facing pages (FloorPage, OrderPage, DeliveryPage).
 * Minimal top bar with logo + user menu. No sidebar.
 * Min-width 375px. Touch targets ≥ 44px enforced via Tailwind classes in child components.
 */
export function WaiterLayout() {
  return (
    <div className="min-h-screen bg-background flex flex-col">
      {/* Minimal header — Orken Menu context/back + user menu */}
      <header className="flex items-center justify-between gap-3 px-4 py-3 border-b border-sidebar-border bg-sidebar shrink-0">
        <RestauranteBreadcrumb />
        <UserDropdown />
      </header>

      <Outlet />
    </div>
  );
}
