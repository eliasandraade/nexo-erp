import { cn } from "@/lib/utils";
import type { DeliveryOrderDto, DeliveryOrderStatus } from "../types";
import { DeliveryCard } from "./DeliveryCard";

// ── Column definitions ────────────────────────────────────────────────────────

interface Column {
  status: DeliveryOrderStatus;
  label: string;
  dotColor: string;
}

const ACTIVE_COLUMNS: Column[] = [
  { status: "Received",       label: "Novos",      dotColor: "bg-amber-500" },
  { status: "Accepted",       label: "Aceitos",    dotColor: "bg-blue-500"  },
  { status: "InPreparation",  label: "Preparando", dotColor: "bg-amber-400" },
  { status: "ReadyForPickup", label: "Prontos",    dotColor: "bg-green-500" },
  { status: "OutForDelivery", label: "A caminho",  dotColor: "bg-purple-500"},
];

const TERMINAL_STATUSES: DeliveryOrderStatus[] = [
  "Delivered", "Rejected", "Cancelled",
];

// ── KanbanColumn ──────────────────────────────────────────────────────────────

function KanbanColumn({
  col,
  orders,
  storeId,
}: {
  col: Column;
  orders: DeliveryOrderDto[];
  storeId: string;
}) {
  return (
    <div className="flex flex-col min-w-[272px] max-w-[272px] h-full">
      {/* Column header */}
      <div className="flex items-center gap-2 mb-3 shrink-0">
        <span className={cn("w-2 h-2 rounded-full shrink-0", col.dotColor)} />
        <span className="text-sm font-semibold text-foreground">{col.label}</span>
        <span className="ml-auto text-xs font-medium tabular-nums text-muted-foreground bg-muted rounded-full px-2 py-0.5">
          {orders.length}
        </span>
      </div>

      {/* Cards */}
      <div className="flex-1 overflow-y-auto flex flex-col gap-3 pb-4 pr-0.5">
        {orders.length === 0 ? (
          <div className="rounded-xl border-2 border-dashed border-white/10 flex items-center justify-center h-20">
            <p className="text-xs text-muted-foreground">Nenhum pedido</p>
          </div>
        ) : (
          orders.map((order) => (
            <DeliveryCard key={order.id} order={order} storeId={storeId} />
          ))
        )}
      </div>
    </div>
  );
}

// ── DeliveryKanban ────────────────────────────────────────────────────────────

interface DeliveryKanbanProps {
  orders: DeliveryOrderDto[];
  storeId: string;
  showHistory: boolean;
}

export function DeliveryKanban({ orders, storeId, showHistory }: DeliveryKanbanProps) {
  const byStatus = (status: DeliveryOrderStatus) =>
    orders
      .filter((o) => o.status === status)
      .sort(
        (a, b) => new Date(a.receivedAt).getTime() - new Date(b.receivedAt).getTime()
      );

  const terminalOrders = orders.filter((o) =>
    TERMINAL_STATUSES.includes(o.status)
  );

  return (
    <div className="flex h-full gap-4 overflow-x-auto p-4 pb-0">
      {/* Active columns */}
      {ACTIVE_COLUMNS.map((col) => (
        <KanbanColumn
          key={col.status}
          col={col}
          orders={byStatus(col.status)}
          storeId={storeId}
        />
      ))}

      {/* History column — toggle via prop */}
      {showHistory && terminalOrders.length > 0 && (
        <div className="flex flex-col min-w-[272px] max-w-[272px] h-full">
          <div className="flex items-center gap-2 mb-3 shrink-0">
            <span className="w-2 h-2 rounded-full shrink-0 bg-gray-500" />
            <span className="text-sm font-semibold text-muted-foreground">
              Histórico
            </span>
            <span className="ml-auto text-xs font-medium tabular-nums text-muted-foreground bg-muted rounded-full px-2 py-0.5">
              {terminalOrders.length}
            </span>
          </div>
          <div className="flex-1 overflow-y-auto flex flex-col gap-3 pb-4">
            {terminalOrders
              .sort(
                (a, b) =>
                  new Date(b.receivedAt).getTime() -
                  new Date(a.receivedAt).getTime()
              )
              .map((order) => (
                <DeliveryCard key={order.id} order={order} storeId={storeId} />
              ))}
          </div>
        </div>
      )}
    </div>
  );
}
