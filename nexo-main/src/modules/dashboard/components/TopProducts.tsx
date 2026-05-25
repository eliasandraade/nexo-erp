import { Skeleton } from "@/components/ui/skeleton";
import { useDashboardSummary } from "@/modules/dashboard/hooks/useDashboardSummary";
import { formatCurrency } from "@/lib/formatters";
import { Link } from "react-router-dom";

export function TopProducts() {
  const { data: summary, isLoading } = useDashboardSummary();
  const products = summary?.topProducts ?? [];

  return (
    <div className="bg-card rounded-xl border border-border p-5 animate-fade-in">
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
            <div key={p.productId} className="flex items-center gap-3">
              <span className="text-xs font-bold text-muted-foreground w-5 text-right shrink-0">
                {i + 1}
              </span>
              <div className="flex-1 min-w-0">
                <p className="text-sm font-medium text-foreground truncate">{p.productName}</p>
                <p className="text-xs text-muted-foreground">{p.quantitySold} un.</p>
              </div>
              <span className="text-sm font-semibold text-foreground whitespace-nowrap">
                {formatCurrency(p.revenue)}
              </span>
            </div>
          ))}
        </div>
      )}
    </div>
  );
}
