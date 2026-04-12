import { useQuery } from "@tanstack/react-query";
import { Skeleton } from "@/components/ui/skeleton";
import { useMemo } from "react";
import { listSales } from "@/modules/sales/api/sales.api";
import { formatCurrency } from "@/lib/formatters";
import { Link } from "react-router-dom";

interface TopProductRow {
  productCode:       string;
  productDescription: string;
  quantitySold:      number;
  revenueGenerated:  number;
}

export function TopProducts() {
  const { data: sales = [], isLoading } = useQuery({
    queryKey: ["sales"],
    queryFn:  listSales,
  });

  const products = useMemo((): TopProductRow[] => {
    const byProduct = new Map<
      string,
      { code: string; description: string; qty: number; revenue: number }
    >();

    for (const sale of sales) {
      if (sale.status === "Cancelled") continue;
      for (const item of sale.items) {
        if (!byProduct.has(item.productId)) {
          byProduct.set(item.productId, {
            code:        item.productCode,
            description: item.productName,
            qty:         0,
            revenue:     0,
          });
        }
        const entry = byProduct.get(item.productId)!;
        entry.qty     += item.quantity;
        entry.revenue += item.total;
      }
    }

    return Array.from(byProduct.values())
      .map((p) => ({
        productCode:        p.code,
        productDescription: p.description,
        quantitySold:       p.qty,
        revenueGenerated:   Math.round(p.revenue * 100) / 100,
      }))
      .sort((a, b) => b.revenueGenerated - a.revenueGenerated)
      .slice(0, 5);
  }, [sales]);

  return (
    <div
      className="bg-card rounded-lg border border-border p-5 shadow-sm animate-fade-in"
      style={{ animationDelay: "375ms" }}
    >
      <div className="flex items-center justify-between mb-4">
        <h3 className="text-sm font-semibold text-foreground">Produtos mais vendidos</h3>
        {products.length > 0 && (
          <Link
            to="/relatorios"
            className="text-[11px] text-muted-foreground hover:text-foreground transition-colors"
          >
            Ver relatório
          </Link>
        )}
      </div>

      {isLoading ? (
        <div className="space-y-3">
          {Array.from({ length: 5 }).map((_, i) => (
            <Skeleton key={i} className="h-8 w-full" />
          ))}
        </div>
      ) : products.length === 0 ? (
        <div className="py-4 text-center space-y-1">
          <p className="text-sm font-medium text-foreground">Nenhuma venda ainda.</p>
          <p className="text-xs text-muted-foreground">Os produtos mais vendidos aparecem aqui.</p>
        </div>
      ) : (
        <div className="space-y-3">
          {products.map((p, i) => (
            <div key={p.productCode} className="flex items-center gap-3">
              <span className="text-xs font-bold text-muted-foreground w-5 text-right shrink-0">
                {i + 1}
              </span>
              <div className="flex-1 min-w-0">
                <p className="text-sm font-medium text-foreground truncate">
                  {p.productDescription}
                </p>
                <p className="text-xs text-muted-foreground">{p.quantitySold} un.</p>
              </div>
              <span className="text-sm font-semibold text-foreground whitespace-nowrap">
                {formatCurrency(p.revenueGenerated)}
              </span>
            </div>
          ))}
        </div>
      )}
    </div>
  );
}
