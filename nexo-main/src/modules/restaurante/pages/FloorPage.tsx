import { useState } from "react";
import { useNavigate } from "react-router-dom";
import { Plus } from "lucide-react";
import { useAuth } from "@/modules/auth/context/AuthContext";
import { TableCard } from "../components/TableCard";
import { AreaTabs } from "../components/AreaTabs";
import { OpenOrderSheet } from "../components/OpenOrderSheet";
import { useRestauranteTables } from "../hooks/useRestauranteTables";
import { useRestauranteAreas } from "../hooks/useRestauranteAreas";
import { useOpenOrder } from "../hooks/useOrderMutations";
import type { OrderType, TableDto } from "../types";

export default function FloorPage() {
  const { session } = useAuth();
  const storeId     = session?.storeId ?? "";
  const navigate    = useNavigate();

  const { data: tables = [], isLoading: tablesLoading } = useRestauranteTables(storeId);
  const { data: areas  = [] }                           = useRestauranteAreas(storeId);
  const openOrderMut                                    = useOpenOrder(storeId);

  const [activeAreaId, setActiveAreaId]   = useState<string | null>(null);
  const [selectedTable, setSelectedTable] = useState<TableDto | null>(null);
  const [sheetOpen, setSheetOpen]         = useState(false);

  const visibleTables = activeAreaId
    ? tables.filter((t) => t.areaId === activeAreaId)
    : tables;

  const handleTableClick = (table: TableDto) => {
    if (table.status === "Occupied") {
      navigate(`/restaurante/mesa/${table.id}`);
      return;
    }
    setSelectedTable(table);
    setSheetOpen(true);
  };

  const handleCounterClick = () => {
    setSelectedTable(null);
    setSheetOpen(true);
  };

  const handleOpenOrder = async (orderType: OrderType, partySize: number | null) => {
    const result = await openOrderMut.mutateAsync({
      orderType,
      tableId:   selectedTable?.id ?? null,
      partySize: partySize ?? null,
    });
    setSheetOpen(false);
    navigate(`/restaurante/comanda/${result.id}`);
  };

  return (
    <div className="flex flex-col h-screen overflow-hidden">
      <div className="px-4 pt-5 pb-3 flex items-center justify-between border-b border-border">
        <div>
          <h1 className="text-lg font-semibold">Salão</h1>
          <p className="text-xs text-muted-foreground">
            {tables.filter((t) => t.status === "Occupied").length} mesa(s) ocupada(s)
          </p>
        </div>
        <button
          onClick={handleCounterClick}
          className="flex items-center gap-1.5 rounded-lg bg-primary px-3 py-2 text-sm font-medium text-primary-foreground"
        >
          <Plus className="h-4 w-4" />
          Balcão
        </button>
      </div>

      <div className="px-4 pt-3 pb-2">
        <AreaTabs areas={areas} activeAreaId={activeAreaId} onSelect={setActiveAreaId} />
      </div>

      <div className="flex-1 overflow-y-auto px-4 pb-4">
        {tablesLoading ? (
          <div className="grid grid-cols-3 gap-3 mt-2">
            {Array.from({ length: 6 }).map((_, i) => (
              <div key={i} className="h-[88px] rounded-xl bg-muted animate-pulse" />
            ))}
          </div>
        ) : visibleTables.length === 0 ? (
          <p className="text-center text-muted-foreground mt-12 text-sm">
            Nenhuma mesa cadastrada nesta área.
          </p>
        ) : (
          <div className="grid grid-cols-3 gap-3 mt-2">
            {visibleTables.map((table) => (
              <TableCard
                key={table.id}
                table={table}
                onClick={() => handleTableClick(table)}
              />
            ))}
          </div>
        )}
      </div>

      <OpenOrderSheet
        open={sheetOpen}
        tableNumber={selectedTable?.number}
        onClose={() => setSheetOpen(false)}
        onSubmit={handleOpenOrder}
        isLoading={openOrderMut.isPending}
      />
    </div>
  );
}
