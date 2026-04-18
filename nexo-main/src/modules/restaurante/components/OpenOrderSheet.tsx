import { useState, useEffect } from "react";
import {
  Sheet, SheetContent, SheetHeader, SheetTitle,
} from "@/components/ui/sheet";
import { Button } from "@/components/ui/button";
import { cn } from "@/lib/utils";
import type { OrderType } from "../types";

interface OpenOrderSheetProps {
  open: boolean;
  tableNumber?: string;
  onClose: () => void;
  onSubmit: (orderType: OrderType, partySize: number | null) => void;
  isLoading: boolean;
}

const ORDER_TYPES: { value: OrderType; label: string }[] = [
  { value: "Counter",  label: "Balcão" },
  { value: "Takeaway", label: "Retirada" },
];

// Stepper for party size — faster than typing on mobile under pressure
function PartyStepper({
  value, onChange,
}: {
  value: number;
  onChange: (v: number) => void;
}) {
  return (
    <div className="flex items-center gap-3">
      <button
        type="button"
        onClick={() => onChange(Math.max(1, value - 1))}
        disabled={value <= 1}
        className="h-11 w-11 rounded-lg border border-border flex items-center justify-center text-foreground disabled:opacity-30 hover:bg-muted transition-colors text-xl leading-none"
      >
        –
      </button>
      <span className="text-xl font-semibold w-8 text-center tabular-nums">{value}</span>
      <button
        type="button"
        onClick={() => onChange(value + 1)}
        className="h-11 w-11 rounded-lg border border-border flex items-center justify-center text-foreground hover:bg-muted transition-colors text-xl leading-none"
      >
        +
      </button>
    </div>
  );
}

export function OpenOrderSheet({
  open, tableNumber, onClose, onSubmit, isLoading,
}: OpenOrderSheetProps) {
  const tableMode = !!tableNumber;

  const [orderType, setOrderType] = useState<OrderType>(tableMode ? "DineIn" : "Counter");
  const [partySize, setPartySize] = useState(1);

  useEffect(() => {
    if (open) {
      setOrderType(tableMode ? "DineIn" : "Counter");
      setPartySize(1);
    }
  }, [open, tableMode]);

  const handleSubmit = () => {
    onSubmit(orderType, partySize > 0 ? partySize : null);
  };

  return (
    <Sheet open={open} onOpenChange={(v) => !v && onClose()}>
      <SheetContent side="bottom" className="rounded-t-2xl pb-8">
        <SheetHeader className="mb-5">
          <SheetTitle>
            {tableMode ? `Mesa ${tableNumber}` : "Nova comanda"}
          </SheetTitle>
          {tableMode && (
            <p className="text-sm text-muted-foreground -mt-1">
              Abrindo comanda de mesa
            </p>
          )}
        </SheetHeader>

        {/* Type selector — only shown when no table is pre-selected */}
        {!tableMode && (
          <div className="flex gap-2 mb-5">
            {ORDER_TYPES.map((t) => (
              <button
                key={t.value}
                onClick={() => setOrderType(t.value)}
                className={cn(
                  "flex-1 rounded-lg py-2.5 text-sm font-medium border transition-colors",
                  orderType === t.value
                    ? "border-primary bg-primary/10 text-primary"
                    : "border-border text-muted-foreground"
                )}
              >
                {t.label}
              </button>
            ))}
          </div>
        )}

        {/* Party size stepper */}
        <div className="mb-6">
          <label className="text-sm text-muted-foreground mb-3 block">
            Número de pessoas
            <span className="ml-1 text-xs opacity-60">(opcional)</span>
          </label>
          <PartyStepper value={partySize} onChange={setPartySize} />
        </div>

        <Button
          className="w-full h-12 text-base"
          onClick={handleSubmit}
          disabled={isLoading}
        >
          {isLoading ? "Abrindo..." : "Abrir comanda"}
        </Button>
      </SheetContent>
    </Sheet>
  );
}
