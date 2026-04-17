import { useState } from "react";
import { useNavigate, useParams } from "react-router-dom";
import { ArrowLeft, Plus, AlertTriangle } from "lucide-react";
import { Button } from "@/components/ui/button";
import { useAuth } from "@/modules/auth/context/AuthContext";
import { OrderItemRow } from "../components/OrderItemRow";
import { AddItemDrawer } from "../components/AddItemDrawer";
import { PaymentDrawer } from "../components/PaymentDrawer";
import { useActiveOrder, useOrder } from "../hooks/useActiveOrder";
import { useAddItem, useCloseOrder, useCancelOrder } from "../hooks/useOrderMutations";
import type { AddOrderItemRequest } from "../types";

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

  const handleAddItem = (req: AddOrderItemRequest) => {
    addItemMut.mutate(
      { orderId: order.id, req },
      { onSuccess: () => setAddDrawerOpen(false) }
    );
  };

  const handleCloseOrder = async () => {
    await closeOrderMut.mutateAsync(order.id);
    setPayDrawerOpen(true);
  };

  const handleCancel = () => {
    if (!window.confirm("Cancelar esta comanda? Esta ação não pode ser desfeita.")) return;
    cancelMut.mutate(order.id, { onSuccess: () => navigate("/restaurante") });
  };

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
            {order.status}
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
          <div className="space-y-1 mb-4 text-sm">
            <div className="flex justify-between">
              <span className="text-muted-foreground">Subtotal</span>
              <span>R$ {order.itemsSubtotal.toFixed(2)}</span>
            </div>
            {order.couvertAmount > 0 && (
              <div className="flex justify-between">
                <span className="text-muted-foreground">
                  Couvert ({order.partySize ?? "?"} pessoas)
                </span>
                <span>R$ {order.couvertAmount.toFixed(2)}</span>
              </div>
            )}
          </div>
          <div className="flex gap-2">
            <Button
              variant="outline"
              size="sm"
              className="text-destructive border-destructive/50"
              onClick={handleCancel}
              disabled={cancelMut.isPending}
            >
              <AlertTriangle className="h-4 w-4" />
            </Button>
            <Button
              className="flex-1 h-12"
              onClick={handleCloseOrder}
              disabled={
                order.items.filter((i) => i.status !== "Cancelled").length === 0 ||
                closeOrderMut.isPending
              }
            >
              Fechar conta
            </Button>
          </div>
        </div>
      )}

      <AddItemDrawer
        open={addDrawerOpen}
        onClose={() => setAddDrawerOpen(false)}
        onAdd={handleAddItem}
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
