import { useQuery } from "@tanstack/react-query";
import { Skeleton } from "@/components/ui/skeleton";
import { commissionService } from "@/modules/commissions/services/commissionService";
import { formatCurrency } from "@/lib/formatters";

function getInitials(name: string): string {
  return name
    .split(" ")
    .slice(0, 2)
    .map((w) => w[0]?.toUpperCase() ?? "")
    .join("");
}

export function SellerRanking() {
  const { data: sellers = [], isLoading } = useQuery({
    queryKey: ["dashboard-seller-ranking"],
    queryFn: () => commissionService.getCommissionSummaryBySeller(),
  });

  // Show top 4 by active commission
  const top = sellers.slice(0, 4);

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
      ) : top.length === 0 ? (
        <div className="py-4 text-center space-y-1">
          <p className="text-sm font-medium text-foreground">Sem dados ainda.</p>
          <p className="text-xs text-muted-foreground">O ranking aparece após as primeiras vendas.</p>
        </div>
      ) : (
        <div className="space-y-3">
          {top.map((s, i) => (
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
                {formatCurrency(s.activeCommission)}
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
