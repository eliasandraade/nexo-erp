import { useEffect, useState } from "react";
import { Ban, CalendarClock, CheckCircle2, Lock, Play, Star } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Textarea } from "@/components/ui/textarea";
import {
  Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle,
} from "@/components/ui/dialog";
import { useAssistanceAction, type AssistanceAction } from "../../hooks/use-build-quality";
import type { BuildAssistanceDto } from "../../api/build-quality.api";
import { ASSISTANCE_STATUS, channelLabel, coverageLabel, localDateTimeToIso } from "../../lib/quality";
import { fmtDate, fmtDateTime } from "../../lib/format";
import { StatusPill } from "../commercial/StatusPill";
import { ConfirmActionDialog } from "../commercial/ConfirmActionDialog";
import { errorMessage } from "../commercial/errors";
import { PhotoStrip, PrivateUploadButton } from "../quality/PrivateUploadButton";

const MAX_PHOTOS = 6;
type CoverageChoice = "auto" | "yes" | "no";

interface Props {
  projectId: string;
  request: BuildAssistanceDto | null;
  onChange: (r: BuildAssistanceDto) => void;
  onClose: () => void;
}

/** One technical assistance request: triage (warranty coverage), visit, treatment, resolution and the client's rating. */
export function AssistanceDetailDialog({ projectId, request: r, onChange, onClose }: Props) {
  const action = useAssistanceAction(projectId);
  const [coverage, setCoverage] = useState<CoverageChoice>("auto");
  const [triageNotes, setTriageNotes] = useState("");
  const [visit, setVisit] = useState("");
  const [resolution, setResolution] = useState("");
  const [rejecting, setRejecting] = useState(false);
  const [failure, setFailure] = useState<string | null>(null);

  const rid = r?.id;
  const savedNotes = r?.triageNotes ?? "";
  useEffect(() => {
    setCoverage("auto");
    setTriageNotes(savedNotes);
    setVisit("");
    setResolution("");
    setFailure(null);
  }, [rid, savedNotes]);

  if (!r) return null;
  const st = ASSISTANCE_STATUS[r.status] ?? { label: r.status, tone: "gray" as const };
  const cov = coverageLabel(r.warrantyCovered);
  const canTriage = r.status === "Open" || r.status === "Triaged";
  const canSchedule = r.status === "Triaged" || r.status === "Scheduled";
  const canStart = canSchedule;
  const canResolve = r.status === "Triaged" || r.status === "Scheduled" || r.status === "InProgress";
  const canReject = r.status === "Open" || r.status === "Triaged" || r.status === "Scheduled";
  const isOpen = !["Resolved", "Closed", "Rejected"].includes(r.status);
  const photoCount = Array.isArray(r.photoKeys) ? r.photoKeys.length : 0;

  const run = (a: AssistanceAction, ok: string, after?: () => void) => {
    setFailure(null);
    action.mutate({ id: r.id, action: a }, {
      onSuccess: (data) => { onChange(data); toast.success(ok); after?.(); },
      onError:   (e) => {
        const msg = errorMessage(e, "Não foi possível concluir.");
        setFailure(msg);
        toast.error(msg);
      },
    });
  };

  const triage = () => {
    const a: AssistanceAction = { kind: "triage", notes: triageNotes.trim() || null };
    if (coverage !== "auto") a.warrantyCovered = coverage === "yes";
    run(a, "Chamado triado.");
  };

  const schedule = () => {
    const iso = localDateTimeToIso(visit);
    if (!iso) { setFailure("Informe a data e a hora da visita."); return; }
    run({ kind: "schedule", scheduledFor: iso }, "Visita agendada.");
  };

  const resolve = () => {
    if (!resolution.trim()) { setFailure("Descreva o que foi feito (o cliente vê este texto)."); return; }
    run({ kind: "resolve", resolutionNotes: resolution.trim() }, "Chamado resolvido.");
  };

  return (
    <>
      <Dialog open={!rejecting} onOpenChange={(v) => !v && onClose()}>
        <DialogContent className="max-h-[90vh] overflow-y-auto sm:max-w-lg">
          <DialogHeader>
            <DialogTitle>Chamado #{r.number} — {r.title}</DialogTitle>
            <DialogDescription asChild>
              <div className="flex flex-wrap items-center gap-1.5">
                <StatusPill label={st.label} tone={st.tone} />
                <StatusPill label={`Aberto via ${channelLabel(r.openedVia)}`} tone={r.openedVia === "Portal" ? "blue" : "gray"} />
                <StatusPill label={cov.label} tone={cov.tone} />
              </div>
            </DialogDescription>
          </DialogHeader>

          <dl className="grid grid-cols-2 gap-x-3 gap-y-2 text-xs">
            <div><dt className="text-muted-foreground">Categoria</dt><dd>{r.category}</dd></div>
            <div><dt className="text-muted-foreground">Local</dt><dd>{r.location ?? "—"}</dd></div>
            <div><dt className="text-muted-foreground">Aberto por</dt><dd>{r.openedByName} em {fmtDate(r.openedOn)}</dd></div>
            <div><dt className="text-muted-foreground">Telefone</dt><dd>{r.contactPhone ?? "—"}</dd></div>
            {r.scheduledFor && <div className="col-span-2"><dt className="text-muted-foreground">Visita</dt><dd>{fmtDateTime(r.scheduledFor)}</dd></div>}
          </dl>
          <p className="whitespace-pre-wrap text-sm">{r.description}</p>

          <div className="space-y-2">
            <PhotoStrip urls={r.photoUrls} alt={`Foto do chamado ${r.number}`} />
            {isOpen && photoCount < MAX_PHOTOS && (
              <PrivateUploadButton context="build-assistance" capture label="Adicionar foto" disabled={action.isPending}
                testId="assistance-photo-input" onUploaded={({ key }) => run({ kind: "photo", key }, "Foto adicionada.")} />
            )}
          </div>

          <section className="space-y-2 rounded-md border border-border p-3" data-testid="assistance-coverage">
            <p className="text-sm font-semibold">Garantia</p>
            <p className="text-xs">
              <span className="font-medium">{cov.label}</span>
              {r.warrantySystem ? ` — garantia de ${r.warrantySystem}` : ""}
              {r.triagedAt ? ` · triado${r.triagedByName ? ` por ${r.triagedByName}` : ""} em ${fmtDateTime(r.triagedAt)}` : " · ainda não triado"}
            </p>
            {canTriage && (
              <div className="space-y-2">
                <div>
                  <Label htmlFor="as-coverage" className="text-xs">Cobertura</Label>
                  <select id="as-coverage" value={coverage} onChange={(e) => setCoverage(e.target.value as CoverageChoice)}
                    className="mt-0.5 h-9 w-full rounded-md border border-input bg-background px-2 text-sm">
                    <option value="auto">Calcular pela garantia da categoria</option>
                    <option value="yes">Coberto (decisão da empresa)</option>
                    <option value="no">Não coberto (decisão da empresa)</option>
                  </select>
                  <p className="mt-0.5 text-[11px] text-muted-foreground">
                    Automático: garantia da categoria vigente na data de abertura → coberto; vencida → não coberto; sem garantia
                    (ex.: Outros) → a decidir.
                  </p>
                </div>
                <div>
                  <Label htmlFor="as-triage-notes" className="flex items-center gap-1 text-xs"><Lock className="h-3 w-3" /> Notas da triagem (interno)</Label>
                  <Textarea id="as-triage-notes" value={triageNotes} onChange={(e) => setTriageNotes(e.target.value)} rows={2} className="mt-0.5" />
                </div>
                <Button size="sm" onClick={triage} disabled={action.isPending}>Triar chamado</Button>
              </div>
            )}
            {!canTriage && r.triageNotes && <p className="text-xs text-muted-foreground"><Lock className="mr-1 inline h-3 w-3" />{r.triageNotes}</p>}
          </section>

          {(canSchedule || canResolve) && (
            <section className="space-y-3 rounded-md border border-border p-3">
              <p className="text-sm font-semibold">Atendimento</p>
              {canSchedule && (
                <div className="flex flex-wrap items-end gap-2">
                  <div>
                    <Label htmlFor="as-visit" className="text-xs">Visita</Label>
                    <Input id="as-visit" type="datetime-local" value={visit} onChange={(e) => setVisit(e.target.value)} className="mt-0.5" />
                  </div>
                  <Button size="sm" variant="outline" onClick={schedule} disabled={action.isPending}>
                    <CalendarClock className="mr-1 h-3.5 w-3.5" /> Agendar visita
                  </Button>
                  {canStart && (
                    <Button size="sm" variant="outline" onClick={() => run({ kind: "start" }, "Atendimento iniciado.")} disabled={action.isPending}>
                      <Play className="mr-1 h-3.5 w-3.5" /> Iniciar atendimento
                    </Button>
                  )}
                </div>
              )}
              {canResolve && (
                <div className="space-y-1">
                  <Label htmlFor="as-resolution" className="text-xs">O que foi feito (visível ao cliente)</Label>
                  <Textarea id="as-resolution" value={resolution} onChange={(e) => setResolution(e.target.value)} rows={2} />
                  <Button size="sm" onClick={resolve} disabled={action.isPending}>
                    <CheckCircle2 className="mr-1 h-3.5 w-3.5" /> Resolver
                  </Button>
                </div>
              )}
            </section>
          )}

          {r.resolutionNotes && (
            <p className="rounded-md bg-muted/40 p-2 text-xs"><span className="font-medium">Resolução:</span> {r.resolutionNotes}
              {r.resolvedAt ? ` (${fmtDateTime(r.resolvedAt)})` : ""}</p>
          )}
          {r.rejectionReason && (
            <p className="rounded-md bg-muted/40 p-2 text-xs"><span className="font-medium">Recusado:</span> {r.rejectionReason}</p>
          )}
          {(r.clientRating != null || r.clientFeedback) && (
            <div className="space-y-1 rounded-md bg-muted/40 p-2 text-xs" data-testid="assistance-rating">
              {r.clientRating != null && (
                <p className="flex items-center gap-1" aria-label={`Avaliação do cliente: ${r.clientRating} de 5`}>
                  <span className="font-medium">Avaliação do cliente:</span>
                  {[1, 2, 3, 4, 5].map((n) => (
                    <Star key={n} className={`h-3.5 w-3.5 ${n <= (r.clientRating ?? 0) ? "fill-amber-400 text-amber-400" : "text-muted-foreground"}`} />
                  ))}
                </p>
              )}
              {r.clientFeedback && <p>“{r.clientFeedback}”</p>}
            </div>
          )}

          {failure && (
            <p role="alert" className="rounded-md border border-destructive/30 bg-destructive/5 p-2 text-xs text-destructive">{failure}</p>
          )}

          <div className="flex flex-wrap gap-2">
            {r.status === "Resolved" && (
              <Button size="sm" onClick={() => run({ kind: "close" }, "Chamado encerrado.")} disabled={action.isPending}>Encerrar chamado</Button>
            )}
            {canReject && (
              <Button size="sm" variant="ghost" onClick={() => setRejecting(true)}><Ban className="mr-1 h-3.5 w-3.5" /> Recusar</Button>
            )}
          </div>
        </DialogContent>
      </Dialog>

      <ConfirmActionDialog
        open={rejecting}
        title={`Recusar o chamado #${r.number}?`}
        description={<p>O motivo é mostrado ao cliente no portal.</p>}
        confirmLabel="Recusar chamado"
        destructive
        textLabel="Motivo"
        pending={action.isPending}
        onConfirm={(reason) => {
          if (!reason) { toast.error("Informe o motivo da recusa."); return; }
          run({ kind: "reject", reason }, "Chamado recusado.", () => setRejecting(false));
        }}
        onClose={() => setRejecting(false)}
      />
    </>
  );
}
