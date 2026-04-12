import { useQuery } from "@tanstack/react-query";
import { Skeleton } from "@/components/ui/skeleton";
import { useMemo } from "react";
import { listSales } from "@/modules/sales/api/sales.api";
import { formatCurrency } from "@/lib/formatters";

function getInitials(name: string): string {
  return name
    .split(" ")
    .slice(0, 2)
    .map((w) => w[0]?.toUpperCase() ?? "")
    .join("");
}

interface SellerRow {
  operator:        string;
  totalSalesCount: number;
  totalRevenue:    number;
}

export function SellerRanking() {
  const { data: sales = [], isLoading } = useQuery({
    queryKey: ["sales"],
    queryFn:  listSales,
  });

  const sellers = useMemo((): SellerRow[] => {
    const bySeller = new Map<string, { count: number; revenue: number }>();

    for (const sale of sales) {
      if (sale.status === "Cancelled") continue;
      if (!bySeller.has(sale.soldByName)) {
        bySeller.set(sale.soldByName, { count: 0, revenue: 0 });
      }
      const entry = bySeller.get(sale.soldByName)!;
      entry.count++;
      entry.revenue += sale.total;
    }

    return Array.from(bySeller.entries())
      .map(([operator, data]) => ({
        operator,
        totalSalesCount: data.count,
        totalRevenue:    Math.round(data.revenue * 100) / 100,
      }))
      .sort((a, b) => b.totalRevenue - a.totalRevenue)
      .slice(0, 4);
  }, [sales]);

  return (
    <div
      className="bg-card rounded-lg border border-border p-5 shadow-sm animate-fade-in"
      style={{ animationDelay: "450ms" }}
    >
      <h3 className="text-sm font-semibold text-foreground mb-4">Ranking de vendedores</h3>

      {isLoading ? (
        <div className="space-y-3">
          {Array.from({ length: 4 }).map((_, i) => (
            <Skeleton key={i} className="h-8 w-full" />
          ))}
        </div>
      ) : sellers.length === 0 ? (
        <div className="py-4 text-center space-y-1">
          <p className="text-sm font-medium text-foreground">Sem dados ainda.</p>
          <p className="text-xs text-muted-foreground">O ranking aparece após as primeiras vendas.</p>
        </div>
      ) : (
        <div className="space-y-3">
          {sellers.map((s, i) => (
            <div key={s.operator} className="flex items-center gap-3">
              <div className="w-8 h-8 rounded-full bg-primary/10 flex items-center justify-center shrink-0">
                <span className="text-[10px] font-bold text-primary">
                  {getInitials(s.operator)}
                </span>
              </div>
              <div className="flex-1 min-w-0">
                <p className="text-sm font-medium text-foreground truncate">{s.operator}</p>
                <p className="text-xs text-muted-foreground">{s.totalSalesCount} venda(s)</p>
              </div>
              <span className="text-sm font-semibold text-foreground whitespace-nowrap">
                {formatCurrency(s.totalRevenue)}
              </span>
              {i === 0 && (
                <span className="text-[10px] font-bold bg-warning/10 text-warning px-1.5 py-0.5 rounded">
                  🏆
                </span>
              )}
            </div>
          ))}
        </div>
      )}
    </div>
  );
}
