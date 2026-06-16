import { useNavigate } from "react-router-dom";
import {
  Hammer, AlertTriangle, DollarSign, Wallet, TrendingUp, Activity,
} from "lucide-react";
import { cn } from "@/lib/utils";
import { useBuildDashboard } from "../hooks/use-build";

function fmt(v: number | null | undefined): string {
  if (v == null) return "—";
  return v.toLocaleString("pt-BR", { style: "currency", currency: "BRL", maximumFractionDigits: 0 });
}

function fmtDate(d: string | null | undefined): string {
  if (!d) return "—";
  return new Date(d + "T12:00:00").toLocaleDateString("pt-BR", { day: "2-digit", month: "short" });
}

/**
 * Real Build dashboard fed by GET /v1/build/dashboard — no client-side estimates.
 * Hidden when the tenant has no projects (the list's empty state handles onboarding).
 */
export function BuildDashboardSection() {
  const navigate = useNavigate();
  const { data, isLoading, isError } = useBuildDashboard();

  if (isLoading) {
    return (
      <div className="grid grid-cols-2 sm:grid-cols-3 lg:grid-cols-6 gap-3">
        {[...Array(6)].map((_, i) => (
          <div key={i} className="rounded-xl border border-border bg-card p-3 h-20 animate-pulse bg-muted/30" />
        ))}
      </div>
    );
  }

  if (isError) {
    return (
      <div className="rounded-xl border border-destructive/20 bg-destructive/5 p-4 text-center">
        <p className="text-sm text-destructive">Não foi possível carregar o painel.</p>
      </div>
    );
  }

  if (!data || data.totalProjects === 0) return null;

  const kpis = [
    { icon: Hammer,        label: "Em andamento", value: String(data.inProgressCount), tone: "text-blue-600 dark:text-blue-400" },
    { icon: AlertTriangle, label: "Atrasadas",    value: String(data.overdueCount),
      tone: data.overdueCount > 0 ? "text-red-600 dark:text-red-400" : "text-muted-foreground" },
    { icon: DollarSign,    label: "Previsto",     value: fmt(data.totalApproved || data.totalEstimated), tone: "text-foreground" },
    { icon: Wallet,        label: "Realizado",    value: fmt(data.totalRealized), tone: "text-foreground" },
    { icon: TrendingUp,    label: "Saldo",        value: fmt(data.balance),
      tone: data.balance < 0 ? "text-red-600 dark:text-red-400" : "text-emerald-600 dark:text-emerald-400" },
    { icon: Activity,      label: "Progresso médio", value: `${data.avgStageProgress.toFixed(0)}%`, tone: "text-foreground" },
  ];

  return (
    <div className="space-y-3">
      <div className="grid grid-cols-2 sm:grid-cols-3 lg:grid-cols-6 gap-3">
        {kpis.map(({ icon: Icon, label, value, tone }) => (
          <div key={label} className="rounded-xl border border-border bg-card p-3">
            <div className="flex items-center gap-1.5 mb-1">
              <Icon className="h-3.5 w-3.5 text-muted-foreground" />
              <p className="text-[11px] text-muted-foreground uppercase tracking-wide truncate">{label}</p>
            </div>
            <p className={cn("text-lg font-bold tabular-nums", tone)}>{value}</p>
          </div>
        ))}
      </div>

      {data.recentExpenses.length > 0 && (
        <div className="rounded-xl border border-border bg-card p-4">
          <p className="text-xs font-semibold uppercase tracking-wide text-muted-foreground mb-2">
            Despesas recentes
          </p>
          <div className="divide-y divide-border">
            {data.recentExpenses.map((e, i) => (
              <button
                key={`${e.projectId}-${i}`}
                onClick={() => navigate(`/build/projetos/${e.projectId}`)}
                className="w-full flex items-center justify-between gap-3 py-2 text-left hover:bg-muted/20 -mx-1 px-1 rounded transition-colors"
              >
                <div className="min-w-0">
                  <p className="text-sm font-medium truncate">{e.description}</p>
                  <p className="text-xs text-muted-foreground truncate">
                    {e.projectName} · {fmtDate(e.date)}
                  </p>
                </div>
                <p className="text-sm font-bold tabular-nums text-red-600 dark:text-red-400 shrink-0">
                  -{fmt(e.amount)}
                </p>
              </button>
            ))}
          </div>
        </div>
      )}
    </div>
  );
}
