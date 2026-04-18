import { useState, useMemo, useEffect } from "react";
import { useNavigate } from "react-router-dom";
import {
  Sheet, SheetContent, SheetHeader, SheetTitle,
} from "@/components/ui/sheet";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { cn } from "@/lib/utils";
import { Plus, X, Users } from "lucide-react";
import { usePayOrder } from "../hooks/useOrderMutations";
import { useFoodSettings } from "../hooks/useFoodSettings";
import type { OrderDto, PaymentInputDto } from "../types";

// ─── Types ────────────────────────────────────────────────────────────────────

type PayMethod = "cash" | "pix" | "card";

const METHODS: { key: PayMethod; label: string }[] = [
  { key: "cash", label: "Dinheiro" },
  { key: "pix",  label: "PIX" },
  { key: "card", label: "Cartão" },
];

function toBackendPayment(method: PayMethod, amount: number): PaymentInputDto {
  const map: Record<PayMethod, { method: string; type: string }> = {
    cash: { method: "Cash",  type: "Cash" },
    pix:  { method: "Pix",   type: "Cash" },
    card: { method: "Debit", type: "Cash" },
  };
  return { ...map[method], amount };
}

interface PaymentEntry {
  id: string;
  method: PayMethod;
  amount: string;
}

function makeEntry(amount = ""): PaymentEntry {
  return { id: crypto.randomUUID(), method: "cash", amount };
}

// ─── Main component ───────────────────────────────────────────────────────────

interface PaymentDrawerProps {
  open: boolean;
  order: OrderDto;
  storeId: string;
  onClose: () => void;
}

export function PaymentDrawer({ open, order, storeId, onClose }: PaymentDrawerProps) {
  const navigate = useNavigate();
  const payMut   = usePayOrder(storeId);
  const { data: settings } = useFoodSettings(storeId);

  // ── Total calculation ──────────────────────────────────────────────────────
  // Prefer values already stored on the closed order; fall back to estimating
  // from settings while the drawer is open before close() fires.
  const serviceFeePercent = settings?.serviceFeeEnabled ? (settings.serviceFeePercent ?? 0) : 0;
  const serviceFee = order.serviceFeeAmount > 0
    ? order.serviceFeeAmount
    : Math.round(order.itemsSubtotal * (serviceFeePercent / 100) * 100) / 100;

  const total = order.itemsSubtotal + order.couvertAmount + serviceFee;

  // ── State ──────────────────────────────────────────────────────────────────
  const [entries,   setEntries]   = useState<PaymentEntry[]>(() => [makeEntry(total.toFixed(2))]);
  const [splitN,    setSplitN]    = useState(order.partySize?.toString() ?? "");
  const [partySize, setPartySize] = useState(order.partySize?.toString() ?? "");

  // Reset every time the drawer opens so stale data never carries over
  useEffect(() => {
    if (!open) return;
    setEntries([makeEntry(total.toFixed(2))]);
    setSplitN(order.partySize?.toString() ?? "");
    setPartySize(order.partySize?.toString() ?? "");
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open]);

  // ── Derived values ─────────────────────────────────────────────────────────
  const totalPaid = useMemo(
    () => entries.reduce((sum, e) => {
      const v = parseFloat(e.amount);
      return sum + (isNaN(v) ? 0 : v);
    }, 0),
    [entries]
  );

  // Round to 2 decimal places to avoid floating-point noise (e.g. 0.009999...)
  const remaining = Math.round((total - totalPaid) * 100) / 100;
  const isExact   = Math.abs(remaining) < 0.01;
  const isOver    = remaining < -0.005;
  const hasCash   = entries.some(e => e.method === "cash");
  const troco     = isOver && hasCash
    ? Math.round((totalPaid - total) * 100) / 100
    : 0;

  const canConfirm =
    isExact &&
    entries.every(e => {
      const v = parseFloat(e.amount);
      return !isNaN(v) && v > 0;
    });

  // ── Entry mutations ────────────────────────────────────────────────────────

  // When adding a new entry, pre-fill with the outstanding balance so the
  // operator doesn't have to type it manually.
  const handleAddEntry = () => {
    const rem = Math.max(0, Math.round((total - totalPaid) * 100) / 100);
    setEntries(es => [...es, makeEntry(rem > 0 ? rem.toFixed(2) : "")]);
  };

  const handleRemove = (id: string) =>
    setEntries(es => es.length > 1 ? es.filter(e => e.id !== id) : es);

  const handleMethod = (id: string, method: PayMethod) =>
    setEntries(es => es.map(e => e.id === id ? { ...e, method } : e));

  const handleAmount = (id: string, val: string) =>
    setEntries(es => es.map(e => e.id === id ? { ...e, amount: val } : e));

  // ── Split by N people ──────────────────────────────────────────────────────
  // Split total into N equal parts. Last entry absorbs rounding remainder.
  const handleSplit = () => {
    const n = parseInt(splitN);
    if (!n || n < 1 || n > 50) return;
    const base = Math.floor((total / n) * 100) / 100;
    const last = Math.round((total - base * (n - 1)) * 100) / 100;
    setEntries(
      Array.from({ length: n }, (_, i) =>
        makeEntry(i === n - 1 ? last.toFixed(2) : base.toFixed(2))
      )
    );
  };

  // ── Submit ─────────────────────────────────────────────────────────────────
  const handlePay = () => {
    const payments = entries.map(e =>
      toBackendPayment(e.method, parseFloat(e.amount))
    );
    payMut.mutate(
      {
        orderId: order.id,
        req: {
          payments,
          partySize: partySize ? parseInt(partySize) : undefined,
        },
      },
      { onSuccess: () => navigate("/restaurante") }
    );
  };

  // ─────────────────────────────────────────────────────────────────────────

  return (
    <Sheet open={open} onOpenChange={v => !v && onClose()}>
      <SheetContent side="bottom" className="rounded-t-2xl pb-safe-bottom max-h-[94vh] overflow-y-auto pb-8">
        <SheetHeader className="mb-5">
          <SheetTitle>Fechar conta — #{order.orderNumber}</SheetTitle>
        </SheetHeader>

        {/* ── Order breakdown ──────────────────────────────────────────── */}
        <div className="space-y-1.5 mb-5 text-sm">
          <BreakdownRow label="Subtotal dos itens" value={order.itemsSubtotal} />
          {order.couvertAmount > 0 && (
            <BreakdownRow
              label={`Couvert (${order.partySize ?? "?"} pessoas)`}
              value={order.couvertAmount}
            />
          )}
          {serviceFeePercent > 0 && (
            <BreakdownRow
              label={`Taxa de serviço ${serviceFeePercent}%`}
              value={serviceFee}
            />
          )}
          <div className="flex justify-between font-semibold border-t border-border pt-2 mt-1">
            <span>Total</span>
            <span className="text-base">R$ {total.toFixed(2)}</span>
          </div>
        </div>

        {/* ── Manual couvert party size ─────────────────────────────────── */}
        {settings?.couvertEnabled && !settings.couvertAutomatic && !order.partySize && (
          <div className="mb-4">
            <label className="text-sm text-muted-foreground mb-1 block">
              Número de pessoas (para couvert)
            </label>
            <Input
              type="number" min={1} value={partySize}
              onChange={e => setPartySize(e.target.value)}
            />
          </div>
        )}

        {/* ── Split by N ───────────────────────────────────────────────── */}
        <div className="flex items-center gap-2 mb-5 px-3 py-2.5 bg-muted/40 rounded-xl border border-border">
          <Users className="h-4 w-4 text-muted-foreground shrink-0" />
          <span className="text-sm text-muted-foreground shrink-0">Dividir por</span>
          <Input
            type="number"
            min={1}
            max={50}
            placeholder="N"
            value={splitN}
            onChange={e => setSplitN(e.target.value)}
            onKeyDown={e => e.key === "Enter" && handleSplit()}
            className="w-16 h-8 text-center px-1 font-medium"
          />
          <span className="text-sm text-muted-foreground shrink-0">pessoas</span>
          <Button
            variant="outline"
            size="sm"
            onClick={handleSplit}
            disabled={!splitN || parseInt(splitN) < 1}
            className="ml-auto h-8 px-3 shrink-0"
          >
            Dividir
          </Button>
        </div>

        {/* ── Payment entries ───────────────────────────────────────────── */}
        <div className="space-y-2.5 mb-3">
          {entries.map((entry, idx) => (
            <PaymentEntryRow
              key={entry.id}
              entry={entry}
              label={entries.length > 1 ? `Pagamento ${idx + 1}` : undefined}
              canRemove={entries.length > 1}
              onMethodChange={method => handleMethod(entry.id, method)}
              onAmountChange={val    => handleAmount(entry.id, val)}
              onRemove={() => handleRemove(entry.id)}
            />
          ))}
        </div>

        {/* Add payment */}
        <button
          onClick={handleAddEntry}
          className="flex items-center gap-1.5 text-sm text-primary hover:text-primary/80 transition-colors mb-5 py-1"
        >
          <Plus className="h-4 w-4" />
          Adicionar forma de pagamento
        </button>

        {/* ── Running balance ───────────────────────────────────────────── */}
        <div className={cn(
          "rounded-xl px-4 py-3 mb-4 text-sm border transition-colors",
          isExact
            ? "bg-green-500/10 border-green-500/25"
            : "bg-amber-500/8 border-amber-500/25"
        )}>
          <div className="flex justify-between mb-1">
            <span className="text-muted-foreground">Total recebido</span>
            <span className="font-medium tabular-nums">R$ {totalPaid.toFixed(2)}</span>
          </div>

          {/* Remaining or overpaid */}
          <div className="flex justify-between font-semibold">
            {isExact ? (
              <>
                <span className="text-green-600">✓ Valor correto</span>
                <span className="text-green-600">—</span>
              </>
            ) : remaining > 0 ? (
              <>
                <span className="text-amber-600">Faltam</span>
                <span className="text-amber-600 tabular-nums">R$ {remaining.toFixed(2)}</span>
              </>
            ) : (
              <>
                <span className="text-muted-foreground">A mais</span>
                <span className="text-muted-foreground tabular-nums">R$ {Math.abs(remaining).toFixed(2)}</span>
              </>
            )}
          </div>

          {/* Troco — only when overpaid in cash */}
          {troco > 0 && (
            <div className="flex justify-between font-semibold text-green-600 pt-2 mt-1 border-t border-green-500/20">
              <span>Troco (dinheiro)</span>
              <span className="tabular-nums">R$ {troco.toFixed(2)}</span>
            </div>
          )}
        </div>

        {/* Error */}
        {payMut.isError && (
          <p className="text-xs text-destructive mb-3">
            Erro ao processar pagamento. Verifique a conexão e tente novamente.
          </p>
        )}

        {/* Confirm */}
        <Button
          className="w-full h-13 text-base"
          onClick={handlePay}
          disabled={!canConfirm || payMut.isPending}
        >
          {payMut.isPending ? "Processando..." : "Confirmar pagamento"}
        </Button>
      </SheetContent>
    </Sheet>
  );
}

// ─── PaymentEntryRow ──────────────────────────────────────────────────────────

interface PaymentEntryRowProps {
  entry: PaymentEntry;
  label?: string;
  canRemove: boolean;
  onMethodChange: (m: PayMethod) => void;
  onAmountChange: (v: string) => void;
  onRemove: () => void;
}

function PaymentEntryRow({
  entry, label, canRemove, onMethodChange, onAmountChange, onRemove,
}: PaymentEntryRowProps) {
  return (
    <div className="rounded-xl border border-border bg-card p-3 space-y-2.5">
      {label && (
        <p className="text-[11px] font-medium text-muted-foreground uppercase tracking-wide">
          {label}
        </p>
      )}
      <div className="flex items-center gap-2">
        {/* Method pills */}
        <div className="flex gap-1 flex-1">
          {METHODS.map(m => (
            <button
              key={m.key}
              type="button"
              onClick={() => onMethodChange(m.key)}
              className={cn(
                "flex-1 py-2 rounded-lg text-xs font-medium transition-colors",
                entry.method === m.key
                  ? "bg-primary text-primary-foreground"
                  : "bg-muted text-muted-foreground hover:text-foreground"
              )}
            >
              {m.label}
            </button>
          ))}
        </div>

        {/* Remove button */}
        {canRemove && (
          <button
            type="button"
            onClick={onRemove}
            className="p-1.5 text-muted-foreground hover:text-destructive transition-colors shrink-0"
          >
            <X className="h-4 w-4" />
          </button>
        )}
      </div>

      {/* Amount input — large for mobile */}
      <div className="relative">
        <span className="absolute left-3 top-1/2 -translate-y-1/2 text-muted-foreground text-sm font-medium pointer-events-none">
          R$
        </span>
        <Input
          type="number"
          min={0}
          step="0.01"
          value={entry.amount}
          onChange={e => onAmountChange(e.target.value)}
          className="pl-9 h-12 text-lg font-semibold text-right pr-3"
          placeholder="0,00"
          inputMode="decimal"
        />
      </div>
    </div>
  );
}

// ─── BreakdownRow ─────────────────────────────────────────────────────────────

function BreakdownRow({ label, value }: { label: string; value: number }) {
  return (
    <div className="flex justify-between text-muted-foreground">
      <span>{label}</span>
      <span className="text-foreground tabular-nums">R$ {value.toFixed(2)}</span>
    </div>
  );
}
