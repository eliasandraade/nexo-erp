import { useState } from "react";
import { cn } from "@/lib/utils";
import type { KitchenItem } from "../types";
import { useUpdateItemStatus } from "../hooks/useOrderMutations";

const STATUS_SEQUENCE: Record<string, string> = {
  Pending:   "Preparing",
  Preparing: "Ready",
  Ready:     "Delivered",
};

const STATUS_ACTION: Record<string, string> = {
  Pending:   "Iniciar",
  Preparing: "Pronto",
  Ready:     "Entregue",
};

function elapsed(since: string | null): { label: string; color: string } {
  if (!since) return { label: "", color: "text-gray-400" };
  const mins = Math.floor((Date.now() - new Date(since).getTime()) / 60_000);
  const label = mins < 1 ? "<1 min" : `${mins} min`;
  const color = mins < 5 ? "text-green-400" : mins < 10 ? "text-amber-400" : "text-red-400";
  return { label, color };
}

export function KitchenCard({
  item, storeId,
}: {
  item: KitchenItem;
  storeId: string;
}) {
  const updateMut          = useUpdateItemStatus(storeId);
  const [pending, setPending] = useState(false);
  const { label, color }   = elapsed(item.sentToKitchenAt);
  const nextStatus         = STATUS_SEQUENCE[item.status];

  const handleAdvance = async () => {
    if (!nextStatus || pending) return;
    setPending(true);
    try {
      await updateMut.mutateAsync({
        orderId: item.orderId,
        itemId:  item.itemId,
        status:  nextStatus,
      });
    } finally {
      setPending(false);
    }
  };

  return (
    <div className="bg-gray-900 rounded-xl p-4 border border-gray-700 flex flex-col gap-2">
      <div className="flex justify-between items-start">
        <span className="text-xs text-gray-400">
          {item.tableNumber ? `Mesa ${item.tableNumber}` : item.orderType} · #{item.orderNumber}
        </span>
        {label && <span className={cn("text-xs font-medium", color)}>{label}</span>}
      </div>

      <p className="text-lg font-bold leading-tight">
        {item.quantity}× {item.productName}
      </p>

      {item.modifiers.length > 0 && (
        <p className="text-sm text-gray-300">
          {item.modifiers.map((m) => m.labelSnapshot).join(", ")}
        </p>
      )}

      {item.notes && (
        <p className="text-sm text-amber-300 italic">"{item.notes}"</p>
      )}

      {nextStatus && (
        <button
          onClick={handleAdvance}
          disabled={pending}
          className="mt-1 w-full rounded-lg bg-gray-700 hover:bg-gray-600 disabled:opacity-50 py-2 text-sm font-medium transition-colors"
        >
          {pending ? "..." : STATUS_ACTION[item.status]}
        </button>
      )}
    </div>
  );
}
