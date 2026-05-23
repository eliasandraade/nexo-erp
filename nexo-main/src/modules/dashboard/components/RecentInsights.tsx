import { Lightbulb, Package, Landmark, ShoppingCart, Percent, Activity } from "lucide-react";
import { useQuery } from "@tanstack/react-query";
import { Skeleton } from "@/components/ui/skeleton";
import { useMemo } from "react";
import { listSales } from "@/modules/sales/api/sales.api";
import { useStockItems } from "@/modules/inventory/hooks/use-stock";
import { useProducts } from "@/modules/products/hooks/use-products";
import { useOpenSession } from "@/modules/cash/hooks/use-cash";
import { deriveStockStatus } from "@/modules/inventory/types";
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

let seq = 0;
function makeInsight(
  category: InsightCategory,
  severity: InsightSeverity,
  title: string,
  description: string,
  value?: string
): Insight {
  return { id: `i-${seq++}`, category, severity, title, description, value };
}

export function RecentInsights() {
  const { data: sales        = [], isLoading: loadingSales }    = useQuery({ queryKey: ["sales"],   queryFn: listSales });
  const { data: stockItems   = [], isLoading: loadingStock }    = useStockItems();
  const { data: products     = [], isLoading: loadingProducts } = useProducts();
  const { data: cashSession,       isLoading: loadingCash }     = useOpenSession();

  const isLoading = loadingSales || loadingStock || loadingProducts || loadingCash;

  const insights = useMemo((): Insight[] => {
    seq = 0;
    const list: Insight[] = [];

    // ── Inventory: zero stock ─────────────────────────────────────────────────
    const zeroCount = stockItems.filter((s) => {
      const p = products.find((p) => p.id === s.productId);
      return deriveStockStatus(s.availableQuantity, p?.minStockQuantity ?? null) === "zero";
    }).length;

    if (zeroCount > 0) {
      list.push(makeInsight(
        "inventory", "critical",
        "Produtos sem estoque",
        "Existem produtos com estoque zerado que podem impedir vendas.",
        `${zeroCount} produto${zeroCount > 1 ? "s" : ""}`,
      ));
    }

    // ── Inventory: low stock ──────────────────────────────────────────────────
    const lowCount = stockItems.filter((s) => {
      const p = products.find((p) => p.id === s.productId);
      return deriveStockStatus(s.availableQuantity, p?.minStockQuantity ?? null) === "low";
    }).length;

    if (lowCount > 0) {
      list.push(makeInsight(
        "inventory", "warning",
        "Estoque baixo",
        "Alguns produtos estão com estoque abaixo do nível mínimo.",
        `${lowCount} produto${lowCount > 1 ? "s" : ""}`,
      ));
    }

    // ── Cash: no open session ─────────────────────────────────────────────────
    if (!loadingCash && cashSession?.status !== "Open") {
      list.push(makeInsight(
        "cash", "warning",
        "Caixa não aberto",
        "Não há sessão de caixa ativa. O PDV não poderá finalizar vendas.",
      ));
    }

    // ── Sales: high cancellation rate (min 5 sales) ───────────────────────────
    const totalSales     = sales.length;
    const cancelledCount = sales.filter((s) => s.status === "Cancelled").length;
    if (totalSales >= 5) {
      const rate = cancelledCount / totalSales;
      if (rate > 0.1) {
        list.push(makeInsight(
          "sales", "warning",
          "Taxa de cancelamento elevada",
          "A proporção de vendas canceladas está acima de 10% do total.",
          `${Math.round(rate * 100)}% de cancelamentos`,
        ));
      }
    }

    // ── Sales: top seller (info) ──────────────────────────────────────────────
    const bySeller = new Map<string, number>();
    for (const s of sales) {
      if (s.status === "Cancelled") continue;
      bySeller.set(s.soldByName, (bySeller.get(s.soldByName) ?? 0) + s.total);
    }
    if (bySeller.size > 0) {
      const [topName, topRevenue] = [...bySeller.entries()]
        .sort(([, a], [, b]) => b - a)[0];
      list.push(makeInsight(
        "sales", "info",
        "Melhor vendedor",
        `${topName} lidera em faturamento no período.`,
        topRevenue.toLocaleString("pt-BR", { style: "currency", currency: "BRL" }),
      ));
    }

    return list;
  }, [sales, stockItems, products, cashSession, loadingCash]);

  // Show up to 3 most critical first
  const top = [...insights]
    .sort((a, b) => {
      const order: Record<InsightSeverity, number> = { critical: 0, warning: 1, info: 2 };
      return order[a.severity] - order[b.severity];
    })
    .slice(0, 3);

  return (
    <div
      className="bg-card rounded-xl border border-border p-5 animate-fade-in"
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
            const Icon            = categoryIcons[item.category];
            const { color, bg }   = severityColors[item.severity];
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
