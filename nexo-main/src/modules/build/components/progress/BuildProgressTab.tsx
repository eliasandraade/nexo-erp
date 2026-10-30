import { lazy, Suspense } from "react";
import { AlertTriangle, Info } from "lucide-react";
import { cn } from "@/lib/utils";
import { Skeleton } from "@/components/ui/skeleton";
import { useProjectProgress } from "../../hooks/use-build-progress";
import type { BuildPlanSource, BuildProgressDto } from "../../api/build-progress.api";
import { fmtDate, fmtPercent } from "../../lib/format";
import { ScheduleBaselinePanel } from "./ScheduleBaselinePanel";
import { ProgressAtDate } from "./ProgressAtDate";

const ProgressCurvesChart = lazy(() => import("./ProgressCurvesChart"));

const METHODOLOGY_LABEL: Record<string, string> = {
  "weighted-stages/1": "Média ponderada das etapas pelo peso (v1)",
};

const FLAG_LABEL: Record<string, string> = {
  missing_planned_dates: "sem datas planejadas",
};

const STATUS_LABEL: Record<string, string> = {
  Pending: "Pendente", InProgress: "Em andamento", Completed: "Concluída",
};

function planSourceLabel(source: BuildPlanSource, revision: number | null): string {
  if (source === "baseline") return "Linha de base";
  if (source === "revision") return revision != null ? `Revisão ${revision}` : "Revisão";
  return "Plano atual (sem linha de base congelada)";
}

const fmtWeight = (w: number) => w.toLocaleString("pt-BR", { maximumFractionDigits: 2 });

function Kpi({ label, value, hint, accent }: { label: string; value: string; hint?: string; accent?: boolean }) {
  return (
    <div className="rounded-md border border-border bg-card p-4">
      <p className="text-xs text-muted-foreground">{label}</p>
      <p className={cn("text-2xl font-bold tabular-nums", accent && "text-primary")}>{value}</p>
      {hint && <p className="mt-0.5 text-[11px] text-muted-foreground">{hint}</p>}
    </div>
  );
}

function ProgressSummary({ progress }: { progress: BuildProgressDto }) {
  const late = progress.plannedDelivery && progress.forecastDelivery && progress.forecastDelivery > progress.plannedDelivery;
  return (
    <div className="grid grid-cols-2 gap-3 lg:grid-cols-4">
      <Kpi label="Real" value={fmtPercent(progress.actualPercent)} accent hint="Calculado pelas etapas" />
      <Kpi label="Planejado" value={fmtPercent(progress.plannedPercent)} hint={`Para hoje (${fmtDate(progress.date)})`} />
      <Kpi label="Previsto" value={fmtPercent(progress.forecastPercent)} />
      <Kpi
        label="Entrega prevista"
        value={fmtDate(progress.forecastDelivery)}
        hint={progress.plannedDelivery
          ? `${late ? "Atrasada · " : ""}planejada para ${fmtDate(progress.plannedDelivery)}`
          : undefined}
      />
    </div>
  );
}

export function BuildProgressTab({ projectId }: { projectId: string }) {
  const { data: progress, isLoading, isError } = useProjectProgress(projectId);

  if (isLoading) {
    return (
      <div className="space-y-3">
        {[1, 2, 3].map((i) => <div key={i} className="h-20 rounded-md bg-muted animate-pulse" />)}
      </div>
    );
  }

  if (isError || !progress) {
    return (
      <div className="rounded-md border border-destructive/20 bg-destructive/5 p-8 text-center">
        <p className="text-sm text-destructive">Erro ao carregar o progresso da obra.</p>
      </div>
    );
  }

  const stages = [...progress.stages].sort((a, b) => a.order - b.order);
  const hasCurves = progress.curves.planned.length + progress.curves.actual.length + progress.curves.forecast.length > 0;

  return (
    <div className="space-y-5">
      <ProgressSummary progress={progress} />

      <div className="flex items-start gap-2 rounded-md border border-border bg-muted/30 p-3 text-xs text-muted-foreground">
        <Info className="mt-0.5 h-3.5 w-3.5 shrink-0" />
        <p>
          O % geral não é digitado: é a média das etapas ponderada pelo peso de cada uma.
          {" "}Metodologia: <span className="font-medium text-foreground">{METHODOLOGY_LABEL[progress.methodology] ?? progress.methodology}</span>
          {" "}· Plano de referência: <span className="font-medium text-foreground">{planSourceLabel(progress.planSource, progress.planRevisionNumber)}</span>
        </p>
      </div>

      {progress.warnings.length > 0 && (
        <ul className="space-y-1 rounded-md border border-amber-200 bg-amber-50 p-3 text-xs text-amber-800 dark:border-amber-800 dark:bg-amber-900/20 dark:text-amber-300">
          {progress.warnings.map((w) => (
            <li key={w} className="flex items-start gap-1.5">
              <AlertTriangle className="mt-0.5 h-3.5 w-3.5 shrink-0" /> {w}
            </li>
          ))}
        </ul>
      )}

      <div className="rounded-md border border-border bg-card p-4">
        <h3 className="mb-2 text-sm font-semibold">Curvas de avanço</h3>
        {hasCurves ? (
          <Suspense fallback={<Skeleton className="h-[260px] w-full" />}>
            <ProgressCurvesChart curves={progress.curves} />
          </Suspense>
        ) : (
          <p className="text-sm text-muted-foreground">As curvas aparecem quando as etapas tiverem datas e avanço registrado.</p>
        )}
      </div>

      <div className="space-y-2">
        <h3 className="text-sm font-semibold">Por etapa</h3>
        {stages.length === 0 ? (
          <p className="rounded-md border border-dashed border-border p-6 text-center text-sm text-muted-foreground">
            Cadastre etapas para acompanhar o progresso.
          </p>
        ) : (
          <div className="overflow-x-auto rounded-md border border-border">
            <table className="w-full text-sm">
              <thead className="bg-muted/40 text-xs text-muted-foreground">
                <tr>
                  <th className="p-2 text-left font-medium">Etapa</th>
                  <th className="p-2 text-right font-medium">Peso</th>
                  <th className="p-2 text-right font-medium">Planejado</th>
                  <th className="p-2 text-right font-medium">Real</th>
                  <th className="p-2 text-left font-medium">Fim previsto</th>
                </tr>
              </thead>
              <tbody>
                {stages.map((s) => (
                  <tr key={s.stageId} className="border-t border-border align-top">
                    <td className="p-2">
                      <p className="font-medium">{s.name}</p>
                      <p className="text-[11px] text-muted-foreground">{STATUS_LABEL[s.status] ?? s.status}</p>
                      {s.flags.length > 0 && (
                        <div className="mt-1 flex flex-wrap gap-1">
                          {s.flags.map((f) => (
                            <span key={f} className="rounded-full bg-amber-100 px-2 py-0.5 text-[10px] font-medium text-amber-800 dark:bg-amber-900/30 dark:text-amber-300">
                              {FLAG_LABEL[f] ?? f}
                            </span>
                          ))}
                        </div>
                      )}
                    </td>
                    <td className="p-2 text-right tabular-nums">{fmtWeight(s.weight)}</td>
                    <td className="p-2 text-right tabular-nums">{fmtPercent(s.plannedPercent)}</td>
                    <td className="p-2 text-right tabular-nums font-medium">{fmtPercent(s.actualPercent)}</td>
                    <td className="p-2">
                      <p>{fmtDate(s.forecastEnd)}</p>
                      {s.delayDays != null && s.delayDays > 0 && (
                        <p className="text-[11px] text-red-600 dark:text-red-400">{s.delayDays} dia{s.delayDays !== 1 ? "s" : ""} de atraso</p>
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>

      <ScheduleBaselinePanel projectId={projectId} />
      <ProgressAtDate projectId={projectId} />
    </div>
  );
}
