import { Outlet } from "react-router-dom";

/**
 * Mobile-first layout for waiter-facing pages (FloorPage, OrderPage).
 * No sidebar — maximizes screen real estate on phones.
 * Min-width 375px. Touch targets ≥ 44px enforced via Tailwind classes in child components.
 */
export function WaiterLayout() {
  return (
    <div className="min-h-screen bg-background flex flex-col">
      <Outlet />
    </div>
  );
}
