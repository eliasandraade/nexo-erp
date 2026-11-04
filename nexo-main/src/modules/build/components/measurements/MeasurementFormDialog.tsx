import { useEffect, useState } from "react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Textarea } from "@/components/ui/textarea";
import {
  Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle,
} from "@/components/ui/dialog";
import { useStages } from "../../hooks/use-build";
import { useCostByStage } from "../../hooks/use-build-progress";
import { useSaveMeasurement } from "../../hooks/use-build-commercial";
import type { BuildMeasurementDto } from "../../api/build-commercial.api";
import { measuredPercentProblem } from "../../lib/commercial";
import { fmtBRL, localToday, parseDecimal } from "../../lib/format";
import { errorMessage } from "../commercial/errors";

interface Row {
  stageId: string;
  name: string;
  previous: number;
  measured: string;
  amount: string;
}

interface Props {
  projectId: string;
  /** null = new; a draft = editing it; undefined = closed. */
  measurement: BuildMeasurementDto | null | undefined;
  onClose: () => void;
}

/** Measurement of a period: per stage, the cumulative % (≥ the current %, ≤ 100) and the amount. */
export function MeasurementFormDialog({ projectId, measurement, onClose }: Props) {
  const open = measurement !== undefined;
  const { data: stages } = useStages(projectId);
  const { data: cost } = useCostByStage(projectId);
  const mut = useSaveMeasurement(projectId);
  const [start, setStart] = useState("");
  const [end, setEnd] = useState("");
  const [notes, setNotes] = useState("");
  const [rows, setRows] = useState<Row[]>([]);

  useEffect(() => {
    if (!open) return;
    const today = localToday();
    setStart(measurement?.periodStart ?? `${today.slice(0, 8)}01`);
    setEnd(measurement?.periodEnd ?? today);
    setNotes(measurement?.notes ?? "");
    setRows([...(stages ?? [])].sort((a, b) => a.order - b.order).map((s) => {
      const item = measurement?.items.find((i) => i.stageId === s.id);
      return {
        stageId: s.id, name: s.name,
        // the stage's current % is the floor of the new cumulative %
        previous: item?.previousPercent ?? s.progressPercent,
        measured: item ? String(item.measuredPercent) : String(s.progressPercent),
        amount: item?.amount != null ? String(item.amount) : "",
      };
    }));
  }, [open, measurement, stages]);

  const budgeted = (stageId: string) => cost?.stages.find((c) => c.stageId === stageId)?.budgeted ?? null;
  const patch = (stageId: string, p: Partial<Row>) => setRows((rs) => rs.map((r) => (r.stageId === stageId ? { ...r, ...p } : r)));

  const problems = rows.map((r) => (r.measured.trim() === "" ? null : measuredPercentProblem(Number(r.measured), r.previous)));
  const hasProblem = problems.some(Boolean);
  const measuredRows = rows.filter((r, i) => !problems[i] && r.measured.trim() !== "" && Number(r.measured) > r.previous);

  const save = () => {
    if (!start || !end || end < start) { toast.error("Período inválido."); return; }
    if (hasProblem) { toast.error("Corrija os percentuais destacados."); return; }
    if (measuredRows.length === 0) { toast.error("Informe o avanço de pelo menos uma etapa."); return; }
    const items = [];
    for (const r of measuredRows) {
      const a = parseDecimal(r.amount);
      if (a != null && (Number.isNaN(a) || a < 0)) { toast.error(`Valor inválido em ${r.name}.`); return; }
      items.push({ stageId: r.stageId, measuredPercent: Number(r.measured), amount: a });
    }
    mut.mutate({
      id: measurement?.id,
      req: { periodStart: start, periodEnd: end, notes: notes.trim() || null, items },
    }, {
      onSuccess: (dto) => { toast.success(measurement ? "Medição atualizada." : `Medição #${dto.number} criada como rascunho.`); onClose(); },
      onError:   (e) => toast.error(errorMessage(e, "Erro ao salvar a medição.")),
    });
  };

  return (
    <Dialog open={open} onOpenChange={(v) => !v && onClose()}>
      <DialogContent className="max-h-[90vh] overflow-y-auto sm:max-w-2xl">
        <DialogHeader>
          <DialogTitle>{measurement ? `Editar medição #${measurement.number}` : "Nova medição"}</DialogTitle>
          <DialogDescription>
            Informe o % acumulado de cada etapa ao fim do período. Etapas sem avanço ficam fora da medição.
          </DialogDescription>
        </DialogHeader>
        <div className="space-y-3">
          <div className="grid grid-cols-2 gap-3">
            <div>
              <Label htmlFor="ms-start" className="text-xs">Início do período</Label>
              <Input id="ms-start" type="date" value={start} onChange={(e) => setStart(e.target.value)} className="mt-0.5" />
            </div>
            <div>
              <Label htmlFor="ms-end" className="text-xs">Fim do período</Label>
              <Input id="ms-end" type="date" value={end} onChange={(e) => setEnd(e.target.value)} className="mt-0.5" />
            </div>
          </div>

          {rows.length === 0 ? (
            <p className="text-sm text-muted-foreground">Cadastre as etapas da obra para medir.</p>
          ) : (
            <div className="overflow-x-auto rounded-md border border-border">
              <table className="w-full min-w-[560px] text-sm">
                <thead className="border-b border-border bg-muted/40 text-xs text-muted-foreground">
                  <tr>
                    <th className="px-3 py-2 text-left font-medium">Etapa</th>
                    <th className="px-3 py-2 text-right font-medium">Atual</th>
                    <th className="px-3 py-2 text-right font-medium">Medido (acumulado)</th>
                    <th className="px-3 py-2 text-right font-medium">Valor sugerido</th>
                    <th className="px-3 py-2 text-right font-medium">Valor (R$)</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-border">
                  {rows.map((r, i) => {
                    const b = budgeted(r.stageId);
                    const delta = Number(r.measured) - r.previous;
                    const suggested = b != null && !problems[i] && delta > 0 ? (delta / 100) * b : null;
                    return (
                      <tr key={r.stageId}>
                        <td className="px-3 py-2">{r.name}</td>
                        <td className="px-3 py-2 text-right tabular-nums">{r.previous}%</td>
                        <td className="px-3 py-2 text-right">
                          <Input aria-label={`% medido — ${r.name}`} value={r.measured} inputMode="numeric"
                            aria-invalid={!!problems[i]}
                            onChange={(e) => patch(r.stageId, { measured: e.target.value })}
                            className={`ml-auto h-8 w-20 text-right ${problems[i] ? "border-destructive" : ""}`} />
                          {problems[i] && <p role="alert" className="mt-0.5 text-[11px] text-destructive">{problems[i]}</p>}
                        </td>
                        <td className="px-3 py-2 text-right tabular-nums text-muted-foreground">
                          {suggested != null ? fmtBRL(suggested) : "—"}
                        </td>
                        <td className="px-3 py-2 text-right">
                          <Input aria-label={`Valor — ${r.name}`} value={r.amount} inputMode="decimal"
                            placeholder={suggested != null ? suggested.toFixed(2).replace(".", ",") : ""}
                            onChange={(e) => patch(r.stageId, { amount: e.target.value })}
                            className="ml-auto h-8 w-28 text-right" />
                        </td>
                      </tr>
                    );
                  })}
                </tbody>
              </table>
            </div>
          )}
          <p className="text-[11px] text-muted-foreground">
            Valor sugerido = avanço do período × valor orçado da etapa no orçamento aprovado. O valor informado é opcional.
          </p>
          <div>
            <Label htmlFor="ms-notes" className="text-xs">Observações</Label>
            <Textarea id="ms-notes" value={notes} onChange={(e) => setNotes(e.target.value)} rows={2} className="mt-0.5" />
          </div>
        </div>
        <DialogFooter>
          <Button variant="outline" onClick={onClose}>Cancelar</Button>
          <Button onClick={save} disabled={mut.isPending || hasProblem}>Salvar rascunho</Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
