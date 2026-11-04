import { useEffect, useState } from "react";
import { AlertTriangle, CalendarCheck, ClipboardCheck, FileText, KeyRound, PackageCheck, Plus, Trash2 } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Textarea } from "@/components/ui/textarea";
import {
  useAcceptHandover, useCreateWalkthrough, useDeliverHandover, useHandover, useSaveHandover,
} from "../../hooks/use-build-quality";
import type { BuildHandoverDto } from "../../api/build-quality.api";
import { HANDOVER_STATUS, INSPECTION_STATUS, channelLabel } from "../../lib/quality";
import { fmtDate, fmtDateTime } from "../../lib/format";
import { StatusPill } from "../commercial/StatusPill";
import { ConfirmActionDialog } from "../commercial/ConfirmActionDialog";
import { errorMessage } from "../commercial/errors";
import { InspectionFormDialog } from "../quality/InspectionFormDialog";
import { PrivateUploadButton } from "../quality/PrivateUploadButton";

interface Doc { name: string; fileKey: string | null; fileUrl: string | null }

function isHandover(v: unknown): v is BuildHandoverDto {
  return !!v && typeof v === "object" && !Array.isArray(v) && "status" in v && "projectId" in v;
}

interface Props {
  projectId: string;
  onRunInspection: (id: string) => void;
}

/** The delivery of the property: date, walkthrough with the client, documents, keys, delivery and acceptance. */
export function HandoverSection({ projectId, onRunInspection }: Props) {
  const { data, isLoading, isError } = useHandover(projectId);
  const handover = isHandover(data) ? data : null;
  const save = useSaveHandover(projectId);
  const [date, setDate] = useState("");

  if (isLoading) return <div className="h-32 animate-pulse rounded-md bg-muted" />;
  if (isError) {
    return (
      <p className="rounded-md border border-destructive/20 bg-destructive/5 p-6 text-center text-sm text-destructive">
        Erro ao carregar a entrega.
      </p>
    );
  }

  if (!handover) {
    return (
      <div className="space-y-3 rounded-md border border-dashed border-border p-4">
        <p className="text-sm font-medium">A entrega do imóvel ainda não foi agendada.</p>
        <p className="text-xs text-muted-foreground">
          Agende para registrar a vistoria com o cliente, os documentos entregues e as chaves.
        </p>
        <div className="flex flex-wrap items-end gap-2">
          <div>
            <Label htmlFor="handover-first-date" className="text-xs">Data da entrega</Label>
            <Input id="handover-first-date" type="date" value={date} onChange={(e) => setDate(e.target.value)} className="mt-0.5 w-44" />
          </div>
          <Button size="sm" disabled={save.isPending}
            onClick={() => save.mutate({ scheduledFor: date || null }, {
              onSuccess: () => toast.success("Entrega agendada."),
              onError:   (e) => toast.error(errorMessage(e, "Não foi possível agendar.")),
            })}>
            <CalendarCheck className="mr-1 h-3.5 w-3.5" /> Agendar entrega
          </Button>
        </div>
      </div>
    );
  }

  return <HandoverDetails projectId={projectId} handover={handover} onRunInspection={onRunInspection} />;
}

function HandoverDetails({ projectId, handover: h, onRunInspection }: { projectId: string; handover: BuildHandoverDto; onRunInspection: (id: string) => void }) {
  const save = useSaveHandover(projectId);
  const deliver = useDeliverHandover(projectId);
  const accept = useAcceptHandover(projectId);
  const walk = useCreateWalkthrough(projectId);
  const [date, setDate] = useState("");
  const [keys, setKeys] = useState(false);
  const [notes, setNotes] = useState("");
  const [docs, setDocs] = useState<Doc[]>([]);
  const [docName, setDocName] = useState("");
  const [signer, setSigner] = useState("");
  const [comment, setComment] = useState("");
  const [confirmDeliver, setConfirmDeliver] = useState(false);
  const [walkOpen, setWalkOpen] = useState(false);
  const [failure, setFailure] = useState<string | null>(null);

  useEffect(() => {
    setDate(h.scheduledFor ?? "");
    setKeys(h.keysDelivered);
    setNotes(h.notes ?? "");
    setDocs((Array.isArray(h.documents) ? h.documents : []).map((d) => ({ name: d.name, fileKey: d.fileKey, fileUrl: d.fileUrl })));
  }, [h]);

  const st = HANDOVER_STATUS[h.status] ?? { label: h.status, tone: "gray" as const };
  const delivered = h.status === "Delivered";
  const walkStatus = h.walkthroughStatus ? INSPECTION_STATUS[h.walkthroughStatus] : null;
  const walkCancelled = h.walkthroughStatus === "Cancelled";

  const saveAll = () => {
    save.mutate({
      scheduledFor: date || null, keysDelivered: keys, notes: notes.trim() || null,
      documents: docs.map((d) => ({ name: d.name, fileKey: d.fileKey })),
    }, {
      onSuccess: () => toast.success("Entrega salva."),
      onError:   (e) => toast.error(errorMessage(e, "Não foi possível salvar a entrega.")),
    });
  };

  const addDoc = (fileKey: string | null, fileUrl: string | null, fallbackName?: string) => {
    const name = docName.trim() || fallbackName || "";
    if (!name) { toast.error("Informe o nome do documento."); return; }
    setDocs((d) => [...d, { name, fileKey, fileUrl }]);
    setDocName("");
  };

  const doDeliver = () => {
    setFailure(null);
    deliver.mutate(keys, {
      onSuccess: (r) => {
        setConfirmDeliver(false);
        toast.success(r.projectStatusNote ? `Imóvel entregue. ${r.projectStatusNote}` : "Imóvel entregue. A obra foi concluída.");
      },
      onError: (e) => {
        const msg = errorMessage(e, "Não foi possível entregar.");
        setConfirmDeliver(false);
        setFailure(msg);
        toast.error(msg);
      },
    });
  };

  const doAccept = () => {
    if (!signer.trim()) { toast.error("Informe quem aceitou."); return; }
    setFailure(null);
    accept.mutate({ signerName: signer.trim(), comment: comment.trim() || null }, {
      onSuccess: () => { toast.success("Aceite registrado."); setSigner(""); setComment(""); },
      onError:   (e) => {
        const msg = errorMessage(e, "Não foi possível registrar o aceite.");
        setFailure(msg);
        toast.error(msg);
      },
    });
  };

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-start justify-between gap-2 rounded-md border border-border bg-card p-3">
        <div className="space-y-0.5 text-sm">
          <p className="font-semibold">Entrega do imóvel</p>
          <p className="text-xs text-muted-foreground">
            Prevista para {fmtDate(h.scheduledFor)}
            {h.deliveredAt ? ` · entregue em ${fmtDateTime(h.deliveredAt)}${h.deliveredByName ? ` por ${h.deliveredByName}` : ""}` : ""}
          </p>
          {h.projectStatusNote && <p className="text-xs text-amber-700 dark:text-amber-400">{h.projectStatusNote}</p>}
        </div>
        <StatusPill label={st.label} tone={st.tone} />
      </div>

      {h.openCriticalNonConformities > 0 && !delivered && (
        <p role="note" className="flex items-start gap-1.5 rounded-md border border-red-200 bg-red-50 p-2 text-xs text-red-700 dark:border-red-900 dark:bg-red-900/20 dark:text-red-300">
          <AlertTriangle className="mt-0.5 h-3.5 w-3.5 shrink-0" />
          {h.openCriticalNonConformities} não conformidade(s) crítica(s) aberta(s): resolva antes de entregar o imóvel.
        </p>
      )}

      <section className="space-y-2 rounded-md border border-border p-3">
        <h4 className="flex items-center gap-1.5 text-sm font-semibold"><ClipboardCheck className="h-4 w-4" /> Vistoria com o cliente</h4>
        {h.walkthroughInspectionId && !walkCancelled ? (
          <div className="flex flex-wrap items-center gap-2">
            {walkStatus && <StatusPill label={walkStatus.label} tone={walkStatus.tone} />}
            <Button size="sm" variant="outline" onClick={() => onRunInspection(h.walkthroughInspectionId as string)}>
              Abrir vistoria
            </Button>
            <p className="w-full text-[11px] text-muted-foreground">Cada pendência (“Não ok”) vira uma não conformidade ao concluir.</p>
          </div>
        ) : delivered ? (
          <p className="text-xs text-muted-foreground">Entregue sem vistoria com o cliente.</p>
        ) : (
          <div className="space-y-1">
            <p className="text-xs text-muted-foreground">
              Opcional: percorra o imóvel com o cliente usando um checklist. Ao concluir, a entrega vai para “Vistoria concluída”.
            </p>
            <Button size="sm" onClick={() => setWalkOpen(true)}><Plus className="mr-1 h-3.5 w-3.5" /> Criar vistoria com o cliente</Button>
          </div>
        )}
      </section>

      <section className="space-y-3 rounded-md border border-border p-3">
        <h4 className="flex items-center gap-1.5 text-sm font-semibold"><FileText className="h-4 w-4" /> Dados da entrega</h4>
        <div className="grid gap-3 sm:grid-cols-2">
          <div>
            <Label htmlFor="handover-date" className="text-xs">Data da entrega</Label>
            <Input id="handover-date" type="date" value={date} onChange={(e) => setDate(e.target.value)} className="mt-0.5" />
          </div>
          <label className="flex items-center gap-2 self-end pb-2 text-sm">
            <input type="checkbox" checked={keys} onChange={(e) => setKeys(e.target.checked)} />
            <KeyRound className="h-4 w-4 text-muted-foreground" /> Chaves entregues
          </label>
        </div>
        <div className="space-y-2">
          <p className="text-xs font-medium">Documentos entregues</p>
          {docs.length === 0 && <p className="text-xs text-muted-foreground">Nenhum documento.</p>}
          <ul className="space-y-1">
            {docs.map((d, i) => (
              <li key={`${d.name}-${i}`} className="flex items-center justify-between gap-2 rounded-md bg-muted/40 px-2 py-1 text-sm">
                <span className="min-w-0 truncate">
                  {d.fileUrl ? <a href={d.fileUrl} target="_blank" rel="noreferrer" className="text-primary hover:underline">{d.name}</a> : d.name}
                  {!d.fileKey && <span className="ml-1 text-[11px] text-muted-foreground">(sem arquivo)</span>}
                </span>
                <button type="button" aria-label={`Remover documento ${d.name}`} onClick={() => setDocs(docs.filter((_, j) => j !== i))}
                  className="p-0.5 text-muted-foreground hover:text-destructive"><Trash2 className="h-3.5 w-3.5" /></button>
              </li>
            ))}
          </ul>
          <div className="flex flex-wrap items-end gap-2">
            <div className="min-w-0 flex-1">
              <Label htmlFor="handover-doc-name" className="text-xs">Nome do documento</Label>
              <Input id="handover-doc-name" value={docName} onChange={(e) => setDocName(e.target.value)}
                placeholder="Ex: Manual do proprietário" className="mt-0.5" />
            </div>
            <PrivateUploadButton context="build-handover" allowPdf label="Enviar arquivo" testId="handover-doc-input"
              onUploaded={({ key, url }, file) => addDoc(key, url, file.name)} />
            <Button type="button" size="sm" variant="ghost" onClick={() => addDoc(null, null)}>Sem arquivo</Button>
          </div>
          <p className="text-[11px] text-muted-foreground">Arquivos privados (PDF ou imagem): o cliente só os vê pelo portal, por link temporário.</p>
        </div>
        <div>
          <Label htmlFor="handover-notes" className="text-xs">Observações (interno)</Label>
          <Textarea id="handover-notes" value={notes} onChange={(e) => setNotes(e.target.value)} rows={2} className="mt-0.5" />
        </div>
        <Button size="sm" variant="outline" onClick={saveAll} disabled={save.isPending}>Salvar entrega</Button>
      </section>

      {failure && (
        <p role="alert" data-testid="handover-error" className="rounded-md border border-destructive/30 bg-destructive/5 p-3 text-sm text-destructive">
          {failure}
        </p>
      )}

      {!delivered && (
        <Button onClick={() => setConfirmDeliver(true)} disabled={deliver.isPending} className="w-full sm:w-auto">
          <PackageCheck className="mr-1 h-4 w-4" /> Entregar imóvel
        </Button>
      )}

      <section className="space-y-2 rounded-md border border-border p-3">
        <h4 className="text-sm font-semibold">Aceite do cliente</h4>
        {h.acceptedAt ? (
          <div className="space-y-1 text-sm">
            <p className="flex flex-wrap items-center gap-1.5">
              <StatusPill label="Aceito" tone="green" />
              <StatusPill label={channelLabel(h.acceptanceChannel)} tone={h.acceptanceChannel === "Portal" ? "blue" : "gray"} />
              <span>{h.acceptedByName} em {fmtDateTime(h.acceptedAt)}</span>
            </p>
            {h.acceptanceComment && <p className="text-xs text-muted-foreground">“{h.acceptanceComment}”</p>}
          </div>
        ) : (
          <div className="space-y-2">
            <p className="text-xs text-muted-foreground">
              Aguardando o aceite. O cliente pode aceitar pelo portal (com senha) ou você registra aqui o aceite obtido
              pessoalmente. Disponível depois da vistoria com o cliente ou da entrega.
            </p>
            <div className="grid gap-2 sm:grid-cols-2">
              <div>
                <Label htmlFor="handover-signer" className="text-xs">Quem aceitou</Label>
                <Input id="handover-signer" value={signer} onChange={(e) => setSigner(e.target.value)} className="mt-0.5" />
              </div>
              <div>
                <Label htmlFor="handover-accept-comment" className="text-xs">Comentário (opcional)</Label>
                <Input id="handover-accept-comment" value={comment} onChange={(e) => setComment(e.target.value)} className="mt-0.5" />
              </div>
            </div>
            <Button size="sm" variant="outline" onClick={doAccept} disabled={accept.isPending || h.status === "Scheduled"}>
              Registrar aceite
            </Button>
          </div>
        )}
      </section>

      <ConfirmActionDialog
        open={confirmDeliver}
        title="Entregar o imóvel?"
        description={<>
          <p>A obra é marcada como concluída e as garantias sem data de início começam a contar da entrega.</p>
          <p>Sem nenhuma garantia cadastrada, é criado o conjunto de referência.</p>
          <p>Com não conformidade crítica aberta, a entrega é recusada.</p>
        </>}
        confirmLabel="Entregar imóvel"
        pending={deliver.isPending}
        onConfirm={doDeliver}
        onClose={() => setConfirmDeliver(false)}
      />
      <InspectionFormDialog open={walkOpen} projectId={projectId} walkthrough pending={walk.isPending}
        onClose={() => setWalkOpen(false)}
        onSubmit={(req) => walk.mutate({
          title: req.title, templateId: req.templateId, scheduledFor: req.scheduledFor, inspectorName: req.inspectorName, items: req.items,
        }, {
          onSuccess: (insp) => { toast.success("Vistoria com o cliente criada."); setWalkOpen(false); onRunInspection(insp.id); },
          onError:   (e) => toast.error(errorMessage(e, "Não foi possível criar a vistoria.")),
        })} />
    </div>
  );
}
