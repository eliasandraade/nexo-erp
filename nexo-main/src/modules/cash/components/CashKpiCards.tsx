import { Wallet, TrendingUp, List, AlertTriangle } from "lucide-react";
import { SectionCard } from "@/components/shared/SectionCard";
import type { CashSession } from "../types";
import { formatCurrency } from "@/lib/formatters";

interface CashKpiCardsProps {
  session: CashSession;
  movementsCount: number;
}

interface KpiCardProps {
  icon: React.ElementType;
  label: string;
  value: string;
  highlight?: "positive" | "negative" | "neutral";
}

function KpiCard({ icon: Icon, label, value, highlight = "neutral" }: KpiCardProps) {
  const valueClass =
    highlight === "positive"
      ? "text-green-600 dark:text-green-400"
      : highlight === "negative"
      ? "text-red-600 dark:text-red-400"
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

export function CashKpiCards({ session, movementsCount }: CashKpiCardsProps) {
  const divergenceHighlight =
    session.divergence === undefined
      ? "neutral"
      : session.divergence === 0
      ? "positive"
      : "negative";

  return (
    <div className="grid grid-cols-2 lg:grid-cols-4 gap-4">
      <KpiCard
        icon={Wallet}
        label="Abertura"
        value={formatCurrency(session.openingAmount)}
      />
      <KpiCard
        icon={TrendingUp}
        label="Saldo esperado"
        value={formatCurrency(session.expectedBalance)}
        highlight="positive"
      />
      <KpiCard
        icon={List}
        label="Movimentações"
        value={String(movementsCount)}
      />
      {session.divergence !== undefined ? (
        <KpiCard
          icon={AlertTriangle}
          label="Divergência"
          value={formatCurrency(session.divergence)}
          highlight={divergenceHighlight}
        />
      ) : (
        <KpiCard
          icon={AlertTriangle}
          label="Divergência"
          value="—"
        />
      )}
    </div>
  );
}
