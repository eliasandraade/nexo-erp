import { useEffect, useState } from "react";
import { Ban, CheckCheck, CheckCircle2, Pencil, Play, RotateCcw } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Label } from "@/components/ui/label";
import { Textarea } from "@/components/ui/textarea";
import {
  Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle,
} from "@/components/ui/dialog";
import { useNonConformityAction, useSaveNonConformity, type NcAction } from "../../hooks/use-build-quality";
import type { BuildNonConformityDto } from "../../api/build-quality.api";
import { NC_STATUS, ncRequestFrom, severityInfo } from "../../lib/quality";
import { fmtBRL, fmtDate, fmtDateTime } from "../../lib/format";
import { StatusPill } from "../commercial/StatusPill";
import { ConfirmActionDialog } from "../commercial/ConfirmActionDialog";
import { errorMessage } from "../commercial/errors";
import { PhotoStrip, PrivateUploadButton } from "./PrivateUploadButton";

interface Props {
  projectId: string;
  nc: BuildNonConformityDto | null;
  onChange: (nc: BuildNonConformityDto) => void;
  onEdit: (nc: BuildNonConformityDto) => void;
  onClose: () => void;
}

/** One NC: data, photos and the treatment flow (iniciar → resolver → verificar / reabrir, ou cancelar). */
export function NonConformityDetailDialog({ projectId, nc, onChange, onEdit, onClose }: Props) {
  const action = useNonConformityAction(projectId);
  const save = useSaveNonConformity(projectId);
  const [resolving, setResolving] = useState(false);
  const [rootCause, setRootCause] = useState("");
  const [corrective, setCorrective] = useState("");
  const [cancelling, setCancelling] = useState(false);
  const [failure, setFailure] = useState<string | null>(null);

  const ncId = nc?.id;
  const savedRoot = nc?.rootCause ?? "";
  const savedCorrective = nc?.correctiveAction ?? "";
  useEffect(() => {
    setResolving(false);
    setFailure(null);
    setRootCause(savedRoot);
    setCorrective(savedCorrective);
  }, [ncId, savedRoot, savedCorrective]);

  if (!nc) return null;
  const st = NC_STATUS[nc.status] ?? { label: nc.status, tone: "gray" as const };
  const sev = severityInfo(nc.severity);
  const isOpen = nc.status === "Open" || nc.status === "InTreatment";

  const run = (a: NcAction, ok: string, after?: () => void) => {
    setFailure(null);
    action.mutate({ id: nc.id, action: a }, {
      onSuccess: (data) => { onChange(data); toast.success(ok); after?.(); },
      onError:   (e) => {
        const msg = errorMessage(e, "Não foi possível concluir.");
        setFailure(msg);
        toast.error(msg);
      },
    });
  };

  const resolve = () => {
    if (!corrective.trim()) { setFailure("Informe a ação corretiva para resolver."); return; }
    run({ kind: "resolve", rootCause: rootCause.trim() || null, correctiveAction: corrective.trim() },
      "Não conformidade resolvida. Falta a verificação.", () => setResolving(false));
  };

  const addPhoto = (key: string) => {
    save.mutate({ id: nc.id, req: ncRequestFrom(nc, { photoKeys: [...(nc.photoKeys ?? []), key] }) }, {
      onSuccess: (data) => { onChange(data); toast.success("Foto adicionada."); },
      onError:   (e) => toast.error(errorMessage(e, "Não foi possível adicionar a foto.")),
    });
  };

  return (
    <>
      <Dialog open={!!nc && !cancelling} onOpenChange={(v) => !v && onClose()}>
        <DialogContent className="max-h-[90vh] overflow-y-auto sm:max-w-lg">
          <DialogHeader>
            <DialogTitle>NC #{nc.number} — {nc.title}</DialogTitle>
            <DialogDescription asChild>
              <div className="flex flex-wrap items-center gap-1.5">
                <StatusPill label={st.label} tone={st.tone} />
                <StatusPill label={`Gravidade ${sev.label.toLowerCase()}`} tone={sev.tone} />
                {nc.isOverdue && <StatusPill label="Vencida" tone="red" />}
              </div>
            </DialogDescription>
          </DialogHeader>

          {nc.severity === "Critical" && isOpen && (
            <p className="rounded-md border border-red-200 bg-red-50 p-2 text-xs text-red-700 dark:border-red-900 dark:bg-red-900/20 dark:text-red-300">
              Não conformidade crítica aberta impede concluir a etapa (100%) e a entrega do imóvel.
            </p>
          )}

          <dl className="grid grid-cols-2 gap-x-3 gap-y-2 text-xs">
            <div><dt className="text-muted-foreground">Etapa</dt><dd>{nc.stageName ?? "—"}</dd></div>
            <div><dt className="text-muted-foreground">Responsável</dt><dd>{nc.responsibleName ?? "—"}</dd></div>
            <div><dt className="text-muted-foreground">Prazo</dt><dd>{fmtDate(nc.dueDate)}</dd></div>
            <div><dt className="text-muted-foreground">Impacto (informativo)</dt><dd>{nc.costImpact != null ? fmtBRL(nc.costImpact) : "—"}</dd></div>
            <div className="col-span-2"><dt className="text-muted-foreground">Aberta por</dt>
              <dd>{nc.createdByName} em {fmtDateTime(nc.createdAt)}{nc.inspectionId ? " (pela vistoria)" : ""}</dd></div>
          </dl>
          {nc.description && <p className="whitespace-pre-wrap text-sm">{nc.description}</p>}
          {(nc.rootCause || nc.correctiveAction) && !resolving && (
            <div className="space-y-1 rounded-md bg-muted/40 p-2 text-xs">
              {nc.rootCause && <p><span className="font-medium">Causa raiz:</span> {nc.rootCause}</p>}
              {nc.correctiveAction && <p><span className="font-medium">Ação corretiva:</span> {nc.correctiveAction}</p>}
            </div>
          )}
          {nc.resolvedAt && (
            <p className="text-xs text-muted-foreground">Resolvida{nc.resolvedByName ? ` por ${nc.resolvedByName}` : ""} em {fmtDateTime(nc.resolvedAt)}.</p>
          )}
          {nc.verifiedAt && (
            <p className="text-xs text-muted-foreground">
              Verificada{nc.verifiedByName ? ` por ${nc.verifiedByName}` : ""} em {fmtDateTime(nc.verifiedAt)}
              {nc.verifiedBySameUser ? " (mesma pessoa que resolveu)" : ""}.
            </p>
          )}
          {nc.cancelReason && <p className="text-xs text-muted-foreground">Cancelada: {nc.cancelReason}</p>}

          <div className="space-y-2">
            <PhotoStrip urls={nc.photoUrls} alt={`Foto da NC ${nc.number}`} />
            {isOpen && (
              <PrivateUploadButton context="build-quality" capture label="Adicionar foto" disabled={save.isPending}
                testId="nc-detail-photo-input" onUploaded={({ key }) => addPhoto(key)} />
            )}
          </div>

          {resolving && (
            <div className="space-y-2 rounded-md border border-border p-3">
              <div>
                <Label htmlFor="nc-root" className="text-xs">Causa raiz</Label>
                <Textarea id="nc-root" value={rootCause} onChange={(e) => setRootCause(e.target.value)} rows={2} className="mt-0.5" />
              </div>
              <div>
                <Label htmlFor="nc-corrective" className="text-xs">Ação corretiva (obrigatória)</Label>
                <Textarea id="nc-corrective" value={corrective} onChange={(e) => setCorrective(e.target.value)} rows={2} className="mt-0.5" />
              </div>
              <div className="flex gap-2">
                <Button size="sm" onClick={resolve} disabled={action.isPending}>Confirmar resolução</Button>
                <Button size="sm" variant="ghost" onClick={() => setResolving(false)}>Voltar</Button>
              </div>
            </div>
          )}

          {failure && (
            <p role="alert" className="rounded-md border border-destructive/30 bg-destructive/5 p-2 text-xs text-destructive">{failure}</p>
          )}

          {!resolving && (
            <div className="flex flex-wrap gap-2">
              {nc.status === "Open" && (
                <Button size="sm" variant="outline" disabled={action.isPending}
                  onClick={() => run({ kind: "start" }, "Tratamento iniciado.")}>
                  <Play className="mr-1 h-3.5 w-3.5" /> Iniciar tratamento
                </Button>
              )}
              {isOpen && (
                <Button size="sm" onClick={() => setResolving(true)}><CheckCircle2 className="mr-1 h-3.5 w-3.5" /> Resolver</Button>
              )}
              {nc.status === "Resolved" && (
                <>
                  <Button size="sm" disabled={action.isPending} onClick={() => run({ kind: "verify" }, "Não conformidade verificada.")}>
                    <CheckCheck className="mr-1 h-3.5 w-3.5" /> Verificar
                  </Button>
                  <Button size="sm" variant="outline" disabled={action.isPending}
                    onClick={() => run({ kind: "reopen" }, "Não conformidade reaberta (em tratamento).")}>
                    <RotateCcw className="mr-1 h-3.5 w-3.5" /> Reabrir
                  </Button>
                </>
              )}
              {isOpen && (
                <>
                  <Button size="sm" variant="outline" onClick={() => onEdit(nc)}><Pencil className="mr-1 h-3.5 w-3.5" /> Editar</Button>
                  <Button size="sm" variant="ghost" onClick={() => setCancelling(true)}><Ban className="mr-1 h-3.5 w-3.5" /> Cancelar NC</Button>
                </>
              )}
            </div>
          )}
        </DialogContent>
      </Dialog>

      <ConfirmActionDialog
        open={cancelling}
        title={`Cancelar a NC #${nc.number}?`}
        description={<p>Use quando a não conformidade foi aberta por engano. Informe o motivo.</p>}
        confirmLabel="Cancelar não conformidade"
        destructive
        textLabel="Motivo"
        pending={action.isPending}
        onConfirm={(reason) => {
          if (!reason) { toast.error("Informe o motivo do cancelamento."); return; }
          run({ kind: "cancel", reason }, "Não conformidade cancelada.", () => setCancelling(false));
        }}
        onClose={() => setCancelling(false)}
      />
    </>
  );
}
