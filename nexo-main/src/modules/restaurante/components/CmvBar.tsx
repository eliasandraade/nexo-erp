import { cn } from "@/lib/utils";

interface Props {
  ingredientCost: number;
  gasCost: number;
  laborCost: number;
  calculatedCost: number;
  salePrice: number;
  cmvPercent: number;
}

function fmt(v: number) {
  return v.toLocaleString("pt-BR", { style: "currency", currency: "BRL", minimumFractionDigits: 2 });
}

function cmvColor(pct: number) {
  if (pct < 30) return "text-green-600 dark:text-green-400";
  if (pct <= 40) return "text-yellow-600 dark:text-yellow-400";
  return "text-red-600 dark:text-red-400";
}

export function CmvBar({ ingredientCost, gasCost, laborCost, calculatedCost, salePrice, cmvPercent }: Props) {
  return (
    <div className="sticky bottom-0 z-10 border-t bg-background/95 backdrop-blur px-6 py-3">
      <div className="flex items-center gap-6 flex-wrap text-sm">
        <div className="flex items-center gap-1.5 text-muted-foreground">
          <span>Ingredientes:</span>
          <span className="font-medium text-foreground">{fmt(ingredientCost)}</span>
        </div>
        {gasCost > 0 && (
          <div className="flex items-center gap-1.5 text-muted-foreground">
            <span>Gás:</span>
            <span className="font-medium text-foreground">{fmt(gasCost)}</span>
          </div>
        )}
        {laborCost > 0 && (
          <div className="flex items-center gap-1.5 text-muted-foreground">
            <span>MO:</span>
            <span className="font-medium text-foreground">{fmt(laborCost)}</span>
          </div>
        )}
        <div className="flex items-center gap-1.5 text-muted-foreground">
          <span>Custo total:</span>
          <span className="font-medium text-foreground">{fmt(calculatedCost)}</span>
        </div>
        <div className="flex items-center gap-1.5 text-muted-foreground">
          <span>Venda:</span>
          <span className="font-medium text-foreground">{fmt(salePrice)}</span>
        </div>
        <div className="ml-auto flex items-center gap-2">
          <span className="text-muted-foreground">CMV</span>
          <span className={cn("text-xl font-bold tabular-nums", cmvColor(cmvPercent))}>
            {cmvPercent.toFixed(1)}%
          </span>
        </div>
      </div>
    </div>
  );
}
