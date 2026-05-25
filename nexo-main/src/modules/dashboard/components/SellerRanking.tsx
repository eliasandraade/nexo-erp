import { Skeleton } from "@/components/ui/skeleton";
import { useDashboardSummary } from "@/modules/dashboard/hooks/useDashboardSummary";
import { formatCurrency } from "@/lib/formatters";

function getInitials(name: string): string {
  return name
    .split(" ")
    .slice(0, 2)
    .map((w) => w[0]?.toUpperCase() ?? "")
    .join("");
}

export function SellerRanking() {
  const { data: summary, isLoading } = useDashboardSummary();
  const sellers = summary?.topSellers ?? [];

  return (
    <div className="bg-card rounded-xl border border-border p-5 animate-fade-in">
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
            <div key={s.sellerName} className="flex items-center gap-3">
              <div className="w-8 h-8 rounded-full bg-primary/10 flex items-center justify-center shrink-0">
                <span className="text-[10px] font-bold text-primary">
                  {getInitials(s.sellerName)}
                </span>
              </div>
              <div className="flex-1 min-w-0">
                <p className="text-sm font-medium text-foreground truncate">{s.sellerName}</p>
                <p className="text-xs text-muted-foreground">{s.salesCount} venda(s)</p>
              </div>
              <span className="text-sm font-semibold text-foreground whitespace-nowrap">
                {formatCurrency(s.revenue)}
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
