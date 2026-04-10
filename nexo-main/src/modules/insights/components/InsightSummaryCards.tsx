import { LayoutList, AlertCircle, AlertTriangle, Info } from "lucide-react";
import { SectionCard } from "@/components/shared/SectionCard";

interface InsightSummaryCardsProps {
  total: number;
  critical: number;
  warning: number;
  info: number;
}

interface StatCardProps {
  icon: React.ElementType;
  label: string;
  value: number;
  accent?: "destructive" | "warning";
}

function StatCard({ icon: Icon, label, value, accent }: StatCardProps) {
  const valueClass =
    accent === "destructive" && value > 0
      ? "text-destructive"
      : accent === "warning" && value > 0
      ? "text-yellow-600"
      : "";

  return (
    <SectionCard>
      <div className="flex items-start justify-between">
        <div>
          <p className="text-xs text-muted-foreground mb-1">{label}</p>
          <p className={`text-2xl font-bold tabular-nums ${valueClass}`}>{value}</p>
        </div>
        <div className="rounded-md bg-muted p-2">
          <Icon className="h-4 w-4 text-muted-foreground" />
        </div>
      </div>
    </SectionCard>
  );
}

export function InsightSummaryCards({
  total,
  critical,
  warning,
  info,
}: InsightSummaryCardsProps) {
  return (
    <div className="grid sm:grid-cols-2 lg:grid-cols-4 gap-4">
      <StatCard icon={LayoutList} label="Total de insights" value={total} />
      <StatCard
        icon={AlertCircle}
        label="Críticos"
        value={critical}
        accent="destructive"
      />
      <StatCard
        icon={AlertTriangle}
        label="Atenções"
        value={warning}
        accent="warning"
      />
      <StatCard icon={Info} label="Informativos" value={info} />
    </div>
  );
}
