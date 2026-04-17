import { useState } from "react";
import {
  Sheet, SheetContent, SheetHeader, SheetTitle,
} from "@/components/ui/sheet";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
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
  { value: "DineIn",   label: "Mesa" },
  { value: "Counter",  label: "Balcão" },
  { value: "Takeaway", label: "Retirada" },
];

export function OpenOrderSheet({
  open, tableNumber, onClose, onSubmit, isLoading
}: OpenOrderSheetProps) {
  const defaultType: OrderType = tableNumber ? "DineIn" : "Counter";
  const [orderType, setOrderType] = useState<OrderType>(defaultType);
  const [partySize, setPartySize] = useState("");

  const handleSubmit = () => {
    const ps = partySize ? parseInt(partySize, 10) : null;
    onSubmit(orderType, ps);
  };

  return (
    <Sheet open={open} onOpenChange={(v) => !v && onClose()}>
      <SheetContent side="bottom" className="rounded-t-2xl pb-8">
        <SheetHeader className="mb-4">
          <SheetTitle>
            {tableNumber ? `Abrir comanda — Mesa ${tableNumber}` : "Nova comanda"}
          </SheetTitle>
        </SheetHeader>

        <div className="flex gap-2 mb-4">
          {ORDER_TYPES.map((t) => (
            <button
              key={t.value}
              onClick={() => setOrderType(t.value)}
              className={cn(
                "flex-1 rounded-lg py-2 text-sm font-medium border transition-colors",
                orderType === t.value
                  ? "border-primary bg-primary/10 text-primary"
                  : "border-border text-muted-foreground"
              )}
            >
              {t.label}
            </button>
          ))}
        </div>

        <div className="mb-6">
          <label className="text-sm text-muted-foreground mb-1 block">
            Pessoas (opcional)
          </label>
          <Input
            type="number"
            min={1}
            placeholder="Ex: 4"
            value={partySize}
            onChange={(e) => setPartySize(e.target.value)}
            className="w-full"
          />
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
