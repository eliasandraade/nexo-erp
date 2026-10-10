import { Skeleton } from "@/components/ui/skeleton";
import { useDashboardSummary } from "@/modules/dashboard/hooks/useDashboardSummary";
import { formatCurrency } from "@/lib/formatters";
import { cn } from "@/lib/utils";


interface KpiDef {
  label:  string;
  value:  string;
  sub:    string;
  subOk:  boolean;
}

function KpiCard({ kpi }: { kpi: KpiDef }) {
    return <div className="metric-cell">
      <p className="text-sm text-muted-foreground">{kpi.label}</p>
      <p className="mt-2 break-words text-2xl font-semibold tracking-tight tabular-nums">{kpi.value}</p>
      <p className={cn("mt-2 text-xs", kpi.subOk ? "text-muted-foreground" : "text-warning")}>{kpi.sub}</p>
    </div>;
  }

export function KpiCards() {
  const { data: summary, isLoading, isError } = useDashboardSummary();

  if (isLoading) {
    return (
      <div className="metric-strip">
        {Array.from({ length: 4 }).map((_, i) => (
          <Skeleton key={i} className="h-[106px] rounded-md" />
        ))}
      </div>
    );
  }

  if (isError || !summary) return null;

  const alertCount = (summary?.zeroStockCount ?? 0) + (summary?.lowStockCount ?? 0);

  const kpis: KpiDef[] = [
    {
      label:  "Faturamento",
      value:  formatCurrency(summary?.totalRevenue ?? 0),
      sub:    `${summary?.totalSales ?? 0} venda(s) no período`,
      subOk:  true,
    },
    {
      label:  "Ticket médio",
      value:  formatCurrency(summary?.averageTicket ?? 0),
      sub:    "por venda ativa",
      subOk:  true,
    },
    {
      label:  "Vendas",
      value:  String(summary?.totalSales ?? 0),
      sub:    (summary?.cancelledCount ?? 0) > 0
        ? `${summary!.cancelledCount} cancelada(s)`
        : "sem cancelamentos",
      subOk:  (summary?.cancelledCount ?? 0) === 0,
    },
    {
      label:  "Alerta estoque",
      value:  String(alertCount),
      sub:    alertCount > 0 ? "itens requerem atenção" : "nenhum alerta informado",
      subOk:  alertCount === 0,
    },
  ];

  return (
    <div className="metric-strip">
      {kpis.map((kpi) => (
        <KpiCard key={kpi.label} kpi={kpi} />
      ))}
    </div>
  );
}
