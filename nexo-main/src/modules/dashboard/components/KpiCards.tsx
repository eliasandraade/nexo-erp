import { DollarSign, TrendingUp, Receipt, AlertTriangle } from "lucide-react";
import { useQuery } from "@tanstack/react-query";
import { Skeleton } from "@/components/ui/skeleton";
import { useMemo } from "react";
import { listSales } from "@/modules/sales/api/sales.api";
import { useStockItems } from "@/modules/inventory/hooks/use-stock";
import { useProducts } from "@/modules/products/hooks/use-products";
import { deriveStockStatus } from "@/modules/inventory/types";
import { formatCurrency } from "@/lib/formatters";
import { cn } from "@/lib/utils";

// ─── Types ────────────────────────────────────────────────────────────────────

type Accent = "indigo" | "success" | "secondary" | "warning";

interface KpiDef {
  label:   string;
  value:   string;
  sub:     string;
  subOk:   boolean;
  icon:    React.ElementType;
  accent:  Accent;
}

// Hard-coded so Tailwind JIT can detect the classes at build time
const ACCENT_STRIP: Record<Accent, string> = {
  indigo:    "bg-[#5B4DFF]",
  success:   "bg-success",
  secondary: "bg-secondary",
  warning:   "bg-warning",
};
const ACCENT_ICON: Record<Accent, string> = {
  indigo:    "text-[#5B4DFF]",
  success:   "text-success",
  secondary: "text-secondary",
  warning:   "text-warning",
};

// ─── Single card ─────────────────────────────────────────────────────────────

function KpiCard({ kpi, delay }: { kpi: KpiDef; delay: number }) {
  return (
    <div
      className="bg-card rounded-xl border border-border p-5 animate-fade-in relative overflow-hidden"
      style={{ animationDelay: `${delay}ms` }}
    >
      {/* Top accent strip */}
      <div className={cn("absolute top-0 left-0 right-0 h-[2px]", ACCENT_STRIP[kpi.accent])} />

      {/* Label + icon */}
      <div className="flex items-center justify-between mb-3 pt-0.5">
        <p className="text-[11px] font-semibold uppercase tracking-[0.09em] text-muted-foreground">
          {kpi.label}
        </p>
        <kpi.icon className={cn("h-3.5 w-3.5 shrink-0", ACCENT_ICON[kpi.accent])} />
      </div>

      {/* Value */}
      <p className="text-[26px] font-bold text-foreground leading-none tracking-tight font-display">
        {kpi.value}
      </p>

      {/* Sub */}
      <p className={cn("text-[11px] mt-2 font-medium", kpi.subOk ? "text-muted-foreground" : "text-warning")}>
        {kpi.sub}
      </p>
    </div>
  );
}

// ─── Grid ─────────────────────────────────────────────────────────────────────

export function KpiCards() {
  const { data: sales = [],      isLoading: loadingSales    } = useQuery({ queryKey: ["sales"], queryFn: listSales });
  const { data: stockItems = [], isLoading: loadingStock    } = useStockItems();
  const { data: products = [],   isLoading: loadingProducts } = useProducts();

  const isLoading = loadingSales || loadingStock || loadingProducts;

  const operational = useMemo(() => {
    const activeSales    = sales.filter((s) => s.status !== "Cancelled");
    const cancelledCount = sales.filter((s) => s.status === "Cancelled").length;
    const totalRevenue   = activeSales.reduce((acc, s) => acc + s.total, 0);
    const averageTicket  = activeSales.length > 0
      ? Math.round((totalRevenue / activeSales.length) * 100) / 100
      : 0;
    return {
      totalSales:    sales.length,
      totalRevenue:  Math.round(totalRevenue * 100) / 100,
      averageTicket,
      cancelledCount,
    };
  }, [sales]);

  const alertCount = useMemo(() => {
    return stockItems.filter((s) => {
      const product  = products.find((p) => p.id === s.productId);
      const minStock = product?.minStockQuantity ?? null;
      const status   = deriveStockStatus(s.availableQuantity, minStock);
      return status === "low" || status === "zero";
    }).length;
  }, [stockItems, products]);

  if (isLoading) {
    return (
      <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4">
        {Array.from({ length: 4 }).map((_, i) => (
          <Skeleton key={i} className="h-[106px] rounded-xl" />
        ))}
      </div>
    );
  }

  const kpis: KpiDef[] = [
    {
      label:  "Faturamento",
      value:  formatCurrency(operational.totalRevenue),
      sub:    `${operational.totalSales} venda(s) no período`,
      subOk:  true,
      icon:   DollarSign,
      accent: "indigo",
    },
    {
      label:  "Ticket médio",
      value:  formatCurrency(operational.averageTicket),
      sub:    "por venda ativa",
      subOk:  true,
      icon:   TrendingUp,
      accent: "success",
    },
    {
      label:  "Vendas",
      value:  String(operational.totalSales),
      sub:    operational.cancelledCount > 0
        ? `${operational.cancelledCount} cancelada(s)`
        : "sem cancelamentos",
      subOk:  operational.cancelledCount === 0,
      icon:   Receipt,
      accent: "secondary",
    },
    {
      label:  "Alerta estoque",
      value:  String(alertCount),
      sub:    alertCount > 0 ? "itens requerem atenção" : "estoque normalizado",
      subOk:  alertCount === 0,
      icon:   AlertTriangle,
      accent: alertCount > 0 ? "warning" : "success",
    },
  ];

  return (
    <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4">
      {kpis.map((kpi, i) => (
        <KpiCard key={kpi.label} kpi={kpi} delay={i * 60} />
      ))}
    </div>
  );
}
