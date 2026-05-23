import { Brain, Zap, CheckCircle2, RotateCcw, TrendingUp, Users, Activity } from "lucide-react";
import {
  AreaChart, Area, XAxis, YAxis, CartesianGrid, Tooltip, ResponsiveContainer, Legend,
} from "recharts";
import { useAiDashboard } from "../../hooks/useAiOperations";

function Kpi({
  label, value, sub, icon: Icon, accent,
}: {
  label: string; value: string; sub?: string;
  icon: React.ElementType; accent: string;
}) {
  return (
    <div className="bg-card border border-border rounded-lg p-4">
      <div className="flex items-center justify-between mb-2">
        <p className="text-xs text-muted-foreground">{label}</p>
        <Icon className={`h-4 w-4 ${accent}`} />
      </div>
      <p className="text-2xl font-semibold text-foreground tabular-nums">{value}</p>
      {sub && <p className="text-xs text-muted-foreground mt-1">{sub}</p>}
    </div>
  );
}

function pct(n: number) {
  return `${Math.round(n * 100)}%`;
}

export default function AiDashboardPage() {
  const { data, isLoading } = useAiDashboard();

  const dash = data ?? {
    requestsToday:      0, requestsLast7d:     0,
    ruleBasedPct:       0, claudePct:          0, openAiPct: 0,
    avgConfidence:      0, acceptanceRate:     0,
    correctionRate:     0, reprocessRate:      0,
    activeTenantsCount: 0, requestsByDay:      [],
    topCorrectedFields: [], topTenants:         [],
  };

  return (
    <div className="p-6 space-y-6 max-w-6xl">

      {/* Header */}
      <div className="flex items-center justify-between">
        <div>
          <h1 className="font-display text-[20px] font-bold text-foreground tracking-tight">AI Operations — Dashboard</h1>
          <p className="text-sm text-muted-foreground mt-0.5">
            Visão operacional do Interpretation Engine
          </p>
        </div>
        {isLoading && (
          <p className="text-xs text-muted-foreground animate-pulse">Carregando...</p>
        )}
      </div>

      {/* KPIs */}
      <div className="grid grid-cols-2 lg:grid-cols-4 gap-4">
        <Kpi label="Requests hoje"       value={isLoading ? "—" : String(dash.requestsToday)}   sub={`${dash.requestsLast7d} nos últimos 7d`}  icon={Activity}     accent="text-primary" />
        <Kpi label="Confidence média"    value={isLoading ? "—" : pct(dash.avgConfidence)}       sub="campos AutoFilled"                          icon={Brain}        accent="text-indigo-500" />
        <Kpi label="Acceptance rate"     value={isLoading ? "—" : pct(dash.acceptanceRate)}      sub="sugestões aceitas sem correção"             icon={CheckCircle2} accent="text-green-500" />
        <Kpi label="Correction rate"     value={isLoading ? "—" : pct(dash.correctionRate)}      sub={`Reprocess: ${pct(dash.reprocessRate)}`}    icon={RotateCcw}    accent="text-amber-500" />
      </div>

      {/* Provider distribution + active tenants */}
      <div className="grid grid-cols-1 lg:grid-cols-4 gap-4">
        {/* Analyzer split */}
        <div className="lg:col-span-3 bg-card border border-border rounded-lg p-4">
          <div className="flex items-center gap-2 mb-1">
            <Zap className="h-4 w-4 text-primary" />
            <h2 className="text-sm font-medium text-foreground">Requests por dia — RuleBased vs LLM</h2>
          </div>
          <p className="text-xs text-muted-foreground mb-4">Últimos 14 dias</p>
          {isLoading ? (
            <div className="h-48 flex items-center justify-center text-sm text-muted-foreground">
              Carregando...
            </div>
          ) : (
            <ResponsiveContainer width="100%" height={200}>
              <AreaChart data={dash.requestsByDay} margin={{ top: 0, right: 0, left: -20, bottom: 0 }}>
                <defs>
                  <linearGradient id="gradRb" x1="0" y1="0" x2="0" y2="1">
                    <stop offset="5%"  stopColor="hsl(217,91%,60%)" stopOpacity={0.25} />
                    <stop offset="95%" stopColor="hsl(217,91%,60%)" stopOpacity={0} />
                  </linearGradient>
                  <linearGradient id="gradLlm" x1="0" y1="0" x2="0" y2="1">
                    <stop offset="5%"  stopColor="hsl(262,83%,58%)" stopOpacity={0.25} />
                    <stop offset="95%" stopColor="hsl(262,83%,58%)" stopOpacity={0} />
                  </linearGradient>
                </defs>
                <CartesianGrid strokeDasharray="3 3" stroke="hsl(214,32%,91%)" vertical={false} />
                <XAxis dataKey="date" tick={{ fontSize: 10 }} tickFormatter={d => d.slice(5)} />
                <YAxis tick={{ fontSize: 10 }} />
                <Tooltip
                  contentStyle={{ fontSize: 12, borderRadius: 6 }}
                  formatter={(v, name) => [v, name === "ruleBased" ? "RuleBased" : "LLM"]}
                  labelFormatter={d => `Dia: ${d}`}
                />
                <Legend formatter={v => v === "ruleBased" ? "RuleBased" : "LLM"} wrapperStyle={{ fontSize: 11 }} />
                <Area type="monotone" dataKey="ruleBased" stroke="hsl(217,91%,60%)" fill="url(#gradRb)"  strokeWidth={2} />
                <Area type="monotone" dataKey="llm"       stroke="hsl(262,83%,58%)" fill="url(#gradLlm)" strokeWidth={2} />
              </AreaChart>
            </ResponsiveContainer>
          )}
        </div>

        {/* Provider % + active tenants */}
        <div className="space-y-4">
          <div className="bg-card border border-border rounded-lg p-4">
            <div className="flex items-center gap-2 mb-3">
              <TrendingUp className="h-4 w-4 text-primary" />
              <h2 className="text-sm font-medium">Providers</h2>
            </div>
            <div className="space-y-2.5">
              {[
                { label: "RuleBased", pct: dash.ruleBasedPct, color: "bg-blue-500" },
                { label: "Claude",    pct: dash.claudePct,    color: "bg-violet-500" },
                { label: "OpenAI",    pct: dash.openAiPct,    color: "bg-green-500" },
              ].map(({ label, pct: p, color }) => (
                <div key={label}>
                  <div className="flex justify-between text-xs mb-1">
                    <span className="text-muted-foreground">{label}</span>
                    <span className="font-medium tabular-nums">{isLoading ? "—" : `${p}%`}</span>
                  </div>
                  <div className="h-1.5 rounded-full bg-muted overflow-hidden">
                    <div className={`h-full rounded-full ${color}`} style={{ width: `${p}%` }} />
                  </div>
                </div>
              ))}
            </div>
          </div>

          <div className="bg-card border border-border rounded-lg p-4">
            <div className="flex items-center justify-between mb-1">
              <div className="flex items-center gap-2">
                <Users className="h-4 w-4 text-primary" />
                <p className="text-xs text-muted-foreground">Tenants ativos</p>
              </div>
            </div>
            <p className="text-2xl font-semibold text-foreground tabular-nums">
              {isLoading ? "—" : dash.activeTenantsCount}
            </p>
            <p className="text-xs text-muted-foreground mt-1">com requests neste período</p>
          </div>
        </div>
      </div>

      {/* Bottom tables */}
      <div className="grid grid-cols-1 lg:grid-cols-2 gap-4">

        {/* Top corrected fields */}
        <div className="bg-card border border-border rounded-lg">
          <div className="px-4 py-3 border-b border-border">
            <h2 className="text-sm font-medium text-foreground">Campos mais corrigidos</h2>
            <p className="text-xs text-muted-foreground mt-0.5">
              Onde a interpretação diverge do usuário
            </p>
          </div>
          <div className="divide-y divide-border">
            {isLoading ? (
              <div className="p-4 text-sm text-center text-muted-foreground">Carregando...</div>
            ) : dash.topCorrectedFields.map(({ field, count }) => (
              <div key={field} className="flex items-center justify-between px-4 py-2.5">
                <span className="text-sm text-foreground font-mono text-xs bg-muted px-1.5 py-0.5 rounded">
                  {field}
                </span>
                <div className="flex items-center gap-3">
                  <div
                    className="h-1.5 rounded-full bg-amber-400"
                    style={{ width: `${Math.min(80, count * 2)}px` }}
                  />
                  <span className="text-xs text-muted-foreground tabular-nums w-6 text-right">
                    {count}
                  </span>
                </div>
              </div>
            ))}
          </div>
        </div>

        {/* Top tenants */}
        <div className="bg-card border border-border rounded-lg">
          <div className="px-4 py-3 border-b border-border">
            <h2 className="text-sm font-medium text-foreground">Top tenants por consumo</h2>
            <p className="text-xs text-muted-foreground mt-0.5">Requests no período</p>
          </div>
          <div className="divide-y divide-border">
            {isLoading ? (
              <div className="p-4 text-sm text-center text-muted-foreground">Carregando...</div>
            ) : dash.topTenants.map(({ tenantName, requests }, idx) => (
              <div key={tenantName} className="flex items-center gap-3 px-4 py-2.5">
                <span className="text-xs text-muted-foreground tabular-nums w-4">{idx + 1}</span>
                <span className="text-sm text-foreground flex-1 truncate">{tenantName}</span>
                <div className="flex items-center gap-2">
                  <div
                    className="h-1.5 rounded-full bg-primary/50"
                    style={{ width: `${Math.min(80, requests)}px` }}
                  />
                  <span className="text-xs text-muted-foreground tabular-nums w-8 text-right">
                    {requests}
                  </span>
                </div>
              </div>
            ))}
          </div>
        </div>

      </div>
    </div>
  );
}
