import { DollarSign, TrendingUp, Receipt, AlertTriangle } from "lucide-react";
import { Skeleton } from "@/components/ui/skeleton";
import { useDashboardSummary } from "@/modules/dashboard/hooks/useDashboardSummary";
import { formatCurrency } from "@/lib/formatters";
import { cn } from "@/lib/utils";

type Accent = "indigo" | "success" | "secondary" | "warning";

interface KpiDef {
  label:  string;
  value:  string;
  sub:    string;
  subOk:  boolean;
  icon:   React.ElementType;
  accent: Accent;
}

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

function KpiCard({ kpi }: { kpi: KpiDef }) {
  return (
    <div className="bg-card rounded-xl border border-border p-5 animate-fade-in relative overflow-hidden">
      <div className={cn("absolute top-0 left-0 right-0 h-[2px]", ACCENT_STRIP[kpi.accent])} />
      <div className="flex items-center justify-between mb-3 pt-0.5">
        <p className="text-[11px] font-semibold uppercase tracking-[0.09em] text-muted-foreground">
          {kpi.label}
        </p>
        <kpi.icon className={cn("h-3.5 w-3.5 shrink-0", ACCENT_ICON[kpi.accent])} />
      </div>
      <p className="text-[26px] font-bold text-foreground leading-none tracking-tight font-display">
        {kpi.value}
      </p>
      <p className={cn("text-[11px] mt-2 font-medium", kpi.subOk ? "text-muted-foreground" : "text-warning")}>
        {kpi.sub}
      </p>
    </div>
  );
}

export function KpiCards() {
  const { data: summary, isLoading } = useDashboardSummary();

  if (isLoading) {
    return (
      <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4">
        {Array.from({ length: 4 }).map((_, i) => (
          <Skeleton key={i} className="h-[106px] rounded-xl" />
        ))}
      </div>
    );
  }

  const alertCount = (summary?.zeroStockCount ?? 0) + (summary?.lowStockCount ?? 0);

  const kpis: KpiDef[] = [
    {
      label:  "Faturamento",
      value:  formatCurrency(summary?.totalRevenue ?? 0),
      sub:    `${summary?.totalSales ?? 0} venda(s) no período`,
      subOk:  true,
      icon:   DollarSign,
      accent: "indigo",
    },
    {
      label:  "Ticket médio",
      value:  formatCurrency(summary?.averageTicket ?? 0),
      sub:    "por venda ativa",
      subOk:  true,
      icon:   TrendingUp,
      accent: "success",
    },
    {
      label:  "Vendas",
      value:  String(summary?.totalSales ?? 0),
      sub:    (summary?.cancelledCount ?? 0) > 0
        ? `${summary!.cancelledCount} cancelada(s)`
        : "sem cancelamentos",
      subOk:  (summary?.cancelledCount ?? 0) === 0,
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
      {kpis.map((kpi) => (
        <KpiCard key={kpi.label} kpi={kpi} />
      ))}
    </div>
  );
}
