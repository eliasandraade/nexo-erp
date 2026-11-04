import { cn } from "@/lib/utils";
import { useCostByStage } from "../../hooks/use-build-progress";
import { fmtBRL } from "../../lib/format";

/** Budgeted × realized × variance per stage (+ "Sem etapa" and totals). Variance = budgeted − realized. */
export function CostByStageSection({ projectId }: { projectId: string }) {
  const { data, isLoading, isError } = useCostByStage(projectId);

  const varianceClass = (v: number) =>
    v < 0 ? "text-red-600 dark:text-red-400" : v > 0 ? "text-emerald-600 dark:text-emerald-400" : "text-muted-foreground";

  return (
    <div className="space-y-2">
      <div>
        <p className="text-xs font-semibold uppercase tracking-wide text-muted-foreground">Custo por etapa</p>
        <p className="text-[11px] text-muted-foreground">
          Orçado vem dos orçamentos aprovados da obra; realizado, das despesas confirmadas. Ligue cada despesa à sua
          etapa na lista abaixo.
        </p>
      </div>
      {isLoading ? (
        <div className="h-24 rounded-md bg-muted animate-pulse" />
      ) : isError || !Array.isArray(data?.stages) ? (
        <p className="rounded-md border border-destructive/20 bg-destructive/5 p-4 text-sm text-destructive">
          Erro ao carregar o custo por etapa.
        </p>
      ) : (
        <div className="overflow-x-auto rounded-md border border-border">
          <table className="w-full text-sm">
            <thead className="bg-muted/40 text-xs text-muted-foreground">
              <tr>
                <th className="p-2 text-left font-medium">Etapa</th>
                <th className="p-2 text-right font-medium">Orçado</th>
                <th className="p-2 text-right font-medium">Realizado</th>
                <th className="p-2 text-right font-medium">Variação</th>
              </tr>
            </thead>
            <tbody>
              {[...data.stages]
                .sort((a, b) => (a.order ?? Number.MAX_SAFE_INTEGER) - (b.order ?? Number.MAX_SAFE_INTEGER))
                .map((s) => (
                  <tr key={s.stageId ?? "none"} className="border-t border-border">
                    <td className="p-2">
                      <span className={cn(!s.stageId && "italic text-muted-foreground")}>
                        {s.stageId ? s.stageName : "Sem etapa"}
                      </span>
                      {s.movementCount > 0 && (
                        <span className="ml-1 text-[11px] text-muted-foreground">
                          ({s.movementCount} lançamento{s.movementCount !== 1 ? "s" : ""})
                        </span>
                      )}
                    </td>
                    <td className="p-2 text-right tabular-nums">{fmtBRL(s.budgeted)}</td>
                    <td className="p-2 text-right tabular-nums">{fmtBRL(s.realized)}</td>
                    <td className={cn("p-2 text-right tabular-nums font-medium", varianceClass(s.variance))}>
                      {fmtBRL(s.variance)}
                    </td>
                  </tr>
                ))}
            </tbody>
            <tfoot className="border-t-2 border-border bg-muted/20 font-semibold">
              <tr>
                <td className="p-2">Total</td>
                <td className="p-2 text-right tabular-nums">{fmtBRL(data.totalBudgeted)}</td>
                <td className="p-2 text-right tabular-nums">{fmtBRL(data.totalRealized)}</td>
                <td className={cn("p-2 text-right tabular-nums", varianceClass(data.totalVariance))}>
                  {fmtBRL(data.totalVariance)}
                </td>
              </tr>
            </tfoot>
          </table>
        </div>
      )}
    </div>
  );
}
