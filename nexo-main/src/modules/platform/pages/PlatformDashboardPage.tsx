import { useNavigate } from "react-router-dom";
import { Building2, Store, Users, Package, Zap, ChevronRight, Activity, TrendingUp, TrendingDown, Minus } from "lucide-react";
import { usePlatformStats, usePlatformHealth } from "../hooks/usePlatformSystem";
import { useMrr, useChurn } from "../hooks/usePlatformTenants";

const MODULE_LABELS: Record<string, string> = {
  varejo:      "Varejo",
  restaurante: "Restaurante",
};

function StatusDot({ status }: { status: string }) {
  const color =
    status === "healthy"   ? "bg-green-500" :
    status === "degraded"  ? "bg-amber-500" :
    "bg-red-500";
  return <span className={`inline-block w-2 h-2 rounded-full ${color} shrink-0`} />;
}

function fmt(n: number) {
  return n.toLocaleString("pt-BR", { minimumFractionDigits: 2, maximumFractionDigits: 2 });
}

export default function PlatformDashboardPage() {
  const navigate = useNavigate();
  const { data: stats, isLoading: statsLoading } = usePlatformStats();
  const { data: health } = usePlatformHealth();
  const { data: mrr } = useMrr();
  const { data: churn } = useChurn(30);

  const stat = (v: number | undefined) => statsLoading ? "—" : (v ?? 0);

  return (
    <div className="p-6 space-y-6 max-w-5xl">

      {/* Header */}
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-xl font-semibold text-foreground">Dashboard</h1>
          <p className="text-sm text-muted-foreground mt-0.5">Visão geral da plataforma Orken</p>
        </div>
        {health && (
          <div className="flex items-center gap-2 text-sm">
            <StatusDot status={health.status} />
            <span className={
              health.status === "healthy" ? "text-green-600" :
              health.status === "degraded" ? "text-amber-600" : "text-red-600"
            }>
              {health.status === "healthy" ? "Sistema operacional" :
               health.status === "degraded" ? "Degradado" : "Instável"}
            </span>
            <button
              onClick={() => navigate("/platform/system")}
              className="text-xs text-muted-foreground hover:text-foreground underline-offset-2 hover:underline ml-1"
            >
              ver detalhes →
            </button>
          </div>
        )}
      </div>

      {/* Stats grid */}
      <div className="grid grid-cols-2 lg:grid-cols-4 gap-4">
        {[
          { label: "Clientes ativos",    value: stat(stats?.activeCount),          icon: Building2, className: "text-green-600"  },
          { label: "Total de clientes",  value: stat(stats?.tenantCount),           icon: Building2, className: "text-muted-foreground" },
          { label: "Lojas / Filiais",    value: stat(stats?.storeCount),            icon: Store,     className: "text-primary"    },
          { label: "Usuários totais",    value: stat(stats?.userCount),             icon: Users,     className: "text-primary"    },
        ].map(({ label, value, icon: Icon, className }) => (
          <div key={label} className="bg-card border border-border rounded-lg p-4">
            <div className="flex items-center justify-between mb-2">
              <p className="text-xs text-muted-foreground">{label}</p>
              <Icon className={`h-4 w-4 ${className}`} />
            </div>
            <p className="text-2xl font-semibold text-foreground">{value}</p>
          </div>
        ))}
      </div>

      {/* Financial KPIs */}
      <div className="grid grid-cols-1 lg:grid-cols-3 gap-4">

        {/* MRR */}
        <div className="bg-card border border-border rounded-lg p-4">
          <div className="flex items-center justify-between mb-1">
            <p className="text-xs text-muted-foreground">MRR</p>
            <TrendingUp className="h-4 w-4 text-primary" />
          </div>
          <p className="text-2xl font-semibold text-foreground">
            {mrr ? `R$ ${fmt(mrr.mrr)}` : "—"}
          </p>
          <p className="text-xs text-muted-foreground mt-1">
            ARR: {mrr ? `R$ ${fmt(mrr.arr)}` : "—"}
          </p>
        </div>

        {/* Paying vs non-paying */}
        <div className="bg-card border border-border rounded-lg p-4">
          <div className="flex items-center justify-between mb-1">
            <p className="text-xs text-muted-foreground">Assinaturas pagas</p>
            <Package className="h-4 w-4 text-primary" />
          </div>
          <p className="text-2xl font-semibold text-foreground">
            {mrr?.payingSubscriptions ?? "—"}
          </p>
          <p className="text-xs text-muted-foreground mt-1">
            {mrr?.nonPayingSubscriptions ?? "—"} admin/trial/lifetime
          </p>
        </div>

        {/* Churn */}
        <div className="bg-card border border-border rounded-lg p-4">
          <div className="flex items-center justify-between mb-1">
            <p className="text-xs text-muted-foreground">Churn (30d)</p>
            {churn
              ? churn.trend > 0 ? <TrendingDown className="h-4 w-4 text-destructive" />
              : churn.trend < 0 ? <TrendingUp className="h-4 w-4 text-green-600" />
              : <Minus className="h-4 w-4 text-muted-foreground" />
              : <Minus className="h-4 w-4 text-muted-foreground" />}
          </div>
          <p className="text-2xl font-semibold text-foreground">
            {churn ? `${churn.churnRate}%` : "—"}
          </p>
          <p className="text-xs text-muted-foreground mt-1">
            {churn ? `${churn.canceledSubscriptions} cancelamentos` : "—"}
            {churn && churn.trend !== 0 && (
              <span className={churn.trend > 0 ? " text-destructive" : " text-green-600"}>
                {" "}({churn.trend > 0 ? "+" : ""}{churn.trend} vs. período anterior)
              </span>
            )}
          </p>
        </div>

      </div>

      <div className="grid grid-cols-1 lg:grid-cols-3 gap-4">

        {/* Módulos ativos */}
        <div className="bg-card border border-border rounded-lg p-4">
          <div className="flex items-center gap-2 mb-4">
            <Package className="h-4 w-4 text-primary" />
            <h2 className="text-sm font-medium text-foreground">Módulos ativos</h2>
            <span className="ml-auto text-xs text-muted-foreground">{stat(stats?.activeSubscriptions)} assinaturas</span>
          </div>
          {stats?.moduleBreakdown?.length === 0 ? (
            <p className="text-xs text-muted-foreground">Nenhum módulo ativo.</p>
          ) : (
            <div className="space-y-2">
              {stats?.moduleBreakdown?.map(({ moduleKey, count }) => (
                <div key={moduleKey} className="flex items-center justify-between">
                  <span className="text-sm text-foreground">{MODULE_LABELS[moduleKey] ?? moduleKey}</span>
                  <div className="flex items-center gap-2">
                    <div
                      className="h-1.5 rounded-full bg-primary/30"
                      style={{ width: `${Math.min(100, count * 8)}px` }}
                    />
                    <span className="text-xs text-muted-foreground tabular-nums">{count}</span>
                  </div>
                </div>
              ))}
            </div>
          )}
        </div>

        {/* Health checks */}
        <div className="bg-card border border-border rounded-lg p-4">
          <div className="flex items-center gap-2 mb-4">
            <Activity className="h-4 w-4 text-primary" />
            <h2 className="text-sm font-medium text-foreground">Saúde do sistema</h2>
          </div>
          {!health ? (
            <p className="text-xs text-muted-foreground">Verificando...</p>
          ) : (
            <div className="space-y-3">
              {health.checks.map((c) => (
                <div key={c.name} className="flex items-center justify-between">
                  <div className="flex items-center gap-2">
                    <StatusDot status={c.status} />
                    <span className="text-sm text-foreground capitalize">{c.name}</span>
                  </div>
                  <span className="text-xs text-muted-foreground tabular-nums">
                    {c.latencyMs > 1 ? `${c.latencyMs}ms` : c.latencyMs === 1 ? "< 1ms" : "—"}
                  </span>
                </div>
              ))}
              <button
                onClick={() => navigate("/platform/system")}
                className="mt-2 text-xs text-primary hover:underline"
              >
                Ver endpoints e logs →
              </button>
            </div>
          )}
        </div>

        {/* Suspensos / inativos */}
        <div className="bg-card border border-border rounded-lg p-4">
          <div className="flex items-center gap-2 mb-4">
            <Zap className="h-4 w-4 text-amber-500" />
            <h2 className="text-sm font-medium text-foreground">Atenção</h2>
          </div>
          <div className="space-y-3 text-sm">
            <div className="flex justify-between">
              <span className="text-muted-foreground">Suspensos</span>
              <span className="font-medium text-amber-600">{stat(stats?.suspendedCount)}</span>
            </div>
            <div className="flex justify-between">
              <span className="text-muted-foreground">Sem módulo</span>
              <span className="font-medium text-foreground">
                {statsLoading ? "—" : ((stats?.tenantCount ?? 0) - (stats?.activeSubscriptions ?? 0) < 0 ? 0 : 0)}
              </span>
            </div>
          </div>
        </div>

      </div>

      {/* Clientes recentes */}
      <div className="bg-card border border-border rounded-lg">
        <div className="px-4 py-3 border-b border-border flex items-center justify-between">
          <h2 className="text-sm font-medium text-foreground">Clientes recentes</h2>
          <button
            onClick={() => navigate("/platform/tenants")}
            className="text-xs text-primary hover:underline"
          >
            Ver todos →
          </button>
        </div>
        {statsLoading ? (
          <div className="p-6 text-center text-sm text-muted-foreground">Carregando...</div>
        ) : (
          <div className="divide-y divide-border">
            {stats?.recentTenants?.map(t => (
              <div
                key={t.id}
                onClick={() => navigate(`/platform/tenants/${t.id}`)}
                className="flex items-center justify-between px-4 py-3 hover:bg-muted/50 cursor-pointer transition-colors"
              >
                <div>
                  <p className="text-sm font-medium text-foreground">{t.tradeName ?? t.companyName}</p>
                  <p className="text-xs text-muted-foreground">{t.email}</p>
                </div>
                <div className="flex items-center gap-3">
                  <span className={`px-1.5 py-0.5 rounded text-[10px] font-medium ${
                    t.status === "Active" ? "bg-green-500/10 text-green-600" : "bg-muted text-muted-foreground"
                  }`}>
                    {t.status === "Active" ? "Ativo" : t.status}
                  </span>
                  <ChevronRight className="h-3.5 w-3.5 text-muted-foreground" />
                </div>
              </div>
            ))}
          </div>
        )}
      </div>

    </div>
  );
}
