import { Separator } from "@/components/ui/separator";
import { formatCurrency } from "@/lib/formatters";
import type { DiscountMode } from "../hooks/usePosCart";

interface PosTotalsProps {
  subtotal: number;
  discountTotal: number;
  /** Raw user-entered discount value — used to annotate the % discount line. */
  discountValue?: number;
  discountMode?: DiscountMode;
  total: number;
}

export function PosTotals({
  subtotal,
  discountTotal,
  discountValue,
  discountMode,
  total,
}: PosTotalsProps) {
  const showPercentAnnotation =
    discountMode === "percentage" &&
    discountValue !== undefined &&
    discountValue > 0 &&
    discountTotal > 0;

  return (
    <div className="space-y-2 text-sm">
      <div className="flex justify-between text-muted-foreground">
        <span>Subtotal</span>
        <span className="tabular-nums">{formatCurrency(subtotal)}</span>
      </div>

      {discountTotal > 0 && (
        <div className="flex justify-between text-red-600 dark:text-red-400">
          <span className="flex items-center gap-1.5">
            Desconto
            {showPercentAnnotation && (
              <span className="text-xs opacity-70">({discountValue}%)</span>
            )}
          </span>
          <span className="tabular-nums">- {formatCurrency(discountTotal)}</span>
        </div>
      )}

      <Separator />
      <div className="flex justify-between items-center pt-1">
        <span className="text-base font-semibold">Total</span>
        <span className="text-2xl font-bold text-foreground tabular-nums">
          {formatCurrency(total)}
        </span>
      </div>
    </div>
  );
}
