import { Package, AlertTriangle, TrendingDown } from "lucide-react";
import { cn } from "@/lib/utils";

interface InventoryKpiCardsProps {
  totalProducts: number;
  belowMin: number;
  noTurnover: number;
}

export function InventoryKpiCards({ totalProducts, belowMin, noTurnover }: InventoryKpiCardsProps) {
  const cards = [
    {
      label:     "Itens em estoque",
      value:     totalProducts,
      icon:      Package,
      iconColor: "text-[#5B4DFF]",
      strip:     "bg-[#5B4DFF]",
      subOk:     true,
      sub:       "produtos cadastrados",
    },
    {
      label:     "Abaixo do mínimo",
      value:     belowMin,
      icon:      AlertTriangle,
      iconColor: belowMin > 0 ? "text-warning" : "text-success",
      strip:     belowMin > 0 ? "bg-warning" : "bg-success",
      subOk:     belowMin === 0,
      sub:       belowMin > 0 ? "requerem reposição" : "estoque normalizado",
    },
    {
      label:     "Sem movimentação",
      value:     noTurnover,
      icon:      TrendingDown,
      iconColor: noTurnover > 0 ? "text-destructive" : "text-muted-foreground",
      strip:     noTurnover > 0 ? "bg-destructive" : "bg-muted-foreground",
      subOk:     noTurnover === 0,
      sub:       noTurnover > 0 ? "sem mov. em 14 dias" : "todos com movimentação",
    },
  ];

  return (
    <div className="grid grid-cols-1 sm:grid-cols-3 gap-4">
      {cards.map((c) => (
        <div key={c.label} className="bg-card rounded-xl border border-border p-5 relative overflow-hidden">
          {/* Top accent strip */}
          <div className={cn("absolute top-0 left-0 right-0 h-[2px]", c.strip)} />

          <div className="flex items-center justify-between mb-3 pt-0.5">
            <p className="text-[11px] font-semibold uppercase tracking-[0.09em] text-muted-foreground">
              {c.label}
            </p>
            <c.icon className={cn("h-3.5 w-3.5", c.iconColor)} />
          </div>

          <p className="font-display text-[26px] font-bold text-foreground leading-none">
            {c.value}
          </p>

          <p className={cn("text-[11px] mt-2 font-medium", c.subOk ? "text-muted-foreground" : "text-warning")}>
            {c.sub}
          </p>
        </div>
      ))}
    </div>
  );
}
