import { CheckCircle2, RotateCcw } from "lucide-react";
import { Button } from "@/components/ui/button";
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import { Separator } from "@/components/ui/separator";
import { paymentMethodLabels } from "../types";
import type { CompletedSale } from "../types";
import { formatCurrency } from "@/lib/formatters";

interface PosSaleSuccessModalProps {
  sale: CompletedSale | null;
  onNewSale: () => void;
}

function formatTime(iso: string) {
  return new Date(iso).toLocaleTimeString("pt-BR", {
    hour: "2-digit",
    minute: "2-digit",
    second: "2-digit",
  });
}

export function PosSaleSuccessModal({ sale, onNewSale }: PosSaleSuccessModalProps) {
  if (!sale) return null;

  return (
    <Dialog open={!!sale} onOpenChange={() => {}}>
      <DialogContent
        className="sm:max-w-sm"
        onPointerDownOutside={(e) => e.preventDefault()}
        onEscapeKeyDown={(e) => e.preventDefault()}
      >
        <DialogHeader>
          <div className="flex flex-col items-center gap-2 pt-2">
            <CheckCircle2 className="h-12 w-12 text-green-500" />
            <DialogTitle className="text-xl text-center">Venda finalizada!</DialogTitle>
            <p className="text-xs text-muted-foreground">{sale.id} · {formatTime(sale.timestamp)}</p>
          </div>
        </DialogHeader>

        <div className="space-y-3 text-sm">
          {/* Items summary */}
          <div className="space-y-1">
            {sale.items.map((item) => (
              <div key={item.productId} className="flex justify-between text-muted-foreground">
                <span>
                  {item.quantity}x {item.description}
                </span>
                <span className="tabular-nums">{formatCurrency(item.totalPrice)}</span>
              </div>
            ))}
          </div>

          <Separator />

          {/* Totals */}
          <div className="space-y-1">
            {sale.discountTotal > 0 && (
              <div className="flex justify-between text-muted-foreground">
                <span>Desconto</span>
                <span className="tabular-nums text-red-500">- {formatCurrency(sale.discountTotal)}</span>
              </div>
            )}
            <div className="flex justify-between font-bold text-base">
              <span>Total</span>
              <span className="tabular-nums">{formatCurrency(sale.total)}</span>
            </div>
          </div>

          <Separator />

          {/* Payment */}
          <div className="space-y-1">
            {sale.payments.map((p, i) => (
              <div key={i} className="flex justify-between text-muted-foreground">
                <span>{paymentMethodLabels[p.method]}</span>
                <span className="tabular-nums">{formatCurrency(p.amount)}</span>
              </div>
            ))}
            {sale.change > 0 && (
              <div className="flex justify-between font-semibold text-green-600">
                <span>Troco</span>
                <span className="tabular-nums">{formatCurrency(sale.change)}</span>
              </div>
            )}
          </div>
        </div>

        <Button className="w-full mt-2" onClick={onNewSale}>
          <RotateCcw className="h-4 w-4 mr-2" />
          Nova venda
        </Button>
      </DialogContent>
    </Dialog>
  );
}
