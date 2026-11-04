import { useRef, useState } from "react";
import { ExternalLink, FileText, Pencil, Sparkles, Trash2, Upload } from "lucide-react";
import { toast } from "sonner";
import { cn } from "@/lib/utils";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Skeleton } from "@/components/ui/skeleton";
import { Dialog, DialogContent, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import type { BuildDocumentCategory, BuildDocumentDto } from "../../api/build-ai.api";
import {
  useActiveSuggestion, useAiGate, useAnalyzeDocument, useDeleteDocument, useDocuments, useSetDocumentVisibility,
  useUpdateDocument, useUploadDocument,
} from "../../hooks/use-build-ai";
import {
  DOCUMENT_AI_MAX_BYTES, DOCUMENT_CATEGORIES, DOCUMENT_CATEGORY_LABEL, VISION_DISABLED_MESSAGE, aiErrorMessage,
  documentFileProblem, fmtBytes, readDocumentExtraction, validityBadge,
} from "../../lib/ai";
import { fmtBRL, fmtDate, localToday } from "../../lib/format";
import { ProgressBar } from "../twin/common";
import { ConfirmActionDialog } from "../commercial/ConfirmActionDialog";
import { AiErrorAlert, selectClass } from "../ai/common";
import { AiSuggestionCard } from "../ai/AiSuggestionCard";

const ACCEPT = ".pdf,.jpg,.jpeg,.png,.webp,application/pdf,image/jpeg,image/png,image/webp";
const categoryLabel = (c: string) => DOCUMENT_CATEGORY_LABEL[c as BuildDocumentCategory] ?? c;

const BADGE_CLASS = {
  expired: "bg-rose-100 text-rose-700 dark:bg-rose-900/30 dark:text-rose-300",
  soon:    "bg-amber-100 text-amber-800 dark:bg-amber-900/30 dark:text-amber-300",
  ok:      "bg-emerald-100 text-emerald-700 dark:bg-emerald-900/30 dark:text-emerald-400",
} as const;

function UploadForm({ projectId, onDone }: { projectId: string; onDone: () => void }) {
  const upload = useUploadDocument(projectId);
  const fileRef = useRef<HTMLInputElement>(null);
  const [file, setFile] = useState<File | null>(null);
  const [category, setCategory] = useState<BuildDocumentCategory>("Outro");
  const [title, setTitle] = useState("");
  const [validUntil, setValidUntil] = useState("");
  const [clientVisible, setClientVisible] = useState(false);
  const [progress, setProgress] = useState<number | null>(null);
  const [error, setError] = useState<string | null>(null);

  const problem = file ? documentFileProblem(file) : null;

  const submit = () => {
    if (!file || problem) return;
    setError(null);
    setProgress(0);
    upload.mutate({
      input: { file, category, title: title.trim() || undefined, validUntil: validUntil || undefined, clientVisible },
      onProgress: (loaded, total) => setProgress(total > 0 ? (loaded / total) * 100 : 0),
    }, {
      onSuccess: () => { toast.success("Documento enviado."); onDone(); },
      onError:   (e) => setError(aiErrorMessage(e, "Não foi possível enviar o documento.")),
      onSettled: () => setProgress(null),
    });
  };

  return (
    <div className="space-y-2 rounded-md border border-dashed border-primary/40 p-3" data-testid="document-upload">
      <div className="grid gap-2 sm:grid-cols-2">
        <div className="space-y-1 sm:col-span-2">
          <Label htmlFor="doc-file" className="text-xs">Arquivo (PDF, JPEG, PNG ou WEBP, até 20 MB)</Label>
          <Input id="doc-file" ref={fileRef} type="file" accept={ACCEPT}
            onChange={(e) => { const f = e.target.files?.[0] ?? null; setFile(f); setError(null); if (f && !title) setTitle(f.name.replace(/\.[^.]+$/, "")); }} />
        </div>
        <div className="space-y-1">
          <Label htmlFor="doc-category" className="text-xs">Categoria</Label>
          <select id="doc-category" className={selectClass} value={category} onChange={(e) => setCategory(e.target.value as BuildDocumentCategory)}>
            {DOCUMENT_CATEGORIES.map((c) => <option key={c} value={c}>{DOCUMENT_CATEGORY_LABEL[c]}</option>)}
          </select>
        </div>
        <div className="space-y-1">
          <Label htmlFor="doc-title" className="text-xs">Título</Label>
          <Input id="doc-title" value={title} maxLength={200} onChange={(e) => setTitle(e.target.value)} />
        </div>
        <div className="space-y-1">
          <Label htmlFor="doc-valid" className="text-xs">Validade (opcional)</Label>
          <Input id="doc-valid" type="date" value={validUntil} onChange={(e) => setValidUntil(e.target.value)} />
        </div>
        <label className="flex items-center gap-2 self-end text-sm">
          <input type="checkbox" className="h-4 w-4 accent-primary" checked={clientVisible} onChange={(e) => setClientVisible(e.target.checked)} />
          <span>Visível no portal do cliente</span>
        </label>
      </div>
      {problem && <p role="alert" className="text-xs text-destructive">{problem}</p>}
      <AiErrorAlert message={error} />
      {progress !== null && <ProgressBar value={progress} label="Enviando o documento" />}
      <div className="flex justify-end gap-2">
        <Button size="sm" variant="ghost" onClick={onDone} disabled={upload.isPending}>Cancelar</Button>
        <Button size="sm" onClick={submit} disabled={!file || !!problem || upload.isPending}>
          <Upload className="mr-1 h-3.5 w-3.5" /> {upload.isPending ? "Enviando…" : "Enviar documento"}
        </Button>
      </div>
    </div>
  );
}

function EditDialog({ projectId, doc, onClose }: { projectId: string; doc: BuildDocumentDto | null; onClose: () => void }) {
  const update = useUpdateDocument(projectId);
  const [form, setForm] = useState({ category: "Outro" as BuildDocumentCategory, title: "", validUntil: "" });
  const [error, setError] = useState<string | null>(null);
  const [loadedId, setLoadedId] = useState<string | null>(null);
  if (doc && loadedId !== doc.id) {
    setLoadedId(doc.id);
    setForm({ category: (doc.category as BuildDocumentCategory) ?? "Outro", title: doc.title, validUntil: doc.validUntil ?? "" });
    setError(null);
  }

  const save = () => {
    if (!doc) return;
    update.mutate({ id: doc.id, req: { category: form.category, title: form.title.trim() || undefined, validUntil: form.validUntil || undefined } }, {
      onSuccess: () => { toast.success("Documento atualizado."); onClose(); },
      onError:   (e) => setError(aiErrorMessage(e, "Não foi possível salvar.")),
    });
  };

  return (
    <Dialog open={!!doc} onOpenChange={(v) => !v && onClose()}>
      <DialogContent className="sm:max-w-md">
        <DialogHeader><DialogTitle>Editar documento</DialogTitle></DialogHeader>
        <div className="space-y-2">
          <div className="space-y-1">
            <Label htmlFor="doc-edit-title" className="text-xs">Título</Label>
            <Input id="doc-edit-title" value={form.title} maxLength={200} onChange={(e) => setForm({ ...form, title: e.target.value })} />
          </div>
          <div className="space-y-1">
            <Label htmlFor="doc-edit-category" className="text-xs">Categoria</Label>
            <select id="doc-edit-category" className={selectClass} value={form.category}
              onChange={(e) => setForm({ ...form, category: e.target.value as BuildDocumentCategory })}>
              {DOCUMENT_CATEGORIES.map((c) => <option key={c} value={c}>{DOCUMENT_CATEGORY_LABEL[c]}</option>)}
            </select>
          </div>
          <div className="space-y-1">
            <Label htmlFor="doc-edit-valid" className="text-xs">Validade</Label>
            <Input id="doc-edit-valid" type="date" value={form.validUntil} onChange={(e) => setForm({ ...form, validUntil: e.target.value })} />
          </div>
          <AiErrorAlert message={error} />
        </div>
        <DialogFooter>
          <Button variant="outline" onClick={onClose}>Cancelar</Button>
          <Button onClick={save} disabled={update.isPending || !form.title.trim()}>{update.isPending ? "Salvando…" : "Salvar"}</Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

function ExtractionView({ output }: { output: unknown }) {
  const x = readDocumentExtraction(output);
  const rows: Array<[string, string | null]> = [
    ["Tipo", x.documentType], ["Emitente", x.issuer], ["CNPJ do emitente", x.issuerCnpj], ["Número", x.number],
    ["Emissão", x.issueDate ? fmtDate(x.issueDate) : null], ["Vencimento", x.dueDate ? fmtDate(x.dueDate) : null],
    ["Validade", x.validUntil ? fmtDate(x.validUntil) : null], ["Valor total", x.totalAmount != null ? fmtBRL(x.totalAmount) : null],
  ];
  return (
    <div className="space-y-1.5 text-sm" data-testid="document-extraction">
      <dl className="grid gap-x-4 gap-y-0.5 sm:grid-cols-2">
        {rows.filter(([, v]) => v).map(([k, v]) => (
          <div key={k}><dt className="inline text-xs text-muted-foreground">{k}: </dt><dd className="inline">{v}</dd></div>
        ))}
      </dl>
      {x.items.length > 0 && (
        <ul className="list-disc pl-5 text-xs">
          {x.items.slice(0, 20).map((i, n) => (
            <li key={n}>{i.description}{i.quantity != null ? ` · ${i.quantity}` : ""}{i.total != null ? ` · ${fmtBRL(i.total)}` : ""}</li>
          ))}
        </ul>
      )}
      {x.summary && <p className="text-xs text-foreground/80">{x.summary}</p>}
    </div>
  );
}

function DocumentRow({ projectId, doc, visionEnabled, aiEnabled, onEdit, onDelete }: {
  projectId: string; doc: BuildDocumentDto; visionEnabled: boolean; aiEnabled: boolean;
  onEdit: () => void; onDelete: () => void;
}) {
  const visibility = useSetDocumentVisibility(projectId);
  const analyze = useAnalyzeDocument(projectId);
  const { suggestion, setCurrent } = useActiveSuggestion(projectId, "DocumentIntelligence", (s) => s.subjectId === doc.id);
  const [error, setError] = useState<string | null>(null);
  const badge = validityBadge(doc.validUntil, localToday());
  const tooBigForAi = doc.sizeBytes > DOCUMENT_AI_MAX_BYTES;
  const extracted = doc.extracted && typeof doc.extracted === "object" ? doc.extracted : null;

  return (
    <li className="space-y-2 rounded-md border border-border p-3" data-testid="document-row">
      <div className="flex flex-wrap items-start justify-between gap-2">
        <div className="min-w-0 space-y-0.5">
          <p className="flex flex-wrap items-center gap-1.5 text-sm font-medium">
            <FileText className="h-4 w-4 text-muted-foreground" aria-hidden />
            {doc.url
              ? <a href={doc.url} target="_blank" rel="noopener noreferrer" className="hover:underline">{doc.title}<ExternalLink className="ml-0.5 inline h-3 w-3" aria-hidden /></a>
              : doc.title}
            <span className="rounded-full bg-muted px-2 py-0.5 text-[11px] font-medium text-muted-foreground">{categoryLabel(doc.category)}</span>
            {badge && <span className={cn("rounded-full px-2 py-0.5 text-[11px] font-medium", BADGE_CLASS[badge.tone])}>{badge.label}</span>}
          </p>
          <p className="text-[11px] text-muted-foreground">{doc.fileName} · {fmtBytes(doc.sizeBytes)} · enviado por {doc.uploadedByName}</p>
        </div>
        <label className="flex items-center gap-1.5 text-xs">
          <input type="checkbox" className="h-4 w-4 accent-primary" checked={doc.clientVisible} disabled={visibility.isPending}
            aria-label={`Visível ao cliente: ${doc.title}`}
            onChange={(e) => visibility.mutate({ id: doc.id, clientVisible: e.target.checked }, {
              onSuccess: (d) => toast.success(d?.clientVisible ? "Documento visível no portal." : "Documento oculto do portal."),
              onError:   (err) => toast.error(aiErrorMessage(err, "Não foi possível alterar a visibilidade.")),
            })} />
          <span>Visível ao cliente</span>
        </label>
      </div>

      {extracted && (
        <details className="rounded bg-muted/40 px-2 py-1 text-xs">
          <summary className="cursor-pointer font-medium">Dados confirmados da análise (interno)</summary>
          <ExtractionView output={extracted} />
        </details>
      )}

      <div className="flex flex-wrap justify-end gap-1">
        {visionEnabled ? (
          (!suggestion || suggestion.status !== "Pending") && (
            <Button size="sm" variant="outline" className="h-7 text-xs" disabled={!aiEnabled || analyze.isPending || tooBigForAi}
              title={tooBigForAi ? "Arquivos acima de 5 MB não vão à IA." : undefined}
              onClick={() => {
                setError(null);
                analyze.mutate(doc.id, { onSuccess: (s) => setCurrent(s), onError: (e) => setError(aiErrorMessage(e, "Não foi possível analisar.")) });
              }}>
              <Sparkles className="mr-1 h-3 w-3" /> {analyze.isPending ? "Analisando…" : "Analisar com IA"}
            </Button>
          )
        ) : (
          <Button size="sm" variant="outline" className="h-7 text-xs" disabled title={VISION_DISABLED_MESSAGE}>
            <Sparkles className="mr-1 h-3 w-3" /> Analisar com IA
          </Button>
        )}
        <Button size="sm" variant="ghost" className="h-7 text-xs" onClick={onEdit}><Pencil className="mr-1 h-3 w-3" /> Editar</Button>
        <Button size="sm" variant="ghost" className="h-7 text-xs text-destructive" onClick={onDelete}><Trash2 className="mr-1 h-3 w-3" /> Excluir</Button>
      </div>
      {tooBigForAi && visionEnabled && <p className="text-right text-[11px] text-muted-foreground">Acima de 5 MB: não pode ser analisado pela IA.</p>}
      <AiErrorAlert message={error} />

      {suggestion && (
        <>
          <AiSuggestionCard suggestion={suggestion} acceptLabel="Aplicar ao documento"
            acceptHint="Aceitar atualiza a categoria, a validade e os dados extraídos do documento (os dados extraídos são internos)."
            onDecided={(s) => { setCurrent(s); toast.success(s.status === "Rejected" ? "Análise recusada." : "Documento atualizado com a análise."); }}>
            <ExtractionView output={suggestion.output} />
          </AiSuggestionCard>
          {suggestion.status !== "Pending" && (
            <div className="flex justify-end"><Button size="sm" variant="ghost" className="h-7 text-xs" onClick={() => setCurrent(null)}>Fechar</Button></div>
          )}
        </>
      )}
    </li>
  );
}

/** "Documentos": contracts, ART/RRT, permits, invoices… with validity, client visibility and the AI analysis (vision). */
export function DocumentsTab({ projectId }: { projectId: string }) {
  const { data, isLoading, isError } = useDocuments(projectId);
  const gate = useAiGate();
  const remove = useDeleteDocument(projectId);
  const [uploading, setUploading] = useState(false);
  const [category, setCategory] = useState("");
  const [editing, setEditing] = useState<BuildDocumentDto | null>(null);
  const [deleting, setDeleting] = useState<BuildDocumentDto | null>(null);

  const docs = (Array.isArray(data) ? data : [])
    .filter((d) => d && typeof d === "object" && d.id && (!category || d.category === category))
    .sort((a, b) => String(b.createdAt).localeCompare(String(a.createdAt)));

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-end justify-between gap-2">
        <div className="w-48 space-y-1">
          <Label htmlFor="doc-filter" className="text-xs">Categoria</Label>
          <select id="doc-filter" className={selectClass} value={category} onChange={(e) => setCategory(e.target.value)}>
            <option value="">Todas</option>
            {DOCUMENT_CATEGORIES.map((c) => <option key={c} value={c}>{DOCUMENT_CATEGORY_LABEL[c]}</option>)}
          </select>
        </div>
        {!uploading && (
          <Button size="sm" variant="outline" onClick={() => setUploading(true)}><Upload className="mr-1 h-3.5 w-3.5" /> Enviar documento</Button>
        )}
      </div>
      {!gate.loading && gate.aiEnabled && !gate.visionEnabled && (
        <p className="text-xs text-muted-foreground">{VISION_DISABLED_MESSAGE}</p>
      )}

      {uploading && <UploadForm projectId={projectId} onDone={() => setUploading(false)} />}

      {isLoading && <Skeleton className="h-20 w-full" />}
      {isError && <p role="alert" className="text-sm text-destructive">Não foi possível carregar os documentos.</p>}
      {!isLoading && !isError && docs.length === 0 && (
        <p className="rounded-md border border-dashed border-border p-4 text-center text-sm text-muted-foreground">Nenhum documento ainda.</p>
      )}
      {docs.length > 0 && (
        <ul className="space-y-2">
          {docs.map((d) => (
            <DocumentRow key={d.id} projectId={projectId} doc={d} aiEnabled={gate.aiEnabled} visionEnabled={gate.visionEnabled}
              onEdit={() => setEditing(d)} onDelete={() => setDeleting(d)} />
          ))}
        </ul>
      )}

      <EditDialog projectId={projectId} doc={editing} onClose={() => setEditing(null)} />
      <ConfirmActionDialog open={!!deleting} title="Excluir documento?" destructive confirmLabel="Excluir"
        description={<p>“{deleting?.title}” e o arquivo serão apagados. Esta ação não pode ser desfeita.</p>}
        pending={remove.isPending} onClose={() => setDeleting(null)}
        onConfirm={() => deleting && remove.mutate(deleting.id, {
          onSuccess: () => { toast.success("Documento excluído."); setDeleting(null); },
          onError:   (e) => toast.error(aiErrorMessage(e, "Não foi possível excluir.")),
        })} />
    </div>
  );
}
