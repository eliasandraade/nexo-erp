import { Lightbulb, Package, Landmark, ShoppingCart, Percent, Activity } from "lucide-react";
import { Skeleton } from "@/components/ui/skeleton";
import { useMemo } from "react";
import { useDashboardSummary } from "@/modules/dashboard/hooks/useDashboardSummary";
import { Link } from "react-router-dom";
import type { InsightCategory, InsightSeverity } from "@/modules/insights/types";

const categoryIcons: Record<InsightCategory, React.ElementType> = {
  inventory:   Package,
  cash:        Landmark,
  sales:       ShoppingCart,
  commissions: Percent,
  operations:  Activity,
};

const severityColors: Record<InsightSeverity, { color: string; bg: string }> = {
  critical: { color: "text-destructive", bg: "bg-destructive/10" },
  warning:  { color: "text-warning",     bg: "bg-warning/10" },
  info:     { color: "text-secondary",   bg: "bg-secondary/10" },
};

interface Insight {
  id:          string;
  category:    InsightCategory;
  severity:    InsightSeverity;
  title:       string;
  description: string;
  value?:      string;
}

export function RecentInsights() {
  const { data: summary, isLoading } = useDashboardSummary();

  const insights = useMemo((): Insight[] => {
    if (!summary) return [];
    const list: Insight[] = [];
    let seq = 0;

    const make = (
      category: InsightCategory,
      severity: InsightSeverity,
      title: string,
      description: string,
      value?: string
    ): Insight => ({ id: `i-${seq++}`, category, severity, title, description, value });

    // Stock alerts
    if (summary.zeroStockCount > 0) {
      list.push(make(
        "inventory", "critical",
        "Produtos sem estoque",
        "Existem produtos com estoque zerado que podem impedir vendas.",
        `${summary.zeroStockCount} produto${summary.zeroStockCount > 1 ? "s" : ""}`,
      ));
    }

    if (summary.lowStockCount > 0) {
      list.push(make(
        "inventory", "warning",
        "Estoque baixo",
        "Alguns produtos estão com estoque abaixo do nível mínimo.",
        `${summary.lowStockCount} produto${summary.lowStockCount > 1 ? "s" : ""}`,
      ));
    }

    // Cash session
    if (!summary.hasOpenCashSession) {
      list.push(make(
        "cash", "warning",
        "Caixa não aberto",
        "Não há sessão de caixa ativa. O PDV não poderá finalizar vendas.",
      ));
    }

    // Cancellation rate
    if (summary.totalSales >= 5) {
      const rate = summary.cancelledCount / summary.totalSales;
      if (rate > 0.1) {
        list.push(make(
          "sales", "warning",
          "Taxa de cancelamento elevada",
          "A proporção de vendas canceladas está acima de 10% do total.",
          `${Math.round(rate * 100)}% de cancelamentos`,
        ));
      }
    }

    // Top seller insight
    if (summary.topSellers.length > 0) {
      const top = summary.topSellers[0];
      list.push(make(
        "sales", "info",
        "Melhor vendedor",
        `${top.sellerName} lidera em faturamento no período.`,
        top.revenue.toLocaleString("pt-BR", { style: "currency", currency: "BRL" }),
      ));
    }

    return list;
  }, [summary]);

  const top = [...insights]
    .sort((a, b) => {
      const order: Record<InsightSeverity, number> = { critical: 0, warning: 1, info: 2 };
      return order[a.severity] - order[b.severity];
    })
    .slice(0, 3);

  return (
    <div className="bg-card rounded-xl border border-border p-5 animate-fade-in">
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
            const Icon          = categoryIcons[item.category];
            const { color, bg } = severityColors[item.severity];
            return (
              <div key={item.id} className="flex gap-3">
                <div className={`w-8 h-8 rounded-lg ${bg} flex items-center justify-center shrink-0`}>
                  <Icon className={`h-4 w-4 ${color}`} />
                </div>
                <div className="flex-1 min-w-0">
                  <p className="text-sm font-medium text-foreground leading-snug">{item.title}</p>
                  <p className="text-xs text-muted-foreground mt-0.5 leading-relaxed line-clamp-2">
                    {item.description}
                  </p>
                  {item.value && (
                    <p className="text-[10px] text-muted-foreground mt-1 font-semibold">{item.value}</p>
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
