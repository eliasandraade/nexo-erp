import { useState } from "react";
import { Check, Info, Pencil, Plus, Send, X } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { useMeasurementAction, useMeasurements, type MeasurementAction } from "../../hooks/use-build-commercial";
import type { BuildMeasurementDto } from "../../api/build-commercial.api";
import { MEASUREMENT_STATUS } from "../../lib/commercial";
import { fmtBRL, fmtDate, fmtDateTime } from "../../lib/format";
import { StatusPill } from "../commercial/StatusPill";
import { ConfirmActionDialog } from "../commercial/ConfirmActionDialog";
import { errorMessage } from "../commercial/errors";
import { MeasurementFormDialog } from "./MeasurementFormDialog";

/** "Medições": what was executed in a period, per stage; approving one moves the obra's progress. */
export function MeasurementsTab({ projectId }: { projectId: string }) {
  const { data: list, isLoading, isError } = useMeasurements(projectId);
  const actionMut = useMeasurementAction(projectId);
  const [editing, setEditing] = useState<BuildMeasurementDto | null | undefined>(undefined);
  const [pending, setPending] = useState<{ m: BuildMeasurementDto; action: Exclude<MeasurementAction, "submit"> } | null>(null);

  const submit = (m: BuildMeasurementDto) => actionMut.mutate({ id: m.id, action: "submit" }, {
    onSuccess: () => toast.success(`Medição #${m.number} enviada para aprovação.`),
    onError:   (e) => toast.error(errorMessage(e, "Não foi possível enviar.")),
  });

  const decide = (reason?: string) => {
    if (!pending) return;
    actionMut.mutate({ id: pending.m.id, action: pending.action, reason }, {
      onSuccess: () => {
        toast.success(pending.action === "approve"
          ? "Medição aprovada. O progresso das etapas foi atualizado."
          : "Medição recusada.");
        setPending(null);
      },
      onError: (e) => toast.error(errorMessage(e, "Não foi possível concluir.")),
    });
  };

  return (
    <div className="space-y-4">
      <div className="flex items-start gap-2 rounded-md border border-blue-200 bg-blue-50 p-3 text-xs text-blue-800 dark:border-blue-800 dark:bg-blue-900/20 dark:text-blue-300">
        <Info className="mt-0.5 h-3.5 w-3.5 shrink-0" />
        <p>
          <strong>Aprovar uma medição atualiza o progresso da obra:</strong> cada etapa medida passa a ter o % medido,
          registrado na data de fim do período. Não é preciso lançar o mesmo avanço de novo na aba Etapas.
        </p>
      </div>

      <div className="flex justify-end">
        <Button size="sm" onClick={() => setEditing(null)}><Plus className="mr-1 h-3.5 w-3.5" /> Nova medição</Button>
      </div>

      {isLoading ? (
        <div className="h-24 animate-pulse rounded-md bg-muted" />
      ) : isError ? (
        <p className="rounded-md border border-destructive/20 bg-destructive/5 p-6 text-center text-sm text-destructive">
          Erro ao carregar as medições.
        </p>
      ) : !list?.length ? (
        <p className="rounded-md border border-dashed border-border p-8 text-center text-sm text-muted-foreground">
          Nenhuma medição nesta obra.
        </p>
      ) : (
        <ul className="space-y-3">
          {list.map((m) => {
            const st = MEASUREMENT_STATUS[m.status] ?? { label: m.status, tone: "gray" as const };
            return (
              <li key={m.id} className="space-y-2 rounded-md border border-border bg-card p-3">
                <div className="flex flex-wrap items-start justify-between gap-2">
                  <div>
                    <p className="text-sm font-semibold">Medição #{m.number}</p>
                    <p className="text-xs text-muted-foreground">{fmtDate(m.periodStart)} a {fmtDate(m.periodEnd)}</p>
                  </div>
                  <div className="flex items-center gap-2">
                    <span className="text-sm font-bold tabular-nums">{fmtBRL(m.totalAmount)}</span>
                    <StatusPill label={st.label} tone={st.tone} />
                  </div>
                </div>

                {m.items.length > 0 && (
                  <div className="overflow-x-auto">
                    <table className="w-full min-w-[480px] text-xs">
                      <thead className="text-muted-foreground">
                        <tr>
                          <th className="py-1 text-left font-medium">Etapa</th>
                          <th className="py-1 text-right font-medium">Anterior</th>
                          <th className="py-1 text-right font-medium">Medido</th>
                          <th className="py-1 text-right font-medium">Avanço</th>
                          <th className="py-1 text-right font-medium">Valor</th>
                          <th className="py-1 text-right font-medium">Sugerido</th>
                        </tr>
                      </thead>
                      <tbody className="divide-y divide-border">
                        {m.items.map((i) => (
                          <tr key={i.id}>
                            <td className="py-1">{i.stageName ?? "—"}</td>
                            <td className="py-1 text-right tabular-nums">{i.previousPercent}%</td>
                            <td className="py-1 text-right tabular-nums">{i.measuredPercent}%</td>
                            <td className="py-1 text-right tabular-nums">+{i.deltaPercent}%</td>
                            <td className="py-1 text-right tabular-nums">{fmtBRL(i.amount)}</td>
                            <td className="py-1 text-right tabular-nums text-muted-foreground">{fmtBRL(i.suggestedAmount)}</td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  </div>
                )}
                {m.notes && <p className="whitespace-pre-wrap text-xs text-muted-foreground">{m.notes}</p>}
                {m.approvedAt && (
                  <p className="text-xs text-muted-foreground">Aprovada por {m.approvedByName ?? "—"} em {fmtDateTime(m.approvedAt)}</p>
                )}
                {m.rejectedAt && (
                  <p className="text-xs text-muted-foreground">
                    Recusada por {m.rejectedByName ?? "—"} em {fmtDateTime(m.rejectedAt)}
                    {m.rejectionReason ? ` — “${m.rejectionReason}”` : ""}
                  </p>
                )}

                <div className="flex flex-wrap gap-2">
                  {m.status === "Draft" && (
                    <>
                      <Button size="sm" variant="outline" onClick={() => setEditing(m)}>
                        <Pencil className="mr-1 h-3.5 w-3.5" /> Editar
                      </Button>
                      <Button size="sm" variant="outline" onClick={() => submit(m)} disabled={actionMut.isPending}>
                        <Send className="mr-1 h-3.5 w-3.5" /> Enviar para aprovação
                      </Button>
                    </>
                  )}
                  {m.status === "Submitted" && (
                    <>
                      <Button size="sm" variant="outline"
                        className="border-emerald-200 text-emerald-600 hover:bg-emerald-50 dark:border-emerald-800"
                        onClick={() => setPending({ m, action: "approve" })}>
                        <Check className="mr-1 h-3.5 w-3.5" /> Aprovar
                      </Button>
                      <Button size="sm" variant="outline" className="border-destructive/30 text-destructive hover:bg-destructive/5"
                        onClick={() => setPending({ m, action: "reject" })}>
                        <X className="mr-1 h-3.5 w-3.5" /> Recusar
                      </Button>
                    </>
                  )}
                </div>
              </li>
            );
          })}
        </ul>
      )}

      <MeasurementFormDialog projectId={projectId} measurement={editing} onClose={() => setEditing(undefined)} />
      <ConfirmActionDialog
        open={!!pending}
        title={pending?.action === "approve" ? `Aprovar a medição #${pending.m.number}?` : `Recusar a medição #${pending?.m.number ?? ""}?`}
        description={pending?.action === "approve"
          ? <><p>Cada etapa recebe o % medido e o progresso da obra é atualizado. Se uma etapa já passou do % medido, a aprovação é recusada.</p>
              <p>Uma etapa com não conformidade crítica aberta não chega a 100%: a aprovação também é recusada.</p></>
          : <p>O progresso não muda.</p>}
        confirmLabel={pending?.action === "approve" ? "Aprovar e atualizar o progresso" : "Recusar"}
        destructive={pending?.action === "reject"}
        textLabel={pending?.action === "reject" ? "Motivo (opcional)" : undefined}
        pending={actionMut.isPending}
        onConfirm={decide}
        onClose={() => setPending(null)}
      />
    </div>
  );
}
