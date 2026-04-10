import { DollarSign, TrendingUp, Receipt, AlertTriangle } from "lucide-react";
import { useQuery } from "@tanstack/react-query";
import { Skeleton } from "@/components/ui/skeleton";
import { reportService } from "@/modules/reports/services/reportService";
import { inventoryService } from "@/modules/inventory/services/inventoryService";
import { formatCurrency } from "@/lib/formatters";

interface KpiDef {
  label: string;
  value: string;
  sub: string;
  subType: "positive" | "warning";
  icon: React.ElementType;
  iconBg: string;
  iconColor: string;
}

export function KpiCards() {
  const { data: operational, isLoading: loadingOp } = useQuery({
    queryKey: ["dashboard-operational"],
    queryFn: () => reportService.getOperationalSummary(),
  });

  const { data: alerts = [], isLoading: loadingAlerts } = useQuery({
    queryKey: ["dashboard-alerts"],
    queryFn: () => inventoryService.listAlerts(),
  });

  const isLoading = loadingOp || loadingAlerts;

  if (isLoading) {
    return (
      <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4">
        {Array.from({ length: 4 }).map((_, i) => (
          <Skeleton key={i} className="h-24 rounded-lg" />
        ))}
      </div>
    );
  }

  const kpis: KpiDef[] = [
    {
      label: "Faturamento",
      value: formatCurrency(operational?.totalRevenue ?? 0),
      sub: `${operational?.totalSales ?? 0} venda(s) no período`,
      subType: "positive",
      icon: DollarSign,
      iconBg: "bg-secondary/10",
      iconColor: "text-secondary",
    },
    {
      label: "Ticket médio",
      value: formatCurrency(operational?.averageTicket ?? 0),
      sub: "por venda ativa",
      subType: "positive",
      icon: TrendingUp,
      iconBg: "bg-success/10",
      iconColor: "text-success",
    },
    {
      label: "Vendas registradas",
      value: String(operational?.totalSales ?? 0),
      sub: `${operational?.cancelledCount ?? 0} cancelada(s)`,
      subType: (operational?.cancelledCount ?? 0) > 0 ? "warning" : "positive",
      icon: Receipt,
      iconBg: "bg-primary/10",
      iconColor: "text-primary",
    },
    {
      label: "Itens em alerta",
      value: String(alerts.length),
      sub: alerts.length > 0 ? "requerem atenção" : "estoque normal",
      subType: alerts.length > 0 ? "warning" : "positive",
      icon: AlertTriangle,
      iconBg: "bg-warning/10",
      iconColor: "text-warning",
    },
  ];

  return (
    <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4">
      {kpis.map((kpi, i) => (
        <div
          key={kpi.label}
          className="bg-card rounded-lg border border-border p-5 shadow-sm animate-fade-in"
          style={{ animationDelay: `${i * 75}ms` }}
        >
          <div className="flex items-start justify-between">
            <div>
              <p className="text-xs font-medium text-muted-foreground uppercase tracking-wide">
                {kpi.label}
              </p>
              <p className="text-2xl font-bold text-foreground mt-1">{kpi.value}</p>
            </div>
            <div className={`w-9 h-9 rounded-lg ${kpi.iconBg} flex items-center justify-center`}>
              <kpi.icon className={`h-4 w-4 ${kpi.iconColor}`} />
            </div>
          </div>
          <p
            className={`text-xs mt-3 font-medium ${
              kpi.subType === "warning" ? "text-warning" : "text-success"
            }`}
          >
            {kpi.sub}
          </p>
        </div>
      ))}
    </div>
  );
}
