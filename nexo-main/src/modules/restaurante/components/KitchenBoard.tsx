import { KitchenCard } from "./KitchenCard";
import type { KitchenItem } from "../types";

const COLUMNS: { status: KitchenItem["status"]; label: string; empty: string }[] = [
  { status: "Pending",   label: "Pendente",   empty: "Sem novos pedidos" },
  { status: "Preparing", label: "Preparando", empty: "Nada em preparo" },
  { status: "Ready",     label: "Pronto",     empty: "Nada pronto ainda" },
];

export function KitchenBoard({
  items, storeId,
}: {
  items: KitchenItem[];
  storeId: string;
}) {
  return (
    <div className="grid grid-cols-3 gap-4 h-full overflow-hidden">
      {COLUMNS.map(({ status, label, empty }) => {
        const col = items.filter((i) => i.status === status);
        return (
          <div key={status} className="flex flex-col gap-2 overflow-y-auto">
            <div className="flex items-center justify-between px-1 sticky top-0 bg-gray-950 pb-2">
              <h2 className="text-sm font-semibold text-gray-300 uppercase tracking-wider">
                {label}
              </h2>
              <span className="text-xs bg-gray-800 rounded-full px-2 py-0.5 text-gray-400">
                {col.length}
              </span>
            </div>
            {col.length === 0 ? (
              <p className="text-xs text-gray-600 text-center mt-4">
                {empty}
              </p>
            ) : (
              col.map((item) => (
                <KitchenCard key={item.itemId} item={item} storeId={storeId} />
              ))
            )}
          </div>
        );
      })}
    </div>
  );
}
