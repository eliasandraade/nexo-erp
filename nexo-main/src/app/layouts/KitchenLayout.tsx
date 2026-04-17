import { Outlet } from "react-router-dom";

/**
 * Full-screen dark layout for KitchenPage.
 * Landscape-optimized for tablets. No nav chrome.
 */
export function KitchenLayout() {
  return (
    <div className="min-h-screen bg-gray-950 text-gray-100 flex flex-col overflow-hidden">
      <Outlet />
    </div>
  );
}
