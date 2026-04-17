import { useEffect } from "react";
import { useAuth } from "@/modules/auth/context/AuthContext";
import { KitchenBoard } from "../components/KitchenBoard";
import { KitchenConnectionBadge } from "../components/KitchenConnectionBadge";
import { useKitchenSocket } from "../hooks/useKitchenSocket";
import { useKitchenItems } from "../hooks/useKitchenItems";

export default function KitchenPage() {
  const { session }     = useAuth();
  const storeId         = session?.storeId ?? "";
  const token           = session ? localStorage.getItem("nexo:access_token") ?? undefined : undefined;

  const { connectionMode } = useKitchenSocket(storeId, token);

  // Polling is only active when SignalR has failed; interval driven by hook
  const { data: items = [] } = useKitchenItems(
    storeId,
    connectionMode === "polling" ? 10_000 : undefined
  );

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
      {/* Header */}
      <div className="flex items-center justify-between mb-4">
        <h1 className="text-xl font-bold text-white">Cozinha</h1>
        <KitchenConnectionBadge mode={connectionMode} />
      </div>

      {/* Board */}
      <div className="flex-1 overflow-hidden">
        <KitchenBoard items={items} storeId={storeId} />
      </div>
    </div>
  );
}
