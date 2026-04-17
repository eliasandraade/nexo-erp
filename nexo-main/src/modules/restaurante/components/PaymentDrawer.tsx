import { useState } from "react";
import { useNavigate } from "react-router-dom";
import {
  Sheet, SheetContent, SheetHeader, SheetTitle,
} from "@/components/ui/sheet";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { cn } from "@/lib/utils";
import { usePayOrder } from "../hooks/useOrderMutations";
import { useFoodSettings } from "../hooks/useFoodSettings";
import type { OrderDto, PaymentInputDto } from "../types";

type PayMethod = "cash" | "pix" | "card";

const METHOD_LABELS: Record<PayMethod, string> = {
  cash: "Dinheiro",
  pix: "PIX",
  card: "Cartão",
};

function toBackendPayment(method: PayMethod, amount: number): PaymentInputDto {
  const map: Record<PayMethod, { method: string; type: string }> = {
    cash: { method: "Cash",  type: "Cash" },
    pix:  { method: "Pix",   type: "Cash" },
    card: { method: "Debit", type: "Cash" },
  };
  return { ...map[method], amount };
}

interface PaymentDrawerProps {
  open: boolean;
  order: OrderDto;
  storeId: string;
  onClose: () => void;
}

export function PaymentDrawer({ open, order, storeId, onClose }: PaymentDrawerProps) {
  const navigate    = useNavigate();
  const payMut      = usePayOrder(storeId);
  const { data: settings } = useFoodSettings(storeId);

  // If service fee not yet calculated (order just closed), use settings to estimate
  const serviceFeePercent = settings?.serviceFeeEnabled ? (settings.serviceFeePercent ?? 0) : 0;
  const estimatedServiceFee = order.serviceFeeAmount > 0
    ? order.serviceFeeAmount
    : Math.round(order.itemsSubtotal * (serviceFeePercent / 100) * 100) / 100;

  const displayTotal = order.itemsSubtotal + order.couvertAmount + estimatedServiceFee;

  const [method, setMethod]     = useState<PayMethod>("cash");
  const [amount, setAmount]     = useState(displayTotal.toFixed(2));
  const [partySize, setPartySize] = useState(order.partySize?.toString() ?? "");

  const paid   = parseFloat(amount) || 0;
  const change = Math.max(0, paid - displayTotal);

  const splitSuggestion =
    order.partySize && order.partySize > 1
      ? (displayTotal / order.partySize).toFixed(2)
      : null;

  const handlePay = () => {
    payMut.mutate(
      {
        orderId: order.id,
        req: {
          payments: [toBackendPayment(method, paid)],
          partySize: partySize ? parseInt(partySize) : undefined,
        },
      },
      {
        onSuccess: () => {
          navigate("/restaurante");
        },
      }
    );
  };

  return (
    <Sheet open={open} onOpenChange={(v) => !v && onClose()}>
      <SheetContent side="bottom" className="rounded-t-2xl pb-8 max-h-[92vh] overflow-y-auto">
        <SheetHeader className="mb-4">
          <SheetTitle>Fechar conta — #{order.orderNumber}</SheetTitle>
        </SheetHeader>

        {/* Breakdown */}
        <div className="space-y-2 mb-5 text-sm">
          <Row label="Subtotal dos itens" value={order.itemsSubtotal} />
          {order.couvertAmount > 0 && (
            <Row
              label={`Couvert (${order.partySize ?? "?"} pessoas)`}
              value={order.couvertAmount}
            />
          )}
          {serviceFeePercent > 0 && (
            <Row
              label={`Taxa de serviço ${serviceFeePercent}%`}
              value={estimatedServiceFee}
            />
          )}
          <div className="flex justify-between font-semibold border-t border-border pt-2">
            <span>Total</span>
            <span className="text-base">R$ {displayTotal.toFixed(2)}</span>
          </div>
          {splitSuggestion && (
            <p className="text-xs text-muted-foreground">
              Sugestão: R$ {splitSuggestion} por pessoa
            </p>
          )}
        </div>

        {/* Manual party size (for manual couvert) */}
        {settings?.couvertEnabled && !settings.couvertAutomatic && !order.partySize && (
          <div className="mb-4">
            <label className="text-sm text-muted-foreground mb-1 block">
              Número de pessoas (para couvert)
            </label>
            <Input
              type="number" min={1} value={partySize}
              onChange={(e) => setPartySize(e.target.value)}
            />
          </div>
        )}

        {/* Payment method */}
        <div className="flex gap-2 mb-4">
          {(Object.keys(METHOD_LABELS) as PayMethod[]).map((m) => (
            <button
              key={m}
              onClick={() => setMethod(m)}
              className={cn(
                "flex-1 py-2 rounded-lg text-sm font-medium border transition-colors",
                method === m
                  ? "border-primary bg-primary/10 text-primary"
                  : "border-border text-muted-foreground"
              )}
            >
              {METHOD_LABELS[m]}
            </button>
          ))}
        </div>

        {/* Amount */}
        <div className="mb-2">
          <label className="text-sm text-muted-foreground mb-1 block">Valor recebido</label>
          <Input
            type="number" min={displayTotal}
            value={amount}
            onChange={(e) => setAmount(e.target.value)}
            className="text-lg font-semibold"
          />
        </div>

        {method === "cash" && change > 0 && (
          <p className="text-sm text-green-600 font-medium mb-4">
            Troco: R$ {change.toFixed(2)}
          </p>
        )}

        <Button
          className="w-full h-12 text-base mt-2"
          onClick={handlePay}
          disabled={paid < displayTotal || payMut.isPending}
        >
          {payMut.isPending ? "Processando..." : "Confirmar pagamento"}
        </Button>
      </SheetContent>
    </Sheet>
  );
}

function Row({ label, value }: { label: string; value: number }) {
  return (
    <div className="flex justify-between text-muted-foreground">
      <span>{label}</span>
      <span className="text-foreground">R$ {value.toFixed(2)}</span>
    </div>
  );
}
