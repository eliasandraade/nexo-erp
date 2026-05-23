import { DollarSign, AlertTriangle, TrendingUp } from "lucide-react";
import { useAiCosts } from "../../hooks/useAiOperations";

function microToDisplay(micros: number): string {
  const cents = micros / 10_000;
  if (cents < 1) return `< $0.01`;
  return `$${(cents / 100).toFixed(2)}`;
}

function centsToDisplay(cents: number): string {
  return `$${(cents / 100).toFixed(2)}`;
}

function UsageBar({ spent, soft, hard }: { spent: number; soft: number | null; hard: number | null }) {
  const limit = hard ?? soft ?? null;
  if (!limit) return <span className="text-xs text-muted-foreground">Sem limite</span>;

  const pct = Math.min(100, (spent / limit) * 100);
  const isOver = pct >= 100;
  const isWarn = pct >= 80;

  return (
    <div className="flex items-center gap-2">
      <div className="h-1.5 w-20 rounded-full bg-muted overflow-hidden">
        <div
          className={`h-full rounded-full transition-all ${
            isOver ? "bg-red-500" : isWarn ? "bg-amber-500" : "bg-green-500"
          }`}
          style={{ width: `${pct}%` }}
        />
      </div>
      <span className="text-xs tabular-nums text-muted-foreground">
        {Math.round(pct)}%
      </span>
    </div>
  );
}

export default function AiCostsPage() {
  const { data: costs, isLoading } = useAiCosts();

  const totals = costs?.reduce(
    (acc, c) => ({
      requests:            acc.requests + c.requests,
      inputTokens:         acc.inputTokens + c.inputTokens,
      outputTokens:        acc.outputTokens + c.outputTokens,
      estimatedCostMicros: acc.estimatedCostMicros + c.estimatedCostMicros,
    }),
    { requests: 0, inputTokens: 0, outputTokens: 0, estimatedCostMicros: 0 }
  );

  return (
    <div className="p-6 space-y-5 max-w-5xl">

      <div>
        <h1 className="font-display text-[20px] font-bold text-foreground tracking-tight">Controle de Custos</h1>
        <p className="text-sm text-muted-foreground mt-0.5">
          Consumo de tokens e custo estimado por tenant. Mês atual.
        </p>
      </div>

      {/* Summary KPIs */}
      {!isLoading && totals && (
        <div className="grid grid-cols-2 lg:grid-cols-4 gap-4">
          {[
            { label: "Requests",       value: totals.requests.toLocaleString("pt-BR"),        icon: TrendingUp  },
            { label: "Input Tokens",   value: (totals.inputTokens  / 1000).toFixed(1) + "K",  icon: TrendingUp  },
            { label: "Output Tokens",  value: (totals.outputTokens / 1000).toFixed(1) + "K",  icon: TrendingUp  },
            { label: "Custo estimado", value: microToDisplay(totals.estimatedCostMicros),      icon: DollarSign  },
          ].map(({ label, value, icon: Icon }) => (
            <div key={label} className="bg-card border border-border rounded-lg p-4">
              <div className="flex items-center justify-between mb-2">
                <p className="text-xs text-muted-foreground">{label}</p>
                <Icon className="h-4 w-4 text-primary" />
              </div>
              <p className="text-xl font-semibold text-foreground tabular-nums">{value}</p>
            </div>
          ))}
        </div>
      )}

      {/* Notice */}
      <div className="flex items-start gap-3 bg-blue-50 border border-blue-200 rounded-lg px-4 py-3">
        <DollarSign className="h-4 w-4 text-blue-600 shrink-0 mt-0.5" />
        <div>
          <p className="text-xs font-medium text-blue-800">
            Custo estimado — providers LLM ainda não ativos.
          </p>
          <p className="text-xs text-blue-700 mt-0.5">
            Atualmente 100% do processamento é RuleBased (zero custo de tokens).
            Os valores abaixo são projeções para quando Claude/OpenAI forem ativados.
          </p>
        </div>
      </div>

      {/* Per-tenant table */}
      <div className="bg-card border border-border rounded-lg overflow-hidden">
        <div className="px-4 py-3 border-b border-border bg-muted/30">
          <h2 className="text-sm font-medium text-foreground">Consumo por tenant</h2>
        </div>

        {isLoading ? (
          <div className="p-8 text-center text-sm text-muted-foreground">Carregando...</div>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full text-sm">
              <thead>
                <tr className="border-b border-border bg-muted/20">
                  {["Tenant","Requests","Input Tok","Output Tok","Custo Est.","Soft Limit","Hard Limit","Uso"].map(h => (
                    <th key={h} className="text-left px-4 py-2 text-xs text-muted-foreground font-medium whitespace-nowrap">
                      {h}
                    </th>
                  ))}
                </tr>
              </thead>
              <tbody className="divide-y divide-border">
                {costs?.map(c => {
                  const overSoft = c.softLimitCents && c.spentCents >= c.softLimitCents;
                  const overHard = c.hardLimitCents && c.spentCents >= c.hardLimitCents;
                  return (
                    <tr key={c.tenantId} className={overHard ? "bg-red-50/50" : overSoft ? "bg-amber-50/50" : ""}>
                      <td className="px-4 py-3">
                        <div className="flex items-center gap-2">
                          {(overSoft || overHard) && (
                            <AlertTriangle className={`h-3.5 w-3.5 shrink-0 ${overHard ? "text-red-500" : "text-amber-500"}`} />
                          )}
                          <span className="text-sm text-foreground font-medium">{c.tenantName}</span>
                        </div>
                      </td>
                      <td className="px-4 py-3 text-xs tabular-nums text-muted-foreground">{c.requests}</td>
                      <td className="px-4 py-3 text-xs tabular-nums text-muted-foreground">{(c.inputTokens / 1000).toFixed(1)}K</td>
                      <td className="px-4 py-3 text-xs tabular-nums text-muted-foreground">{(c.outputTokens / 1000).toFixed(1)}K</td>
                      <td className="px-4 py-3 text-xs tabular-nums text-foreground font-medium">
                        {microToDisplay(c.estimatedCostMicros)}
                      </td>
                      <td className="px-4 py-3 text-xs tabular-nums text-muted-foreground">
                        {c.softLimitCents ? centsToDisplay(c.softLimitCents) : "—"}
                      </td>
                      <td className="px-4 py-3 text-xs tabular-nums text-muted-foreground">
                        {c.hardLimitCents ? centsToDisplay(c.hardLimitCents) : "—"}
                      </td>
                      <td className="px-4 py-3">
                        <UsageBar spent={c.spentCents} soft={c.softLimitCents} hard={c.hardLimitCents} />
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        )}
      </div>
    </div>
  );
}
