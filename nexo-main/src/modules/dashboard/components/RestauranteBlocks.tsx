import { useAuth } from "@/modules/auth/context/AuthContext";
import { useRestauranteTables } from "@/modules/restaurante/hooks/useRestauranteTables";
import { useKitchenItems } from "@/modules/restaurante/hooks/useKitchenItems";

/**
 * Only rendered when session.modules.includes("restaurante").
 * Shows two cards: open tables count and kitchen items count.
 * Empty state = meaningful message, NOT "0 mesas" as a KPI.
 * No fake/static numbers — blocks are not rendered until real queries resolve.
 */
export function RestauranteBlocks() {
  const { session } = useAuth();
  const storeId = session?.storeId ?? "";

  const { data: tables, isLoading: tablesLoading } =
    useRestauranteTables(storeId);
  const { data: kitchenItems, isLoading: kitchenLoading } =
    useKitchenItems(storeId);

  const openTables   = tables?.filter((t) => t.status === "Occupied").length ?? 0;
  const pendingItems = kitchenItems?.filter((i) => i.status !== "Delivered").length ?? 0;

  return (
    <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
      {/* Open Tables */}
      <div className="rounded-xl border border-border bg-card p-5">
        <p className="text-xs font-medium text-muted-foreground uppercase tracking-wide mb-1">
          Mesas abertas
        </p>
        {tablesLoading ? (
          <div className="h-8 w-16 bg-muted animate-pulse rounded mt-1" />
        ) : openTables === 0 ? (
          <p className="text-sm text-muted-foreground mt-1">
            Nenhuma mesa aberta agora.
          </p>
        ) : (
          <p className="text-3xl font-bold">{openTables}</p>
        )}
      </div>

      {/* Kitchen status */}
      <div className="rounded-xl border border-border bg-card p-5">
        <p className="text-xs font-medium text-muted-foreground uppercase tracking-wide mb-1">
          Pedidos na cozinha
        </p>
        {kitchenLoading ? (
          <div className="h-8 w-16 bg-muted animate-pulse rounded mt-1" />
        ) : pendingItems === 0 ? (
          <p className="text-sm text-muted-foreground mt-1">
            Tudo em ordem na cozinha.
          </p>
        ) : (
          <p className="text-3xl font-bold">{pendingItems}</p>
        )}
      </div>
    </div>
  );
}
