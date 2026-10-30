import { useState } from "react";
import { History, Lock } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Label } from "@/components/ui/label";
import { Textarea } from "@/components/ui/textarea";
import {
  Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle,
} from "@/components/ui/dialog";
import { useFreezeBaseline, useScheduleRevisions } from "../../hooks/use-build-progress";
import { fmtDate, fmtDateTime } from "../../lib/format";

/** Freezes the plan (first time: baseline; afterwards: a revision with a mandatory reason) and lists versions. */
export function ScheduleBaselinePanel({ projectId }: { projectId: string }) {
  const { data: revisions = [], isLoading } = useScheduleRevisions(projectId);
  const freezeMut = useFreezeBaseline(projectId);
  const [open, setOpen]     = useState(false);
  const [reason, setReason] = useState("");

  const hasBaseline = revisions.length > 0;
  const needsReason = hasBaseline;

  const submit = () => {
    if (needsReason && !reason.trim()) return;
    freezeMut.mutate(reason.trim() || undefined, {
      onSuccess: () => {
        toast.success(hasBaseline ? "Revisão do cronograma registrada." : "Linha de base congelada.");
        setOpen(false); setReason("");
      },
      onError: (e) => toast.error(e instanceof Error ? e.message : "Não foi possível congelar o cronograma."),
    });
  };

  const sorted = [...revisions].sort((a, b) => b.number - a.number);

  return (
    <div className="space-y-3 rounded-md border border-border bg-card p-4">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <div>
          <h3 className="text-sm font-semibold">Linha de base do cronograma</h3>
          <p className="text-xs text-muted-foreground">
            {hasBaseline
              ? "O planejado é comparado com a versão congelada mais recente."
              : "Congele o plano atual (datas previstas e pesos) para medir atrasos contra ele."}
          </p>
        </div>
        <Button size="sm" variant={hasBaseline ? "outline" : "default"} onClick={() => setOpen(true)} disabled={isLoading}>
          {hasBaseline ? <History className="mr-1.5 h-3.5 w-3.5" /> : <Lock className="mr-1.5 h-3.5 w-3.5" />}
          {hasBaseline ? "Registrar revisão" : "Congelar baseline"}
        </Button>
      </div>

      {sorted.length > 0 && (
        <ul className="divide-y divide-border rounded-md border border-border">
          {sorted.map((r) => (
            <li key={r.id} className="space-y-0.5 p-3 text-sm">
              <div className="flex flex-wrap items-center justify-between gap-2">
                <span className="font-medium">
                  {r.kind === "Baseline" ? "Linha de base" : `Revisão ${r.number}`}
                </span>
                <span className="text-xs text-muted-foreground">{fmtDateTime(r.createdAt)} · {r.createdByName}</span>
              </div>
              {r.reason && <p className="text-xs text-muted-foreground">Motivo: {r.reason}</p>}
              <p className="text-[11px] text-muted-foreground">
                {r.items.length} etapa{r.items.length !== 1 ? "s" : ""}
                {(() => {
                  const ends = r.items.map((i) => i.plannedEnd).filter((d): d is string => !!d).sort();
                  return ends.length ? ` · término planejado ${fmtDate(ends[ends.length - 1])}` : "";
                })()}
              </p>
            </li>
          ))}
        </ul>
      )}

      <Dialog open={open} onOpenChange={(v) => { if (!v) { setOpen(false); setReason(""); } }}>
        <DialogContent className="sm:max-w-md">
          <DialogHeader>
            <DialogTitle>{hasBaseline ? "Registrar revisão do cronograma" : "Congelar linha de base"}</DialogTitle>
            <DialogDescription>
              Grava as datas previstas e os pesos atuais de todas as etapas. Versões anteriores ficam no histórico.
            </DialogDescription>
          </DialogHeader>
          <div className="space-y-1">
            <Label htmlFor="baseline-reason" className="text-xs">
              Motivo {needsReason ? "*" : "(opcional)"}
            </Label>
            <Textarea id="baseline-reason" value={reason} maxLength={1000} onChange={(e) => setReason(e.target.value)}
              placeholder={needsReason ? "Ex: chuvas atrasaram a fundação; cliente aprovou novo prazo" : "Ex: cronograma aprovado com o cliente"}
              className="min-h-[80px] resize-none text-sm" />
            {needsReason && <p className="text-[11px] text-muted-foreground">Obrigatório nas revisões.</p>}
          </div>
          <DialogFooter>
            <Button variant="outline" onClick={() => { setOpen(false); setReason(""); }}>Cancelar</Button>
            <Button onClick={submit} disabled={freezeMut.isPending || (needsReason && !reason.trim())}>
              {freezeMut.isPending ? "Salvando…" : hasBaseline ? "Registrar revisão" : "Congelar"}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </div>
  );
}
