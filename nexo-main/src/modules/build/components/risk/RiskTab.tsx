import { useState } from "react";
import { AlertTriangle, Calculator, Sparkles } from "lucide-react";
import { toast } from "sonner";
import { cn } from "@/lib/utils";
import { Button } from "@/components/ui/button";
import { Skeleton } from "@/components/ui/skeleton";
import type { BuildRiskAnalysisDto, BuildRiskKind } from "../../api/build-ai.api";
import { useActiveSuggestion, useAiGate, useExplainRisk, useRiskAnalysis } from "../../hooks/use-build-ai";
import { aiErrorMessage, readRiskAnalysis, readRiskExplanation } from "../../lib/ai";
import { fmtBRL, fmtDate } from "../../lib/format";
import { AiDisabledNotice, AiErrorAlert } from "../ai/common";
import { AiSuggestionCard } from "../ai/AiSuggestionCard";

type Fmt = "ratio" | "pct" | "int" | "brl" | "days";

const METRICS: Record<BuildRiskKind, Array<{ key: string; label: string; fmt: Fmt; hint?: string }>> = {
  delay: [
    { key: "spi", label: "SPI (prazo)", fmt: "ratio", hint: "real ÷ planejado; < 1 = atrás do plano" },
    { key: "actualPercent", label: "Real", fmt: "pct" },
    { key: "plannedPercent", label: "Planejado para hoje", fmt: "pct" },
    { key: "forecastSlipDays", label: "Previsão vs término contratado", fmt: "days" },
    { key: "delayedStages", label: "Etapas atrasadas", fmt: "int" },
    { key: "lateMaterials", label: "Materiais atrasados", fmt: "int" },
    { key: "pendingChangeOrders", label: "Aditivos pendentes (prazo)", fmt: "int" },
    { key: "openCriticalNonConformities", label: "NCs críticas abertas", fmt: "int" },
  ],
  cost: [
    { key: "cpi", label: "CPI (custo)", fmt: "ratio", hint: "valor agregado ÷ realizado; < 1 = gastando mais que o avanço" },
    { key: "budget", label: "Orçamento", fmt: "brl" },
    { key: "realized", label: "Realizado", fmt: "brl" },
    { key: "earnedValue", label: "Valor agregado", fmt: "brl" },
    { key: "eac", label: "Custo final estimado (EAC)", fmt: "brl", hint: "orçamento ÷ CPI" },
    { key: "vac", label: "Variação no término (VAC)", fmt: "brl", hint: "orçamento − EAC" },
    { key: "approvedChangeOrders", label: "Aditivos aprovados", fmt: "brl" },
    { key: "budgetWithChangeOrders", label: "Orçamento com aditivos", fmt: "brl" },
    { key: "unlinkedMaterialsEstimated", label: "Materiais pedidos sem lançamento (estim.)", fmt: "brl" },
  ],
};

const LEVEL: Record<string, { label: string; cls: string }> = {
  none:   { label: "Sem risco", cls: "bg-emerald-100 text-emerald-700 dark:bg-emerald-900/30 dark:text-emerald-400" },
  low:    { label: "Baixo",     cls: "bg-sky-100 text-sky-700 dark:bg-sky-900/30 dark:text-sky-300" },
  medium: { label: "Médio",     cls: "bg-amber-100 text-amber-800 dark:bg-amber-900/30 dark:text-amber-300" },
  high:   { label: "Alto",      cls: "bg-rose-100 text-rose-700 dark:bg-rose-900/30 dark:text-rose-300" },
};
const SEVERITY_ORDER: Record<string, number> = { high: 0, medium: 1, low: 2, none: 3 };

function fmtMetric(v: number | null | undefined, fmt: Fmt): string {
  if (v == null || !Number.isFinite(v)) return "—";
  switch (fmt) {
    case "ratio": return v.toLocaleString("pt-BR", { minimumFractionDigits: 2, maximumFractionDigits: 2 });
    case "pct":   return `${v.toLocaleString("pt-BR", { maximumFractionDigits: 1 })}%`;
    case "brl":   return fmtBRL(v);
    case "days":  return v > 0 ? `+${v} dia${v === 1 ? "" : "s"}` : v < 0 ? `${v} dias` : "no prazo";
    default:      return v.toLocaleString("pt-BR", { maximumFractionDigits: 0 });
  }
}

function LevelPill({ level }: { level: string }) {
  const m = LEVEL[level] ?? LEVEL.none;
  return <span className={cn("rounded-full px-2 py-0.5 text-[11px] font-medium", m.cls)}>{m.label}</span>;
}

function RiskExplanationView({ output, analysis }: { output: unknown; analysis: BuildRiskAnalysisDto | null }) {
  const x = readRiskExplanation(output);
  const titleOf = (key: string) => analysis?.items.find((i) => i.key === key)?.title ?? key;
  return (
    <div className="space-y-2 text-sm" data-testid="risk-explanation">
      {x.summary && <p className="whitespace-pre-line">{x.summary}</p>}
      {x.items.length > 0 && (
        <ul className="space-y-1">
          {x.items.map((i) => (
            <li key={i.key} className="rounded bg-muted/40 px-2 py-1">
              <p className="text-xs font-semibold">{titleOf(i.key)}</p>
              <p className="text-xs text-foreground/80">{i.explanation}</p>
            </li>
          ))}
        </ul>
      )}
      {x.recommendedActions.length > 0 && (
        <div>
          <p className="text-xs font-semibold">Ações recomendadas</p>
          <ul className="list-disc pl-5 text-xs">{x.recommendedActions.map((a, i) => <li key={i}>{a.text}</li>)}</ul>
        </div>
      )}
      <p className="text-[11px] text-muted-foreground">A IA só explica os itens calculados acima; ela não cria números nem itens novos.</p>
    </div>
  );
}

function RiskCard({ projectId, kind }: { projectId: string; kind: BuildRiskKind }) {
  const { data, isLoading, isError } = useRiskAnalysis(projectId, kind);
  const gate = useAiGate();
  const explain = useExplainRisk(projectId, kind);
  const { suggestion, setCurrent } = useActiveSuggestion(projectId, kind === "cost" ? "CostRisk" : "DelayRisk");
  const [error, setError] = useState<string | null>(null);
  const analysis = readRiskAnalysis(data);
  const title = kind === "cost" ? "Risco de custo" : "Risco de prazo";

  const items = [...(analysis?.items ?? [])].sort((a, b) => (SEVERITY_ORDER[a.severity] ?? 9) - (SEVERITY_ORDER[b.severity] ?? 9));

  return (
    <section data-testid={`risk-${kind}`} aria-label={title} className="min-w-0 space-y-3 rounded-md border border-border bg-card p-3 sm:p-4">
      <div className="flex flex-wrap items-start justify-between gap-2">
        <div className="space-y-0.5">
          <h3 className="flex items-center gap-1.5 text-sm font-semibold">
            <Calculator className="h-4 w-4 text-muted-foreground" aria-hidden /> {title}
            {analysis && <LevelPill level={analysis.level} />}
          </h3>
          <p className="text-xs text-muted-foreground">
            Números da análise determinística do Orken{analysis?.methodology ? ` (${analysis.methodology})` : ""} — calculados, sem IA
            {analysis?.asOf ? `, em ${fmtDate(analysis.asOf)}` : ""}.
          </p>
        </div>
        {(!suggestion || suggestion.status !== "Pending") && (
          <Button size="sm" variant="outline" disabled={!gate.aiEnabled || !analysis || explain.isPending}
            onClick={() => {
              setError(null);
              explain.mutate(undefined, { onSuccess: (s) => setCurrent(s), onError: (e) => setError(aiErrorMessage(e, "Não foi possível explicar.")) });
            }}>
            <Sparkles className="mr-1 h-3.5 w-3.5" /> {explain.isPending ? "Explicando…" : "Explicar com IA"}
          </Button>
        )}
      </div>
      {!gate.loading && !gate.aiEnabled && <AiDisabledNotice />}

      {isLoading && <Skeleton className="h-24 w-full" />}
      {isError && <p role="alert" className="text-sm text-destructive">Não foi possível calcular a análise.</p>}
      {!isLoading && !isError && !analysis && <p className="text-sm text-muted-foreground">Análise indisponível.</p>}

      {analysis && (
        <>
          <dl className="grid grid-cols-2 gap-2 lg:grid-cols-4" data-testid={`risk-${kind}-metrics`}>
            {METRICS[kind].map((m) => (
              <div key={m.key} className="rounded-md border border-border p-2">
                <dt className="text-[11px] text-muted-foreground" title={m.hint}>{m.label}</dt>
                <dd className="text-base font-semibold tabular-nums">{fmtMetric(analysis.metrics[m.key], m.fmt)}</dd>
              </div>
            ))}
          </dl>
          {items.length === 0 ? (
            <p className="text-sm text-muted-foreground">Nenhum risco identificado pela análise.</p>
          ) : (
            <ul className="space-y-1.5">
              {items.map((i) => (
                <li key={i.key} className="space-y-0.5 rounded-md border border-border p-2">
                  <div className="flex flex-wrap items-center gap-2">
                    <AlertTriangle className={cn("h-3.5 w-3.5", i.severity === "high" ? "text-rose-600" : i.severity === "medium" ? "text-amber-600" : "text-sky-600")} aria-hidden />
                    <LevelPill level={i.severity} />
                    <span className="text-sm font-medium">{i.title}</span>
                  </div>
                  {i.evidence && <p className="text-xs text-muted-foreground">{i.evidence}</p>}
                  {Object.keys(i.metrics).length > 0 && (
                    <p className="flex flex-wrap gap-1 text-[11px]">
                      {Object.entries(i.metrics).map(([k, v]) => (
                        <code key={k} className="rounded bg-muted px-1">{k}: {v == null ? "—" : v.toLocaleString("pt-BR", { maximumFractionDigits: 2 })}</code>
                      ))}
                    </p>
                  )}
                </li>
              ))}
            </ul>
          )}
        </>
      )}

      <AiErrorAlert message={error} />
      {suggestion && (
        <>
          <AiSuggestionCard suggestion={suggestion} acceptLabel="Registrar ciência"
            acceptHint="Aceitar só registra que você leu a explicação — nada muda na obra."
            onDecided={(s) => { setCurrent(s); toast.success(s.status === "Rejected" ? "Explicação recusada." : "Ciência registrada."); }}>
            <RiskExplanationView output={suggestion.output} analysis={analysis} />
          </AiSuggestionCard>
          {suggestion.status !== "Pending" && (
            <div className="flex justify-end"><Button size="sm" variant="ghost" onClick={() => setCurrent(null)}>Fechar</Button></div>
          )}
        </>
      )}
    </section>
  );
}

/** "Riscos e previsões": deterministic delay and cost risk (always available), optionally explained by the AI. */
export function RiskTab({ projectId }: { projectId: string }) {
  return (
    <div className="space-y-4">
      <p className="text-xs text-muted-foreground">
        Os indicadores abaixo são calculados pelo Orken a partir do cronograma, do progresso, dos custos, materiais, aditivos e
        qualidade. A IA, quando habilitada, apenas explica esses números e sugere ações.
      </p>
      <RiskCard projectId={projectId} kind="delay" />
      <RiskCard projectId={projectId} kind="cost" />
    </div>
  );
}
