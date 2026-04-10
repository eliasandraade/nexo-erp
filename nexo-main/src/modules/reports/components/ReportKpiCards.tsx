import { Receipt, TrendingUp, Ticket, Percent, XCircle, AlertTriangle } from "lucide-react";
import { SectionCard } from "@/components/shared/SectionCard";
import { formatCurrency } from "@/lib/formatters";

interface ReportKpiCardsProps {
  totalSales: number;
  totalRevenue: number;
  averageTicket: number;
  activeCommission: number;
  cancelledCount: number;
  stockAlerts: number;
}


interface KpiCardProps {
  icon: React.ElementType;
  label: string;
  value: string;
  sub?: string;
  accent?: boolean;
}

function KpiCard({ icon: Icon, label, value, sub, accent }: KpiCardProps) {
  return (
    <SectionCard>
      <div className="flex items-start justify-between">
        <div>
          <p className="text-xs text-muted-foreground mb-1">{label}</p>
          <p className={`text-2xl font-bold tabular-nums ${accent ? "text-destructive" : ""}`}>
            {value}
          </p>
          {sub && <p className="text-xs text-muted-foreground mt-0.5">{sub}</p>}
        </div>
        <div className="rounded-md bg-muted p-2">
          <Icon className="h-4 w-4 text-muted-foreground" />
        </div>
      </div>
    </SectionCard>
  );
}

export function ReportKpiCards({
  totalSales,
  totalRevenue,
  averageTicket,
  activeCommission,
  cancelledCount,
  stockAlerts,
}: ReportKpiCardsProps) {
  return (
    <div className="grid sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-6 gap-4">
      <KpiCard icon={Receipt} label="Vendas" value={String(totalSales)} />
      <KpiCard icon={TrendingUp} label="Faturamento" value={formatCurrency(totalRevenue)} />
      <KpiCard icon={Ticket} label="Ticket médio" value={formatCurrency(averageTicket)} />
      <KpiCard icon={Percent} label="Comissão ativa" value={formatCurrency(activeCommission)} />
      <KpiCard
        icon={XCircle}
        label="Cancelamentos"
        value={String(cancelledCount)}
        accent={cancelledCount > 0}
      />
      <KpiCard
        icon={AlertTriangle}
        label="Alertas de estoque"
        value={String(stockAlerts)}
        accent={stockAlerts > 0}
      />
    </div>
  );
}
