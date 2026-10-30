import { useState } from "react";
import { AlertTriangle, ArrowLeft, Ban, CheckCircle2, Play } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Label } from "@/components/ui/label";
import { Textarea } from "@/components/ui/textarea";
import { useInspection, useInspectionAction, type InspectionAction } from "../../hooks/use-build-quality";
import type { RecordBuildInspectionItemRequest } from "../../api/build-quality.api";
import {
  INSPECTION_STATUS, canComplete, inspectionProblems, previewOutcome,
} from "../../lib/quality";
import { fmtDate, fmtDateTime } from "../../lib/format";
import { StatusPill } from "../commercial/StatusPill";
import { errorMessage } from "../commercial/errors";
import { InspectionItemCard } from "./InspectionItemCard";

interface Props {
  projectId: string;
  inspectionId: string;
  onBack: () => void;
}

/** Runs an inspection (quality or the client walkthrough) item by item, then completes it with the computed outcome. */
export function InspectionRunner({ projectId, inspectionId, onBack }: Props) {
  const { data: insp, isLoading, isError } = useInspection(inspectionId);
  const action = useInspectionAction(projectId);
  const [notes, setNotes] = useState("");
  const [failure, setFailure] = useState<string | null>(null);

  const run = (a: InspectionAction, ok?: string) =>
    action.mutateAsync({ id: inspectionId, action: a }).then(
      (r) => { setFailure(null); if (ok) toast.success(ok); return r; },
      (e) => {
        const msg = errorMessage(e, "Não foi possível salvar.");
        setFailure(msg);
        toast.error(msg);
        throw e;
      },
    );

  const back = (
    <Button variant="ghost" size="sm" onClick={onBack}><ArrowLeft className="mr-1 h-3.5 w-3.5" /> Voltar</Button>
  );

  if (isLoading) return <div className="space-y-3">{back}<div className="h-40 animate-pulse rounded-md bg-muted" /></div>;
  if (isError || !insp) {
    return (
      <div className="space-y-3">
        {back}
        <p className="rounded-md border border-destructive/20 bg-destructive/5 p-6 text-center text-sm text-destructive">
          Erro ao carregar a vistoria.
        </p>
      </div>
    );
  }

  const items = Array.isArray(insp.items) ? [...insp.items].sort((a, b) => a.order - b.order) : [];
  const open = insp.status === "Planned" || insp.status === "InProgress";
  const st = INSPECTION_STATUS[insp.status] ?? { label: insp.status, tone: "gray" as const };
  const problems = inspectionProblems(items);
  const ready = canComplete(problems);
  const outcome = previewOutcome(items);
  const outcomeInfo = INSPECTION_STATUS[outcome.status];
  const walkthrough = insp.kind === "ClientWalkthrough";

  const complete = () => {
    if (!ready) return;
    void run({ kind: "complete", notes: notes.trim() || undefined }, "Vistoria concluída.").catch(() => undefined);
  };

  const cancel = () => {
    if (!confirm(`Cancelar a vistoria “${insp.title}”?`)) return;
    void run({ kind: "cancel" }, "Vistoria cancelada.").catch(() => undefined);
  };

  return (
    <div className="space-y-4 pb-24 sm:pb-4">
      <div className="flex flex-wrap items-start justify-between gap-2">
        <div className="min-w-0 space-y-1">
          {back}
          <h3 className="text-base font-semibold">{insp.title}</h3>
          <p className="text-xs text-muted-foreground">
            {walkthrough ? "Vistoria de entrega com o cliente" : "Vistoria de qualidade"}
            {insp.stageName ? ` · ${insp.stageName}` : ""}
            {insp.scheduledFor ? ` · ${fmtDate(insp.scheduledFor)}` : ""}
            {insp.inspectorName ? ` · ${insp.inspectorName}` : ""}
          </p>
        </div>
        <StatusPill label={st.label} tone={st.tone} />
      </div>

      {insp.status === "Planned" && (
        <Button size="sm" onClick={() => void run({ kind: "start" }, "Vistoria iniciada.").catch(() => undefined)}
          disabled={action.isPending}>
          <Play className="mr-1 h-3.5 w-3.5" /> Iniciar vistoria
        </Button>
      )}

      {items.length === 0 ? (
        <p className="rounded-md border border-dashed border-border p-6 text-center text-sm text-muted-foreground">
          Esta vistoria não tem itens.
        </p>
      ) : (
        <ul className="space-y-3">
          {items.map((it) => (
            <InspectionItemCard key={it.id} item={it} readOnly={!open} busy={action.isPending}
              onRecord={(req: RecordBuildInspectionItemRequest) => run({ kind: "item", itemId: it.id, req })} />
          ))}
        </ul>
      )}

      {failure && (
        <p role="alert" data-testid="inspection-error"
          className="rounded-md border border-destructive/30 bg-destructive/5 p-3 text-sm text-destructive">
          {failure}
        </p>
      )}

      {open ? (
        <div className="space-y-3 rounded-md border border-border bg-muted/30 p-3" data-testid="inspection-outcome">
          {ready ? (
            <div className="space-y-1 text-sm">
              <p>
                Resultado ao concluir: <StatusPill label={outcomeInfo.label} tone={outcomeInfo.tone} />
              </p>
              <p className="text-xs text-muted-foreground">{outcome.reason}</p>
              <p className="text-xs text-muted-foreground">
                Regra: item obrigatório “Não ok” reprova; item opcional “Não ok” aprova com ressalvas. Cada item
                “Não ok” abre uma não conformidade.
              </p>
            </div>
          ) : (
            <div className="space-y-1 text-sm text-destructive" data-testid="inspection-blockers">
              <p className="flex items-center gap-1 font-medium"><AlertTriangle className="h-4 w-4" /> Ainda não dá para concluir:</p>
              <ul className="list-disc space-y-0.5 pl-5 text-xs">
                {problems.empty && <li>A vistoria não tem itens.</li>}
                {problems.pending.length > 0 && (
                  <li>Itens obrigatórios sem resultado: {problems.pending.map((i) => i.text).join("; ")}.</li>
                )}
                {problems.missingPhoto.length > 0 && (
                  <li>Foto obrigatória faltando: {problems.missingPhoto.map((i) => i.text).join("; ")}.</li>
                )}
              </ul>
            </div>
          )}
          <div className="space-y-1">
            <Label htmlFor="inspection-notes" className="text-xs">Observações finais (interno, opcional)</Label>
            <Textarea id="inspection-notes" value={notes} rows={2} onChange={(e) => setNotes(e.target.value)} />
          </div>
          <div className="sticky bottom-0 -mx-3 -mb-3 flex flex-wrap gap-2 border-t border-border bg-background/95 p-3 sm:static sm:m-0 sm:border-0 sm:bg-transparent sm:p-0">
            <Button onClick={complete} disabled={!ready || action.isPending} className="flex-1 sm:flex-none">
              <CheckCircle2 className="mr-1 h-4 w-4" /> Concluir vistoria
            </Button>
            <Button variant="ghost" onClick={cancel} disabled={action.isPending}>
              <Ban className="mr-1 h-4 w-4" /> Cancelar vistoria
            </Button>
          </div>
        </div>
      ) : (
        <div className="rounded-md border border-border bg-muted/30 p-3 text-sm">
          {insp.completedAt
            ? <p>Concluída{insp.completedByName ? ` por ${insp.completedByName}` : ""} em {fmtDateTime(insp.completedAt)}.</p>
            : <p>Vistoria {st.label.toLowerCase()}.</p>}
          {insp.notes && <p className="mt-1 whitespace-pre-wrap text-xs text-muted-foreground">{insp.notes}</p>}
          {insp.notOkItems > 0 && (
            <p className="mt-1 text-xs text-muted-foreground">
              {insp.notOkItems} item(ns) “Não ok” abriram não conformidades — veja em Não conformidades.
            </p>
          )}
        </div>
      )}
    </div>
  );
}
