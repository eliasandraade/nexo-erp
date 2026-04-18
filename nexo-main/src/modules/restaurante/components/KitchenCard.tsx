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

function elapsed(since: string | null): { mins: number; label: string; textColor: string } {
  if (!since) return { mins: 0, label: "", textColor: "text-gray-400" };
  const mins = Math.floor((Date.now() - new Date(since).getTime()) / 60_000);
  const label     = mins < 1 ? "<1 min" : `${mins} min`;
  const textColor = mins < 5 ? "text-green-400" : mins < 10 ? "text-amber-400" : "text-red-400";
  return { mins, label, textColor };
}

// Card border + background vary with status and urgency
function cardStyle(status: string, mins: number): string {
  if (status === "Ready") {
    // Ready cards get a green tint — waiting to be picked up
    return "border-green-500/50 bg-green-950/20";
  }
  if (status === "Preparing") {
    if (mins >= 15) return "border-red-500/60 bg-red-950/15";  // critically late
    if (mins >= 10) return "border-amber-500/40";               // running late
  }
  return "border-gray-700"; // default
}

export function KitchenCard({
  item, storeId,
}: {
  item: KitchenItem;
  storeId: string;
}) {
  const updateMut             = useUpdateItemStatus(storeId);
  const [pending, setPending] = useState(false);
  const { mins, label, textColor } = elapsed(item.sentToKitchenAt);
  const nextStatus            = STATUS_SEQUENCE[item.status];

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
    <div className={cn(
      "bg-gray-900 rounded-xl p-4 border-2 flex flex-col gap-2 transition-colors",
      cardStyle(item.status, mins),
    )}>
      {/* Meta row */}
      <div className="flex justify-between items-start">
        <span className="text-xs text-gray-400">
          {item.tableNumber ? `Mesa ${item.tableNumber}` : item.orderType} · #{item.orderNumber}
        </span>
        {label && (
          <span className={cn("text-xs font-semibold tabular-nums", textColor)}>
            {label}
          </span>
        )}
      </div>

      {/* Product */}
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

      {/* Action button */}
      {nextStatus && (
        <button
          onClick={handleAdvance}
          disabled={pending}
          className={cn(
            "mt-1 w-full rounded-lg py-2.5 text-sm font-medium transition-colors disabled:opacity-50",
            item.status === "Ready"
              ? "bg-green-700/60 hover:bg-green-700/80"
              : "bg-gray-700 hover:bg-gray-600"
          )}
        >
          {pending ? "..." : STATUS_ACTION[item.status]}
        </button>
      )}
    </div>
  );
}
