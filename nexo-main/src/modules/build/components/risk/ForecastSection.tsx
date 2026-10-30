import { CalendarClock, RefreshCw, TrendingUp } from "lucide-react";
import { toast } from "sonner";
import { cn } from "@/lib/utils";
import { Button } from "@/components/ui/button";
import { Skeleton } from "@/components/ui/skeleton";
import type { BuildForecastDto, BuildForecastHistoryItemDto } from "../../api/build-forecast.api";
import { isPredictDisabled, useForecast, useForecastHistory, useRecomputeForecast } from "../../hooks/use-build-forecast";
import {
  FACTOR_LABEL, PREDICT_DISABLED_MESSAGE, bySeverity, confidenceLevel, deliverySparkline, fmtConfidence, fmtDelay, fmtRate, isRiskOnlyFactor, methodLabel,
  severityMeta,
} from "../../lib/forecast";
import { fmtDate, fmtDateTime, fmtPercent } from "../../lib/format";
import { errorMessage } from "../commercial/errors";

const SOURCE_LABEL: Record<string, string> = {
  Scheduled: "automática (diária)",
  Manual:    "recalculada manualmente",
  OnTheFly:  "calculada agora, ainda não gravada",
};

function Pill({ severity }: { severity: string }) {
  const m = severityMeta(severity);
  return <span className={cn("rounded-full px-2 py-0.5 text-[11px] font-medium", m.cls)}>{m.label}</span>;
}

function DelayBadge({ days, label }: { days: number | null; label: string }) {
  if (days == null) return null;
  const late = days > 0;
  return (
    <span className={cn("rounded-full px-2 py-0.5 text-[11px] font-medium",
      late ? "bg-rose-100 text-rose-700 dark:bg-rose-900/30 dark:text-rose-300"
        : "bg-emerald-100 text-emerald-700 dark:bg-emerald-900/30 dark:text-emerald-400")}>
      {fmtDelay(days)} {label}
    </span>
  );
}

function Kpis({ f }: { f: BuildForecastDto }) {
  const conf = f.confidence ?? 0;
  return (
    <dl className="grid grid-cols-2 gap-2 lg:grid-cols-4" data-testid="forecast-kpis">
      <div className="rounded-md border border-border p-2">
        <dt className="text-[11px] text-muted-foreground">Entrega prevista</dt>
        <dd className="text-base font-semibold">{fmtDate(f.forecastDelivery)}</dd>
      </div>
      <div className="rounded-md border border-border p-2">
        <dt className="text-[11px] text-muted-foreground">Fim planejado das etapas</dt>
        <dd className="text-base font-semibold">{fmtDate(f.plannedDelivery)}</dd>
        <dd><DelayBadge days={f.delayDays} label="vs. plano" /></dd>
      </div>
      <div className="rounded-md border border-border p-2">
        <dt className="text-[11px] text-muted-foreground">Término contratado</dt>
        <dd className="text-base font-semibold">{fmtDate(f.expectedEnd)}</dd>
        <dd><DelayBadge days={f.daysLateVsContract} label="vs. contrato" /></dd>
      </div>
      <div className="rounded-md border border-border p-2">
        <dt className="text-[11px] text-muted-foreground">Confiança</dt>
        <dd className="text-base font-semibold tabular-nums">{fmtConfidence(conf)} <span className="text-xs font-normal text-muted-foreground">({confidenceLevel(conf)})</span></dd>
      </div>
    </dl>
  );
}

function Factors({ f }: { f: BuildForecastDto }) {
  const factors = [...(f.factors ?? [])].sort(bySeverity);
  if (factors.length === 0) return <p className="text-sm text-muted-foreground">Nenhum fator de atraso identificado.</p>;
  return (
    <ul className="space-y-1.5" data-testid="forecast-factors">
      {factors.map((x) => (
        <li key={x.key} className="space-y-0.5 rounded-md border border-border p-2">
          <div className="flex flex-wrap items-center gap-2">
            <Pill severity={x.severity} />
            <span className="text-sm font-medium">{x.title || FACTOR_LABEL[x.kind] || x.kind}</span>
            {isRiskOnlyFactor(x.kind) && (
              <span className="rounded-full border border-border px-2 py-0.5 text-[11px] text-muted-foreground">
                Risco — não aplicado às datas
              </span>
            )}
          </div>
          {x.evidence && <p className="break-words text-xs text-muted-foreground">{x.evidence}</p>}
        </li>
      ))}
    </ul>
  );
}

function StagesTable({ f }: { f: BuildForecastDto }) {
  const stages = [...(f.stages ?? [])].sort((a, b) => a.order - b.order);
  if (stages.length === 0) return <p className="text-sm text-muted-foreground">A obra ainda não tem etapas.</p>;
  const windowDays = f.calendar?.windowDays ?? 21;
  return (
    <div className="max-w-full overflow-x-auto rounded-md border border-border" data-testid="forecast-stages">
      <table className="w-full min-w-[640px] text-xs">
        <thead className="bg-muted/40 text-left text-[11px] text-muted-foreground">
          <tr>
            <th className="px-2 py-1.5 font-medium">Etapa</th>
            <th className="px-2 py-1.5 font-medium">Real</th>
            <th className="px-2 py-1.5 font-medium">Ritmo (real · plano)</th>
            <th className="px-2 py-1.5 font-medium">Fim planejado</th>
            <th className="px-2 py-1.5 font-medium">Fim previsto</th>
            <th className="px-2 py-1.5 font-medium">Atraso</th>
            <th className="px-2 py-1.5 font-medium">Confiança</th>
          </tr>
        </thead>
        <tbody className="divide-y divide-border">
          {stages.map((s) => (
            <tr key={s.stageId}>
              <td className="px-2 py-1.5">
                <p className="font-medium">{s.name}</p>
                <p className="text-[11px] text-muted-foreground">{methodLabel(s.method, windowDays)}{s.dataPoints > 0 ? ` · ${s.dataPoints} registro(s)` : ""}</p>
              </td>
              <td className="px-2 py-1.5 tabular-nums">{fmtPercent(s.actualPercent)}</td>
              <td className="px-2 py-1.5 tabular-nums">{fmtRate(s.ratePerWorkingDay)} · {fmtRate(s.plannedRatePerWorkingDay)}</td>
              <td className="px-2 py-1.5">{fmtDate(s.plannedEnd)}</td>
              <td className="px-2 py-1.5">{fmtDate(s.forecastEnd)}</td>
              <td className={cn("px-2 py-1.5 tabular-nums", (s.delayDays ?? 0) > 0 && "text-rose-600 dark:text-rose-400")}>
                {fmtDelay(s.delayDays)}
                {s.inheritedDelayDays > 0 && <span className="block text-[11px] text-muted-foreground">{s.inheritedDelayDays} herdado(s)</span>}
              </td>
              <td className="px-2 py-1.5 tabular-nums">{fmtConfidence(s.confidence)}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

function History({ items }: { items: BuildForecastHistoryItemDto[] }) {
  const line = deliverySparkline(items, 160, 32);
  return (
    <div className="space-y-2" data-testid="forecast-history">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <h4 className="text-xs font-semibold">Histórico das previsões</h4>
        {line && (
          <svg viewBox="0 0 160 32" className="h-8 w-40 text-primary" role="img" aria-label="Evolução da entrega prevista">
            <polyline points={line} fill="none" stroke="currentColor" strokeWidth={1.5} />
          </svg>
        )}
      </div>
      <ul className="divide-y divide-border rounded-md border border-border">
        {items.slice(0, 10).map((h) => (
          <li key={h.id} className="flex flex-wrap items-center justify-between gap-x-3 gap-y-0.5 px-2 py-1.5 text-xs">
            <span className="font-medium">{fmtDate(h.date)}</span>
            <span>Entrega {fmtDate(h.forecastDelivery)}</span>
            <span className="tabular-nums">{fmtDelay(h.delayDays)}</span>
            <span className="tabular-nums text-muted-foreground">confiança {fmtConfidence(h.confidence)}</span>
            <span className="text-muted-foreground">{SOURCE_LABEL[h.source] ?? h.source}{h.createdByName ? ` · ${h.createdByName}` : ""}</span>
          </li>
        ))}
      </ul>
    </div>
  );
}

/** "Previsão de entrega": the deterministic productivity/1 forecast, its factors, the stages and the stored history. */
export function ForecastSection({ projectId }: { projectId: string }) {
  const { data, isLoading, error } = useForecast(projectId);
  const disabled = isPredictDisabled(error);
  const history = useForecastHistory(projectId);
  const recompute = useRecomputeForecast(projectId);
  const f = data && typeof data === "object" && !Array.isArray(data) ? data : null;
  const historyItems = Array.isArray(history.data) ? history.data : [];

  return (
    <section data-testid="forecast-section" aria-label="Previsão de entrega" className="min-w-0 space-y-3 rounded-md border border-border bg-card p-3 sm:p-4">
      <div className="flex flex-wrap items-start justify-between gap-2">
        <div className="space-y-0.5">
          <h3 className="flex items-center gap-1.5 text-sm font-semibold">
            <CalendarClock className="h-4 w-4 text-muted-foreground" aria-hidden /> Previsão de entrega
          </h3>
          <p className="text-xs text-muted-foreground">
            Cálculo determinístico do Orken, sem IA: projeta o fim de cada etapa pelo ritmo real de avanço (em dias úteis) e a entrega
            é o maior fim previsto — mesmos dados, mesma previsão.
          </p>
        </div>
        {!disabled && (
          <Button size="sm" variant="outline" disabled={recompute.isPending || isLoading}
            onClick={() => recompute.mutate(undefined, {
              onSuccess: () => toast.success("Previsão recalculada."),
              onError: (e) => toast.error(isPredictDisabled(e) ? PREDICT_DISABLED_MESSAGE : errorMessage(e, "Não foi possível recalcular.")),
            })}>
            <RefreshCw className={cn("mr-1 h-3.5 w-3.5", recompute.isPending && "animate-spin")} />
            {recompute.isPending ? "Recalculando…" : "Recalcular"}
          </Button>
        )}
      </div>

      {isLoading && <Skeleton className="h-24 w-full" />}
      {disabled && <p role="status" className="rounded-md bg-muted/50 p-3 text-sm text-muted-foreground">{PREDICT_DISABLED_MESSAGE}</p>}
      {!!error && !disabled && <p role="alert" className="text-sm text-destructive">Não foi possível calcular a previsão.</p>}

      {f && (
        <>
          <Kpis f={f} />
          <p className="text-[11px] text-muted-foreground">
            Metodologia {f.methodology} (versão {f.version}) · {SOURCE_LABEL[f.source] ?? f.source} em {fmtDateTime(f.generatedAt)}
            {f.createdByName ? ` por ${f.createdByName}` : ""}
            {f.calendar ? ` · dias úteis ${f.calendar.workingDays.join(", ")}; ${f.calendar.holidays}; janela de ${f.calendar.windowDays} dias` : ""}.
          </p>
          <div className="space-y-1.5">
            <h4 className="flex items-center gap-1.5 text-xs font-semibold"><TrendingUp className="h-3.5 w-3.5" aria-hidden /> Fatores</h4>
            <p className="text-[11px] text-muted-foreground">Aditivos pendentes e clima são riscos: aparecem aqui, mas não mudam as datas.</p>
            <Factors f={f} />
          </div>
          <div className="space-y-1.5">
            <h4 className="text-xs font-semibold">Produtividade e previsão por etapa</h4>
            <StagesTable f={f} />
          </div>
        </>
      )}

      {historyItems.length > 0 && <History items={historyItems} />}
    </section>
  );
}
