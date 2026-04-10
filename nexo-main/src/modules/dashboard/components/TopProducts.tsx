import { useQuery } from "@tanstack/react-query";
import { Skeleton } from "@/components/ui/skeleton";
import { reportService } from "@/modules/reports/services/reportService";
import { formatCurrency } from "@/lib/formatters";
import { Link } from "react-router-dom";

export function TopProducts() {
  const { data: products = [], isLoading } = useQuery({
    queryKey: ["dashboard-top-products"],
    queryFn: () => reportService.getTopProducts(5),
  });

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
        <p className="text-xs text-muted-foreground text-center py-4">
          Nenhuma venda registrada.
        </p>
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
