import { cn } from "@/lib/utils";
import type { OrderItemDto } from "../types";

const statusColor: Record<OrderItemDto["status"], string> = {
  Pending:   "bg-muted text-muted-foreground",
  Preparing: "bg-amber-100 text-amber-700 dark:bg-amber-900/30 dark:text-amber-400",
  Ready:     "bg-green-100 text-green-700 dark:bg-green-900/30 dark:text-green-400",
  Delivered: "bg-blue-100 text-blue-700 dark:bg-blue-900/30 dark:text-blue-400",
  Cancelled: "bg-red-100 text-red-700 dark:bg-red-900/30 dark:text-red-400 line-through opacity-50",
};

const statusLabel: Record<OrderItemDto["status"], string> = {
  Pending:   "Pendente",
  Preparing: "Preparando",
  Ready:     "Pronto",
  Delivered: "Entregue",
  Cancelled: "Cancelado",
};

export function OrderItemRow({ item }: { item: OrderItemDto }) {
  return (
    <div className="flex items-start gap-3 py-3 border-b border-border last:border-0">
      <div className="flex-1 min-w-0">
        <div className="flex items-center gap-2">
          <span className="font-medium text-sm">{item.quantity}×</span>
          <span className="font-medium text-sm truncate">{item.productName}</span>
        </div>
        {item.modifiers.length > 0 && (
          <p className="text-xs text-muted-foreground mt-0.5 pl-6">
            {item.modifiers.map((m) => m.labelSnapshot).join(", ")}
          </p>
        )}
        {item.notes && (
          <p className="text-xs text-muted-foreground italic mt-0.5 pl-6">
            "{item.notes}"
          </p>
        )}
      </div>
      <div className="text-right shrink-0">
        <p className="text-sm font-semibold">R$ {item.total.toFixed(2)}</p>
        <span className={cn("text-[10px] font-medium px-1.5 py-0.5 rounded mt-1 inline-block", statusColor[item.status])}>
          {statusLabel[item.status]}
        </span>
      </div>
    </div>
  );
}
