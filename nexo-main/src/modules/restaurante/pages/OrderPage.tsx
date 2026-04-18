import { useState } from "react";
import { useNavigate, useParams } from "react-router-dom";
import { ArrowLeft, Plus } from "lucide-react";
import { Button } from "@/components/ui/button";
import { useAuth } from "@/modules/auth/context/AuthContext";
import { OrderItemRow } from "../components/OrderItemRow";
import { AddItemDrawer } from "../components/AddItemDrawer";
import { PaymentDrawer } from "../components/PaymentDrawer";
import { useActiveOrder, useOrder } from "../hooks/useActiveOrder";
import { useAddItem, useCloseOrder, useCancelOrder } from "../hooks/useOrderMutations";
import type { AddOrderItemRequest, OrderStatus } from "../types";

// ── Status label map ──────────────────────────────────────────────────────────
const ORDER_STATUS_LABEL: Record<OrderStatus, string> = {
  Open:          "Aberta",
  InPreparation: "Em preparo",
  Ready:         "Pronta",
  Closed:        "Fechada",
  Paid:          "Paga",
  Cancelled:     "Cancelada",
};

export default function OrderPage() {
  const { session }  = useAuth();
  const storeId      = session?.storeId ?? "";
  const { tableId, orderId } = useParams<{ tableId?: string; orderId?: string }>();
  const navigate     = useNavigate();

  // Support both /mesa/:tableId (find active order) and /comanda/:orderId (direct)
  const { data: orderByTable }  = useActiveOrder(storeId, tableId ?? "");
  const { data: orderDirect }   = useOrder(storeId, orderId ?? "");
  const order = orderId ? orderDirect : orderByTable;

  const addItemMut    = useAddItem(storeId);
  const closeOrderMut = useCloseOrder(storeId);
  const cancelMut     = useCancelOrder(storeId);

  const [addDrawerOpen, setAddDrawerOpen] = useState(false);
  const [payDrawerOpen, setPayDrawerOpen] = useState(false);

  // "cancel" | "close" | null — which destructive action is awaiting confirmation
  const [confirmState, setConfirmState] = useState<"cancel" | "close" | null>(null);

  if (!order) {
    return (
      <div className="flex flex-col items-center justify-center h-screen gap-3">
        <p className="text-muted-foreground text-sm">Nenhuma comanda aberta.</p>
        <Button variant="ghost" size="sm" onClick={() => navigate("/restaurante")}>
          ← Voltar ao salão
        </Button>
      </div>
    );
  }

  const isOpen = ["Open", "InPreparation", "Ready"].includes(order.status);
  const hasActiveItems = order.items.filter((i) => i.status !== "Cancelled").length > 0;

  // Fechar conta: confirma antes de chamar a API
  const handleCloseOrder = async () => {
    setConfirmState(null);
    await closeOrderMut.mutateAsync(order.id);
    setPayDrawerOpen(true);
  };

  const handleCancel = () => {
    setConfirmState(null);
    cancelMut.mutate(order.id, { onSuccess: () => navigate("/restaurante") });
  };

  // ── Total display (inclui taxa de serviço quando presente) ────────────────
  const showServiceFee = order.serviceFeeAmount > 0;
  const displayTotal   = order.total > 0
    ? order.total
    : order.itemsSubtotal + order.couvertAmount;

  return (
    <div className="flex flex-col h-screen overflow-hidden">
      {/* Header */}
      <div className="px-4 pt-5 pb-3 border-b border-border flex items-center gap-3">
        <button onClick={() => navigate("/restaurante")} className="p-1">
          <ArrowLeft className="h-5 w-5" />
        </button>
        <div className="flex-1">
          <h1 className="font-semibold">
            {order.tableNumber ? `Mesa ${order.tableNumber}` : order.orderType}
            <span className="text-muted-foreground font-normal ml-2 text-sm">
              #{order.orderNumber}
            </span>
          </h1>
          <p className="text-xs text-muted-foreground">
            {order.partySize ? `${order.partySize} pessoa(s) · ` : ""}
            {ORDER_STATUS_LABEL[order.status] ?? order.status}
          </p>
        </div>
        {isOpen && (
          <button
            onClick={() => setAddDrawerOpen(true)}
            className="rounded-full bg-primary p-2 text-primary-foreground"
          >
            <Plus className="h-5 w-5" />
          </button>
        )}
      </div>

      {/* Items */}
      <div className="flex-1 overflow-y-auto px-4 py-2">
        {order.items.length === 0 ? (
          <p className="text-center text-muted-foreground text-sm mt-8">
            Nenhum item adicionado.
          </p>
        ) : (
          order.items.map((item) => <OrderItemRow key={item.id} item={item} />)
        )}
      </div>

      {/* Totals + action bar */}
      {isOpen && (
        <div className="border-t border-border px-4 pt-3 pb-5">
          {/* Totals */}
          <div className="space-y-1 mb-4 text-sm">
            <div className="flex justify-between text-muted-foreground">
              <span>Subtotal</span>
              <span className="text-foreground">R$ {order.itemsSubtotal.toFixed(2)}</span>
            </div>
            {order.couvertAmount > 0 && (
              <div className="flex justify-between text-muted-foreground">
                <span>Couvert ({order.partySize ?? "?"} pessoas)</span>
                <span className="text-foreground">R$ {order.couvertAmount.toFixed(2)}</span>
              </div>
            )}
            {showServiceFee && (
              <div className="flex justify-between text-muted-foreground">
                <span>Taxa de serviço</span>
                <span className="text-foreground">R$ {order.serviceFeeAmount.toFixed(2)}</span>
              </div>
            )}
            {(order.couvertAmount > 0 || showServiceFee) && (
              <div className="flex justify-between font-semibold border-t border-border pt-1.5 mt-1">
                <span>Total</span>
                <span>R$ {displayTotal.toFixed(2)}</span>
              </div>
            )}
          </div>

          {/* Action bar — normal / confirm cancel / confirm close */}
          {confirmState === "cancel" ? (
            <div className="space-y-2">
              <p className="text-sm text-destructive font-medium text-center">
                Cancelar esta comanda? Esta ação não pode ser desfeita.
              </p>
              <div className="flex gap-2">
                <Button
                  variant="outline"
                  className="flex-1"
                  onClick={() => setConfirmState(null)}
                >
                  Voltar
                </Button>
                <Button
                  variant="destructive"
                  className="flex-1 h-12"
                  onClick={handleCancel}
                  disabled={cancelMut.isPending}
                >
                  {cancelMut.isPending ? "Cancelando..." : "Sim, cancelar"}
                </Button>
              </div>
            </div>
          ) : confirmState === "close" ? (
            <div className="space-y-2">
              <p className="text-sm text-foreground font-medium text-center">
                Fechar a conta e ir para o pagamento?
              </p>
              <div className="flex gap-2">
                <Button
                  variant="outline"
                  className="flex-1"
                  onClick={() => setConfirmState(null)}
                >
                  Voltar
                </Button>
                <Button
                  className="flex-1 h-12"
                  onClick={handleCloseOrder}
                  disabled={closeOrderMut.isPending}
                >
                  {closeOrderMut.isPending ? "Fechando..." : "Sim, fechar conta"}
                </Button>
              </div>
            </div>
          ) : (
            <div className="flex gap-2">
              <Button
                variant="outline"
                size="sm"
                className="text-destructive border-destructive/40 hover:bg-destructive/5 px-3 text-xs"
                onClick={() => setConfirmState("cancel")}
                disabled={cancelMut.isPending}
              >
                Cancelar comanda
              </Button>
              <Button
                className="flex-1 h-12"
                onClick={() => setConfirmState("close")}
                disabled={!hasActiveItems || closeOrderMut.isPending}
              >
                Fechar conta
              </Button>
            </div>
          )}
        </div>
      )}

      <AddItemDrawer
        open={addDrawerOpen}
        onClose={() => setAddDrawerOpen(false)}
        onAdd={(req: AddOrderItemRequest) =>
          addItemMut.mutate(
            { orderId: order.id, req },
            { onSuccess: () => setAddDrawerOpen(false) }
          )
        }
        isLoading={addItemMut.isPending}
      />

      <PaymentDrawer
        open={payDrawerOpen}
        order={order}
        onClose={() => setPayDrawerOpen(false)}
        storeId={storeId}
      />
    </div>
  );
}
