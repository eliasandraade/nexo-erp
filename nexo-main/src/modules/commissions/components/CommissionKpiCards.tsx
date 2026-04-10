import { TrendingUp, TrendingDown, Users, ShoppingBag } from "lucide-react";
import { SectionCard } from "@/components/shared/SectionCard";
import { formatCurrency } from "@/lib/formatters";

interface CommissionKpiCardsProps {
  totalActive: number;
  totalReversed: number;
  sellersWithCommission: number;
  impactedSalesCount: number;
}

interface KpiCardProps {
  icon: React.ElementType;
  label: string;
  value: string;
  valueClassName?: string;
}

function KpiCard({ icon: Icon, label, value, valueClassName }: KpiCardProps) {
  return (
    <SectionCard>
      <div className="flex items-start justify-between">
        <div>
          <p className="text-xs text-muted-foreground mb-1">{label}</p>
          <p className={`text-2xl font-bold tabular-nums ${valueClassName ?? ""}`}>
            {value}
          </p>
        </div>
        <div className="rounded-md bg-muted p-2">
          <Icon className="h-4 w-4 text-muted-foreground" />
        </div>
      </div>
    </SectionCard>
  );
}

export function CommissionKpiCards({
  totalActive,
  totalReversed,
  sellersWithCommission,
  impactedSalesCount,
}: CommissionKpiCardsProps) {
  return (
    <div className="grid sm:grid-cols-2 lg:grid-cols-4 gap-4">
      <KpiCard
        icon={TrendingUp}
        label="Comissão ativa"
        value={formatCurrency(totalActive)}
        valueClassName="text-foreground"
      />
      <KpiCard
        icon={TrendingDown}
        label="Comissão estornada"
        value={formatCurrency(totalReversed)}
        valueClassName="text-muted-foreground"
      />
      <KpiCard
        icon={Users}
        label="Vendedores"
        value={String(sellersWithCommission)}
      />
      <KpiCard
        icon={ShoppingBag}
        label="Vendas com comissão"
        value={String(impactedSalesCount)}
      />
    </div>
  );
}
