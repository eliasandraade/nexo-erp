import { useState } from "react";
import { cn } from "@/lib/utils";
import { Textarea } from "@/components/ui/textarea";
import type { DeliveryOrderDto, DeliveryOrderStatus } from "../types";
import {
  useAcceptDelivery,
  useRejectDelivery,
  useAdvanceDelivery,
  useCancelDelivery,
} from "../hooks/useDeliveryMutations";

// ── Helpers ───────────────────────────────────────────────────────────────────

const CHANNEL_LABEL: Record<string, string> = {
  Portal:    "Portal",
  PhoneCall: "Telefone",
  InPerson:  "Balcão",
  WhatsApp:  "WhatsApp",
  IFood:     "iFood",
  Rappi:     "Rappi",
  Anotaai:   "Anotaai",
  Other:     "Outro",
};

function elapsed(since: string): string {
  const mins = Math.floor((Date.now() - new Date(since).getTime()) / 60_000);
  if (mins < 1) return "<1 min";
  if (mins < 60) return `${mins} min`;
  const h = Math.floor(mins / 60);
  const m = mins % 60;
  return m > 0 ? `${h}h${m}m` : `${h}h`;
}

function elapsedColor(since: string, status: DeliveryOrderStatus): string {
  if (status === "Received") {
    const mins = Math.floor((Date.now() - new Date(since).getTime()) / 60_000);
    if (mins >= 10) return "text-red-400";
    if (mins >= 5) return "text-amber-400";
  }
  return "text-muted-foreground";
}

const TERMINAL: DeliveryOrderStatus[] = [
  "Delivered", "Rejected", "Cancelled",
];

function isTerminal(s: DeliveryOrderStatus) {
  return TERMINAL.includes(s);
}

// ── Card border/bg per status ─────────────────────────────────────────────────

function cardBorder(status: DeliveryOrderStatus): string {
  switch (status) {
    case "Received":       return "border-amber-500/60 bg-amber-950/10";
    case "Accepted":       return "border-blue-500/40";
    case "InPreparation":  return "border-amber-500/40";
    case "ReadyForPickup": return "border-green-500/50 bg-green-950/10";
    case "OutForDelivery": return "border-purple-500/40";
    case "Delivered":      return "border-gray-600/40 opacity-60";
    case "Rejected":       return "border-red-500/30 opacity-60";
    case "Cancelled":      return "border-gray-600/30 opacity-50";
  }
}

// ── DeliveryCard ──────────────────────────────────────────────────────────────

export function DeliveryCard({
  order,
  storeId,
}: {
  order: DeliveryOrderDto;
  storeId: string;
}) {
  const acceptMut  = useAcceptDelivery(storeId);
  const rejectMut  = useRejectDelivery(storeId);
  const advanceMut = useAdvanceDelivery(storeId);
  const cancelMut  = useCancelDelivery(storeId);

  const [pending,        setPending]        = useState(false);
  const [showReject,     setShowReject]     = useState(false);
  const [rejectReason,   setRejectReason]   = useState("");
  const [showCancel,     setShowCancel]     = useState(false);

  const run = async (fn: () => Promise<unknown>) => {
    setPending(true);
    try { await fn(); } finally { setPending(false); }
  };

  const handleAccept = () =>
    run(() => acceptMut.mutateAsync({ id: order.id, req: {} }));

  const handleReject = () =>
    run(() =>
      rejectMut.mutateAsync({
        id: order.id,
        req: { reason: rejectReason.trim() || null },
      }).then(() => { setShowReject(false); setRejectReason(""); })
    );

  const handleDispatch = () =>
    run(() =>
      advanceMut.mutateAsync({ id: order.id, req: { status: "OutForDelivery" } })
    );

  const handleDelivered = () =>
    run(() =>
      advanceMut.mutateAsync({ id: order.id, req: { status: "Delivered" } })
    );

  const handleCancel = () =>
    run(() => cancelMut.mutateAsync(order.id).then(() => setShowCancel(false)));

  // Items summary — up to 3 lines
  const MAX = 3;
  const visible  = order.items.slice(0, MAX);
  const overflow = order.items.length - MAX;

  return (
    <div
      className={cn(
        "bg-gray-900 rounded-xl border-2 flex flex-col gap-2.5 p-3.5 transition-colors",
        cardBorder(order.status)
      )}
    >
      {/* ── Header ── */}
      <div className="flex items-start justify-between gap-2">
        <div className="flex items-center gap-1.5 flex-wrap">
          <span className="text-xs font-bold text-foreground">
            #{order.orderNumber}
          </span>
          <span className="text-[10px] text-muted-foreground">
            {CHANNEL_LABEL[order.channel] ?? order.channel}
          </span>
          <span className="text-[10px] px-1.5 py-0.5 rounded-full bg-muted text-muted-foreground">
            {order.orderType === "Delivery" ? "Entrega" : "Retirada"}
          </span>
        </div>
        <span
          className={cn(
            "text-[10px] font-medium tabular-nums shrink-0",
            elapsedColor(order.receivedAt, order.status)
          )}
        >
          {elapsed(order.receivedAt)}
        </span>
      </div>

      {/* ── Customer ── */}
      <div>
        <p className="text-sm font-semibold leading-tight">{order.customerName}</p>
        <p className="text-xs text-muted-foreground">{order.customerPhone}</p>
      </div>

      {/* ── Items ── */}
      {order.items.length > 0 && (
        <div className="text-xs text-gray-300 space-y-0.5">
          {visible.map((item) => (
            <p key={item.id} className="leading-snug">
              {item.quantity}× {item.productName}
              {item.modifiers.length > 0 && (
                <span className="text-muted-foreground">
                  {" "}({item.modifiers.map((m) => m.label).join(", ")})
                </span>
              )}
            </p>
          ))}
          {overflow > 0 && (
            <p className="text-muted-foreground">+{overflow} item(s)</p>
          )}
        </div>
      )}

      {/* ── Notes ── */}
      {order.notes && (
        <p className="text-xs text-amber-300 italic">"{order.notes}"</p>
      )}

      {/* ── Total ── */}
      <p className="text-sm font-bold tabular-nums">
        R$ {order.total.toFixed(2)}
        {order.deliveryFee > 0 && (
          <span className="text-xs font-normal text-muted-foreground ml-1">
            (+ R$ {order.deliveryFee.toFixed(2)} entrega)
          </span>
        )}
      </p>

      {/* ── Rejection reason (terminal) ── */}
      {order.status === "Rejected" && order.rejectionReason && (
        <p className="text-xs text-red-400">Motivo: {order.rejectionReason}</p>
      )}

      {/* ── Actions ── */}
      {!isTerminal(order.status) && (
        <div className="flex flex-col gap-2 pt-1 border-t border-white/5">

          {/* Received → Accept / Reject */}
          {order.status === "Received" && !showReject && (
            <div className="flex gap-2">
              <button
                onClick={handleAccept}
                disabled={pending}
                className="flex-1 rounded-lg py-2.5 text-sm font-medium bg-green-700/70 hover:bg-green-700 disabled:opacity-50 transition-colors"
              >
                {pending ? "..." : "Aceitar"}
              </button>
              <button
                onClick={() => setShowReject(true)}
                disabled={pending}
                className="flex-1 rounded-lg py-2.5 text-sm font-medium bg-red-900/50 hover:bg-red-900/70 disabled:opacity-50 transition-colors"
              >
                Rejeitar
              </button>
            </div>
          )}

          {/* Reject form */}
          {order.status === "Received" && showReject && (
            <div className="flex flex-col gap-2">
              <Textarea
                placeholder="Motivo da rejeição (opcional)"
                value={rejectReason}
                onChange={(e) => setRejectReason(e.target.value)}
                rows={2}
                className="text-sm resize-none"
                autoFocus
              />
              <div className="flex gap-2">
                <button
                  onClick={() => { setShowReject(false); setRejectReason(""); }}
                  className="flex-1 rounded-lg py-2 text-sm text-muted-foreground hover:text-foreground transition-colors"
                >
                  Cancelar
                </button>
                <button
                  onClick={handleReject}
                  disabled={pending}
                  className="flex-1 rounded-lg py-2 text-sm font-medium bg-red-900/70 hover:bg-red-900 disabled:opacity-50 transition-colors"
                >
                  {pending ? "..." : "Confirmar"}
                </button>
              </div>
            </div>
          )}

          {/* Accepted / InPreparation → waiting kitchen */}
          {(order.status === "Accepted" || order.status === "InPreparation") && (
            <p className="text-xs text-blue-400 text-center py-1">
              ⏳ Aguardando cozinha...
            </p>
          )}

          {/* ReadyForPickup + Delivery → dispatch */}
          {order.status === "ReadyForPickup" && order.orderType === "Delivery" && (
            <button
              onClick={handleDispatch}
              disabled={pending}
              className="w-full rounded-lg py-2.5 text-sm font-medium bg-purple-700/60 hover:bg-purple-700/80 disabled:opacity-50 transition-colors"
            >
              {pending ? "..." : "🚲 Despachar"}
            </button>
          )}

          {/* ReadyForPickup + Takeaway → confirm pickup */}
          {order.status === "ReadyForPickup" && order.orderType === "Takeaway" && (
            <button
              onClick={handleDelivered}
              disabled={pending}
              className="w-full rounded-lg py-2.5 text-sm font-medium bg-green-700/70 hover:bg-green-700 disabled:opacity-50 transition-colors"
            >
              {pending ? "..." : "✓ Confirmar retirada"}
            </button>
          )}

          {/* OutForDelivery → delivered */}
          {order.status === "OutForDelivery" && (
            <button
              onClick={handleDelivered}
              disabled={pending}
              className="w-full rounded-lg py-2.5 text-sm font-medium bg-green-700/70 hover:bg-green-700 disabled:opacity-50 transition-colors"
            >
              {pending ? "..." : "✓ Entregue"}
            </button>
          )}

          {/* Cancel — non-Received statuses (Received has its own reject) */}
          {order.status !== "Received" && !showCancel && (
            <button
              onClick={() => setShowCancel(true)}
              className="w-full rounded-lg py-1.5 text-xs text-muted-foreground hover:text-red-400 transition-colors"
            >
              Cancelar pedido
            </button>
          )}
          {order.status !== "Received" && showCancel && (
            <div className="flex gap-2">
              <button
                onClick={() => setShowCancel(false)}
                className="flex-1 rounded-lg py-2 text-xs text-muted-foreground hover:text-foreground transition-colors"
              >
                Não
              </button>
              <button
                onClick={handleCancel}
                disabled={pending}
                className="flex-1 rounded-lg py-2 text-xs font-medium text-red-400 bg-red-950/40 hover:bg-red-950/70 disabled:opacity-50 transition-colors"
              >
                {pending ? "..." : "Sim, cancelar"}
              </button>
            </div>
          )}
        </div>
      )}
    </div>
  );
}
