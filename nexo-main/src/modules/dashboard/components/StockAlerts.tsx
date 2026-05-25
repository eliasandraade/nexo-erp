import { AlertTriangle } from "lucide-react";
import { Skeleton } from "@/components/ui/skeleton";
import { useDashboardSummary } from "@/modules/dashboard/hooks/useDashboardSummary";
import { Link } from "react-router-dom";

export function StockAlerts() {
  const { data: summary, isLoading } = useDashboardSummary();
  const alertItems = summary?.stockAlerts ?? [];
  const hasAlerts  = alertItems.length > 0;

  return (
    <div className="bg-card rounded-xl border border-border p-5 animate-fade-in">
      <div className="flex items-center justify-between mb-4">
        <div className="flex items-center gap-2">
          <AlertTriangle className="h-4 w-4 text-warning" />
          <h3 className="text-sm font-semibold text-foreground">Alertas de estoque</h3>
        </div>
        {hasAlerts && (
          <Link
            to="/estoque"
            className="text-[11px] text-muted-foreground hover:text-foreground transition-colors"
          >
            Ver todos
          </Link>
        )}
      </div>

      {isLoading ? (
        <div className="space-y-3">
          {Array.from({ length: 3 }).map((_, i) => (
            <Skeleton key={i} className="h-8 w-full" />
          ))}
        </div>
      ) : !hasAlerts ? (
        <div className="py-4 text-center space-y-1">
          <p className="text-sm font-medium text-foreground">Estoque saudável.</p>
          <p className="text-xs text-muted-foreground">Nenhum produto abaixo do mínimo.</p>
        </div>
      ) : (
        <div className="space-y-3">
          {alertItems.map((a) => {
            const pct = a.minStock > 0 ? Math.round((a.currentStock / a.minStock) * 100) : 0;
            const barColor =
              a.status === "zero"
                ? "hsl(0, 84%, 60%)"
                : pct <= 50
                ? "hsl(38, 92%, 50%)"
                : "hsl(160, 84%, 39%)";
            return (
              <div key={a.productId}>
                <div className="flex items-center justify-between mb-1">
                  <p className="text-sm font-medium text-foreground truncate">{a.productName}</p>
                  <span className="text-xs font-semibold text-destructive whitespace-nowrap ml-2">
                    {a.currentStock}/{a.minStock}
                  </span>
                </div>
                <div className="w-full h-1.5 bg-muted rounded-full overflow-hidden">
                  <div
                    className="h-full rounded-full transition-all"
                    style={{ width: `${Math.min(pct, 100)}%`, backgroundColor: barColor }}
                  />
                </div>
              </div>
            );
          })}
        </div>
      )}
    </div>
  );
}
