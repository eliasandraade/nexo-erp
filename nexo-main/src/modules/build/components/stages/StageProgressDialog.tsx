import { useEffect, useState } from "react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Textarea } from "@/components/ui/textarea";
import {
  Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle,
} from "@/components/ui/dialog";
import { useUpdateStageProgress } from "../../hooks/use-build";
import type { BuildStageDto } from "../../api/build.api";
import { localToday } from "../../lib/format";

interface Props {
  projectId: string;
  stage: BuildStageDto | null;
  onClose: () => void;
}

/** Updates one stage's % — with an optional note and the day it refers to (history of the progress). */
export function StageProgressDialog({ projectId, stage, onClose }: Props) {
  const progressMut = useUpdateStageProgress(projectId);
  const [pct,  setPct]  = useState(0);
  const [note, setNote] = useState("");
  const [date, setDate] = useState(localToday);

  useEffect(() => {
    if (!stage) return;
    setPct(stage.progressPercent);
    setNote("");
    setDate(localToday());
  }, [stage]);

  const today = localToday();
  const futureDate = !!date && date > today;

  const save = () => {
    if (!stage) return;
    if (futureDate) { toast.error("A data do avanço não pode estar no futuro."); return; }
    progressMut.mutate({
      id: stage.id,
      req: {
        progressPercent: pct,
        note:            note.trim() || undefined,
        effectiveDate:   date && date !== today ? date : undefined,
      },
    }, {
      onSuccess: () => { toast.success("Progresso atualizado!"); onClose(); },
      onError:   (e) => toast.error(e instanceof Error ? e.message : "Erro ao atualizar."),
    });
  };

  return (
    <Dialog open={!!stage} onOpenChange={(v) => !v && onClose()}>
      <DialogContent className="sm:max-w-md">
        <DialogHeader>
          <DialogTitle>Atualizar progresso</DialogTitle>
          <DialogDescription>{stage?.name}</DialogDescription>
        </DialogHeader>

        <div className="space-y-4 py-1">
          <div className="space-y-1">
            <Label htmlFor="stage-progress-pct" className="text-xs">Progresso da etapa</Label>
            <div className="flex items-center gap-3">
              <input
                id="stage-progress-pct"
                type="range" min={0} max={100} value={pct}
                onChange={(e) => setPct(Number(e.target.value))}
                className="flex-1 accent-primary"
              />
              <span className="w-12 text-right text-sm font-semibold tabular-nums">{pct}%</span>
            </div>
          </div>

          <div className="space-y-1">
            <Label htmlFor="stage-progress-date" className="text-xs">Data do avanço</Label>
            <Input id="stage-progress-date" type="date" value={date} max={today}
              onChange={(e) => setDate(e.target.value)} className="text-sm" />
            <p className="text-[11px] text-muted-foreground">
              Dia a que esse avanço se refere. Padrão: hoje.
            </p>
            {futureDate && <p role="alert" className="text-xs text-destructive">A data não pode estar no futuro.</p>}
          </div>

          <div className="space-y-1">
            <Label htmlFor="stage-progress-note" className="text-xs">Observação (opcional)</Label>
            <Textarea id="stage-progress-note" value={note} maxLength={1000}
              onChange={(e) => setNote(e.target.value)} placeholder="Ex: concluída a concretagem da laje"
              className="min-h-[70px] resize-none text-sm" />
            <p className="text-[11px] text-muted-foreground">Fica no histórico interno do avanço.</p>
          </div>
        </div>

        <DialogFooter>
          <Button variant="outline" onClick={onClose}>Cancelar</Button>
          <Button onClick={save} disabled={progressMut.isPending || futureDate}>
            {progressMut.isPending ? "Salvando…" : "Salvar progresso"}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
