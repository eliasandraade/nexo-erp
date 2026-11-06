import { Outlet } from "react-router-dom";
import { ReadOnlyNotice } from "@/components/shared/ReadOnlyNotice";

/**
 * Full-screen dark layout for KitchenPage.
 * Landscape-optimized for tablets. No nav chrome.
 */
export function KitchenLayout() {
  return (
    <div className="min-h-screen bg-gray-950 text-gray-100 flex flex-col overflow-hidden">
      <ReadOnlyNotice className="bg-gray-900 text-gray-300" />
      <Outlet />
    </div>
  );
}
