import { Lightbulb, TrendingUp, AlertTriangle, Package, Landmark, ShoppingCart, Percent, Activity } from "lucide-react";
import { useQuery } from "@tanstack/react-query";
import { Skeleton } from "@/components/ui/skeleton";
import { insightService } from "@/modules/insights/services/insightService";
import type { InsightCategory, InsightSeverity } from "@/modules/insights/types";
import { Link } from "react-router-dom";

const categoryIcons: Record<InsightCategory, React.ElementType> = {
  inventory: Package,
  cash: Landmark,
  sales: ShoppingCart,
  commissions: Percent,
  operations: Activity,
};

const severityColors: Record<InsightSeverity, { color: string; bg: string }> = {
  critical: { color: "text-destructive", bg: "bg-destructive/10" },
  warning: { color: "text-warning", bg: "bg-warning/10" },
  info: { color: "text-secondary", bg: "bg-secondary/10" },
};

export function RecentInsights() {
  const { data: insights = [], isLoading } = useQuery({
    queryKey: ["dashboard-insights"],
    queryFn: () => insightService.generateInsights(),
  });

  // Show up to 3 most critical first
  const top = [...insights]
    .sort((a, b) => {
      const order: Record<InsightSeverity, number> = { critical: 0, warning: 1, info: 2 };
      return order[a.severity] - order[b.severity];
    })
    .slice(0, 3);

  return (
    <div
      className="bg-card rounded-lg border border-border p-5 shadow-sm animate-fade-in"
      style={{ animationDelay: "525ms" }}
    >
      <div className="flex items-center justify-between mb-4">
        <h3 className="text-sm font-semibold text-foreground">Insights recentes</h3>
        {insights.length > 0 && (
          <Link
            to="/insights"
            className="text-[11px] text-muted-foreground hover:text-foreground transition-colors"
          >
            Ver todos
          </Link>
        )}
      </div>

      {isLoading ? (
        <div className="space-y-3">
          {Array.from({ length: 3 }).map((_, i) => (
            <Skeleton key={i} className="h-12 w-full" />
          ))}
        </div>
      ) : top.length === 0 ? (
        <div className="flex flex-col items-center gap-2 py-4">
          <Lightbulb className="h-5 w-5 text-muted-foreground/50" />
          <p className="text-sm font-medium text-foreground">Tudo em ordem.</p>
          <p className="text-xs text-muted-foreground">Insights aparecem conforme o sistema acumula dados.</p>
        </div>
      ) : (
        <div className="space-y-4">
          {top.map((item) => {
            const Icon = categoryIcons[item.category];
            const { color, bg } = severityColors[item.severity];
            return (
              <div key={item.id} className="flex gap-3">
                <div
                  className={`w-8 h-8 rounded-lg ${bg} flex items-center justify-center shrink-0`}
                >
                  <Icon className={`h-4 w-4 ${color}`} />
                </div>
                <div className="flex-1 min-w-0">
                  <p className="text-sm font-medium text-foreground leading-snug">{item.title}</p>
                  <p className="text-xs text-muted-foreground mt-0.5 leading-relaxed line-clamp-2">
                    {item.description}
                  </p>
                  {item.value && (
                    <p className="text-[10px] text-muted-foreground mt-1 font-semibold">
                      {item.value}
                    </p>
                  )}
                </div>
              </div>
            );
          })}
        </div>
      )}
    </div>
  );
}
