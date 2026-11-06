import { useEffect } from "react";
import { useAuth } from "@/modules/auth/context/AuthContext";
import { KitchenBoard } from "../components/KitchenBoard";
import { KitchenConnectionBadge } from "../components/KitchenConnectionBadge";
import { KitchenSuspendedNotice } from "../components/KitchenSuspendedNotice";
import { useKitchenScreen } from "../hooks/useKitchenScreen";
import { UserDropdown } from "@/components/shared/UserDropdown";
import { RestauranteBreadcrumb } from "../components/RestauranteBreadcrumb";

export default function KitchenPage() {
  const { session } = useAuth();
  const storeId     = session?.storeId ?? "";

  const { items, connectionMode, suspended, checksByItself, checking, checkAgain } = useKitchenScreen(storeId);

  // Auto-fullscreen on first visit
  useEffect(() => {
    const asked = sessionStorage.getItem("kitchen:fullscreen-asked");
    if (!asked && document.fullscreenEnabled) {
      sessionStorage.setItem("kitchen:fullscreen-asked", "1");
      document.documentElement.requestFullscreen().catch(() => {});
    }
  }, []);

  return (
    <div className="flex flex-col h-screen p-4">
      {/* Header — Orken Menu context/back + connection + user menu */}
      <div className="flex items-center justify-between gap-3 mb-4">
        <RestauranteBreadcrumb dark />
        <div className="flex items-center gap-3">
          {!suspended && <KitchenConnectionBadge mode={connectionMode} />}
          <UserDropdown variant="dark" />
        </div>
      </div>

      {suspended && (
        <KitchenSuspendedNotice checksByItself={checksByItself} checking={checking} onCheckAgain={checkAgain} />
      )}

      {/* Board */}
      <div className="flex-1 overflow-hidden">
        <KitchenBoard items={items} storeId={storeId} readOnly={suspended} />
      </div>
    </div>
  );
}
