import { Wallet, TrendingUp, List } from "lucide-react";
import { SectionCard } from "@/components/shared/SectionCard";
import { formatCurrency } from "@/lib/formatters";

interface CashKpiCardsProps {
  openingBalance: number;
  expectedBalance: number;
  movementsCount: number;
}

interface KpiCardProps {
  icon: React.ElementType;
  label: string;
  value: string;
  highlight?: "positive" | "neutral";
}

function KpiCard({ icon: Icon, label, value, highlight = "neutral" }: KpiCardProps) {
  const valueClass =
    highlight === "positive"
      ? "text-green-600 dark:text-green-400"
      : "text-foreground";

  return (
    <SectionCard className="flex items-center gap-4">
      <div className="flex h-10 w-10 items-center justify-center rounded-lg bg-muted shrink-0">
        <Icon className="h-5 w-5 text-muted-foreground" />
      </div>
      <div>
        <p className="text-xs text-muted-foreground">{label}</p>
        <p className={`text-lg font-bold ${valueClass}`}>{value}</p>
      </div>
    </SectionCard>
  );
}

export function CashKpiCards({ openingBalance, expectedBalance, movementsCount }: CashKpiCardsProps) {
  return (
    <div className="grid grid-cols-3 gap-4">
      <KpiCard
        icon={Wallet}
        label="Abertura"
        value={formatCurrency(openingBalance)}
      />
      <KpiCard
        icon={TrendingUp}
        label="Saldo esperado"
        value={formatCurrency(expectedBalance)}
        highlight="positive"
      />
      <KpiCard
        icon={List}
        label="Movimentações"
        value={String(movementsCount)}
      />
    </div>
  );
}
