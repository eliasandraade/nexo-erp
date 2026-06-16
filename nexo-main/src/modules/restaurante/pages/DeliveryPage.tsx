import { useState } from "react";
import { Plus, RefreshCw, History } from "lucide-react";
import { cn } from "@/lib/utils";
import { useAuth } from "@/modules/auth/context/AuthContext";
import { useDeliveryOrders } from "../hooks/useDeliveryOrders";
import { DeliveryKanban } from "../components/DeliveryKanban";
import { ManualOrderSheet } from "../components/ManualOrderSheet";

export default function DeliveryPage() {
  const { session } = useAuth();
  const storeId     = session?.storeId ?? "";

  const { data: orders = [], isLoading, isFetching, refetch } = useDeliveryOrders(storeId);

  const [sheetOpen,    setSheetOpen]    = useState(false);
  const [showHistory,  setShowHistory]  = useState(false);

  const activeCount = orders.filter(
    (o) => !["Delivered", "Rejected", "Cancelled"].includes(o.status)
  ).length;

  return (
    <div className="flex flex-col h-screen overflow-hidden bg-background">
      {/* ── Header ── */}
      <div className="px-4 pt-5 pb-3 flex items-center justify-between border-b border-border shrink-0">
        <div>
          <h1 className="text-lg font-semibold">Entregas</h1>
          <p className="text-xs text-muted-foreground">
            {isLoading
              ? "Carregando pedidos..."
              : activeCount === 0
                ? "Nenhum pedido em andamento"
                : `${activeCount} pedido(s) em andamento`}
          </p>
        </div>

        <div className="flex items-center gap-2">
          {/* History toggle */}
          <button
            onClick={() => setShowHistory((v) => !v)}
            className={cn(
              "p-2 rounded-lg transition-colors",
              showHistory
                ? "bg-muted text-foreground"
                : "text-muted-foreground hover:text-foreground"
            )}
            title="Histórico"
          >
            <History className="h-4 w-4" />
          </button>

          {/* Manual refresh */}
          <button
            onClick={() => refetch()}
            disabled={isFetching}
            className="p-2 rounded-lg text-muted-foreground hover:text-foreground transition-colors disabled:opacity-40"
            title="Atualizar"
          >
            <RefreshCw className={cn("h-4 w-4", isFetching && "animate-spin")} />
          </button>

          {/* New manual order */}
          <button
            onClick={() => setSheetOpen(true)}
            className="flex items-center gap-1.5 rounded-lg bg-primary px-3 py-2 text-sm font-medium text-primary-foreground"
          >
            <Plus className="h-4 w-4" />
            Novo pedido
          </button>
        </div>
      </div>

      {/* ── Polling indicator ── */}
      {isFetching && !isLoading && (
        <div className="h-0.5 bg-primary/30 relative overflow-hidden shrink-0">
          <div className="absolute inset-0 bg-primary animate-pulse" />
        </div>
      )}

      {/* ── Kanban ── */}
      <div className="flex-1 overflow-hidden">
        {isLoading ? (
          <div className="flex gap-4 p-4 h-full">
            {[...Array(5)].map((_, i) => (
              <div
                key={i}
                className="min-w-[272px] flex flex-col gap-3"
              >
                <div className="h-5 w-24 rounded bg-muted animate-pulse" />
                {[...Array(2)].map((_, j) => (
                  <div key={j} className="h-40 rounded-xl bg-muted animate-pulse" />
                ))}
              </div>
            ))}
          </div>
        ) : (
          <DeliveryKanban
            orders={orders}
            storeId={storeId}
            showHistory={showHistory}
          />
        )}
      </div>

      {/* ── Manual order sheet ── */}
      <ManualOrderSheet
        open={sheetOpen}
        onClose={() => setSheetOpen(false)}
        storeId={storeId}
      />
    </div>
  );
}
