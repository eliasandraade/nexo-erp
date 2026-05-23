import { UtensilsCrossed, ChefHat } from "lucide-react";
import { useAuth } from "@/modules/auth/context/AuthContext";
import { useRestauranteTables } from "@/modules/restaurante/hooks/useRestauranteTables";
import { useKitchenItems } from "@/modules/restaurante/hooks/useKitchenItems";

/**
 * Only rendered when session.modules.includes("restaurante").
 * Shows two operational KPIs: open tables count and kitchen items count.
 */
export function RestauranteBlocks() {
  const { session } = useAuth();
  const storeId = session?.storeId ?? "";

  const { data: tables,      isLoading: tablesLoading  } = useRestauranteTables(storeId);
  const { data: kitchenItems, isLoading: kitchenLoading } = useKitchenItems(storeId);

  const openTables   = tables?.filter((t) => t.status === "Occupied").length ?? 0;
  const pendingItems = kitchenItems?.filter((i) => i.status !== "Delivered").length ?? 0;

  return (
    <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">

      {/* Open Tables */}
      <div className="rounded-xl border border-border bg-card p-5 relative overflow-hidden">
        <div className="absolute top-0 left-0 right-0 h-[2px] bg-[#5B4DFF]" />
        <div className="flex items-center justify-between mb-3 pt-0.5">
          <p className="text-[11px] font-semibold uppercase tracking-[0.09em] text-muted-foreground">
            Mesas abertas
          </p>
          <UtensilsCrossed className="h-3.5 w-3.5 text-[#5B4DFF]" />
        </div>
        {tablesLoading ? (
          <div className="h-8 w-16 bg-muted animate-pulse rounded" />
        ) : (
          <p className="font-display text-[26px] font-bold text-foreground leading-none">
            {openTables === 0 ? "—" : openTables}
          </p>
        )}
        <p className="text-[11px] mt-2 text-muted-foreground font-medium">
          {tablesLoading ? "" : openTables === 0 ? "Nenhuma mesa ocupada" : "mesas em atendimento"}
        </p>
      </div>

      {/* Kitchen status */}
      <div className="rounded-xl border border-border bg-card p-5 relative overflow-hidden">
        <div className={`absolute top-0 left-0 right-0 h-[2px] ${pendingItems > 0 ? "bg-warning" : "bg-success"}`} />
        <div className="flex items-center justify-between mb-3 pt-0.5">
          <p className="text-[11px] font-semibold uppercase tracking-[0.09em] text-muted-foreground">
            Cozinha
          </p>
          <ChefHat className={`h-3.5 w-3.5 ${pendingItems > 0 ? "text-warning" : "text-success"}`} />
        </div>
        {kitchenLoading ? (
          <div className="h-8 w-16 bg-muted animate-pulse rounded" />
        ) : (
          <p className="font-display text-[26px] font-bold text-foreground leading-none">
            {pendingItems === 0 ? "—" : pendingItems}
          </p>
        )}
        <p className="text-[11px] mt-2 text-muted-foreground font-medium">
          {kitchenLoading ? "" : pendingItems === 0 ? "Tudo em ordem" : "pedidos em preparo"}
        </p>
      </div>

    </div>
  );
}
