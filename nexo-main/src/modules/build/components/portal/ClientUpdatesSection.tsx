import { useState } from "react";
import { Archive, Megaphone, Pencil, Plus, Send } from "lucide-react";
import { toast } from "sonner";
import { cn } from "@/lib/utils";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Textarea } from "@/components/ui/textarea";
import {
  useArchiveClientUpdate, useClientUpdates, useCreateClientUpdate, useEditClientUpdate, usePublishClientUpdate,
} from "../../hooks/use-build-portal";
import type { BuildClientUpdateDto, BuildClientUpdateKind, BuildClientUpdateStatus } from "../../api/build-portal.api";
import { fmtDateTime } from "../../lib/format";
import { ClientUpdateAi } from "../ai/ClientUpdateAi";

const KIND_LABEL: Record<BuildClientUpdateKind, string> = {
  Update: "Atualização", Milestone: "Marco", WeeklyDigest: "Resumo semanal",
};
const STATUS_LABEL: Record<BuildClientUpdateStatus, string> = {
  Draft: "Rascunho", Published: "Publicada", Archived: "Arquivada",
};
const STATUS_CLASS: Record<BuildClientUpdateStatus, string> = {
  Draft:     "bg-muted text-muted-foreground",
  Published: "bg-emerald-100 text-emerald-700 dark:bg-emerald-900/30 dark:text-emerald-400",
  Archived:  "bg-muted text-muted-foreground line-through",
};

interface Draft { id: string | null; title: string; body: string; kind: BuildClientUpdateKind }

function UpdateEditor({ draft, onChange, onSave, onCancel, saving }: {
  draft: Draft; onChange: (d: Draft) => void; onSave: () => void; onCancel: () => void; saving: boolean;
}) {
  const prefix = draft.id ?? "new";
  return (
    <div className="space-y-2 rounded-md border border-dashed border-primary/40 p-3">
      <div className="grid gap-2 sm:grid-cols-3">
        <div className="space-y-1 sm:col-span-2">
          <Label htmlFor={`cu-title-${prefix}`} className="text-xs">Título *</Label>
          <Input id={`cu-title-${prefix}`} value={draft.title} maxLength={200}
            onChange={(e) => onChange({ ...draft, title: e.target.value })} className="text-sm" />
        </div>
        <div className="space-y-1">
          <Label htmlFor={`cu-kind-${prefix}`} className="text-xs">Tipo</Label>
          <select id={`cu-kind-${prefix}`} value={draft.kind}
            onChange={(e) => onChange({ ...draft, kind: e.target.value as BuildClientUpdateKind })}
            className="h-10 w-full rounded-md border border-input bg-background px-2 text-sm">
            {(Object.keys(KIND_LABEL) as BuildClientUpdateKind[]).map((k) => (
              <option key={k} value={k}>{KIND_LABEL[k]}</option>
            ))}
          </select>
        </div>
      </div>
      <div className="space-y-1">
        <Label htmlFor={`cu-body-${prefix}`} className="text-xs">Texto *</Label>
        <Textarea id={`cu-body-${prefix}`} value={draft.body} maxLength={10000}
          onChange={(e) => onChange({ ...draft, body: e.target.value })} className="min-h-[90px] resize-y text-sm" />
      </div>
      <div className="flex justify-end gap-2">
        <Button size="sm" variant="ghost" onClick={onCancel}>Cancelar</Button>
        <Button size="sm" onClick={onSave} disabled={saving || !draft.title.trim() || !draft.body.trim()}>
          {saving ? "Salvando…" : draft.id ? "Salvar" : "Criar rascunho"}
        </Button>
      </div>
    </div>
  );
}

/** Messages written for the client. They are born drafts; only published ones appear on the portal. */
export function ClientUpdatesSection({ projectId }: { projectId: string }) {
  const { data: updates = [], isLoading } = useClientUpdates(projectId);
  const createMut  = useCreateClientUpdate(projectId);
  const editMut    = useEditClientUpdate(projectId);
  const publishMut = usePublishClientUpdate(projectId);
  const archiveMut = useArchiveClientUpdate(projectId);
  const [draft, setDraft] = useState<Draft | null>(null);

  const err = (fallback: string) => (e: unknown) => toast.error(e instanceof Error ? e.message : fallback);

  const save = () => {
    if (!draft) return;
    const req = { title: draft.title.trim(), body: draft.body.trim(), kind: draft.kind };
    if (draft.id) {
      editMut.mutate({ id: draft.id, req }, {
        onSuccess: () => { setDraft(null); toast.success("Atualização salva."); },
        onError: err("Não foi possível salvar."),
      });
    } else {
      createMut.mutate(req, {
        onSuccess: () => { setDraft(null); toast.success("Rascunho criado. Publique para o cliente ver."); },
        onError: err("Não foi possível criar."),
      });
    }
  };

  const sorted = [...updates].sort((a, b) => b.createdAt.localeCompare(a.createdAt));

  return (
    <section className="space-y-3 rounded-md border border-border bg-card p-4">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <div>
          <h3 className="flex items-center gap-1.5 text-sm font-semibold">
            <Megaphone className="h-4 w-4 text-muted-foreground" /> Atualizações para o cliente
          </h3>
          <p className="text-xs text-muted-foreground">Nascem como rascunho; só as publicadas aparecem no portal.</p>
        </div>
        {!draft && (
          <Button size="sm" variant="outline" onClick={() => setDraft({ id: null, title: "", body: "", kind: "Update" })}>
            <Plus className="mr-1 h-3.5 w-3.5" /> Nova atualização
          </Button>
        )}
      </div>

      <ClientUpdateAi projectId={projectId} />

      {draft && !draft.id && (
        <UpdateEditor draft={draft} onChange={setDraft} onSave={save} onCancel={() => setDraft(null)} saving={createMut.isPending} />
      )}

      {isLoading ? (
        <div className="h-16 rounded-md bg-muted animate-pulse" />
      ) : sorted.length === 0 ? (
        <p className="rounded-md border border-dashed border-border p-4 text-center text-sm text-muted-foreground">
          Nenhuma atualização ainda.
        </p>
      ) : (
        <ul className="space-y-2">
          {sorted.map((u: BuildClientUpdateDto) => (
            <li key={u.id} className="space-y-1.5 rounded-md border border-border p-3">
              {draft?.id === u.id ? (
                <UpdateEditor draft={draft} onChange={setDraft} onSave={save} onCancel={() => setDraft(null)} saving={editMut.isPending} />
              ) : (
                <>
                  <div className="flex flex-wrap items-start justify-between gap-2">
                    <div className="min-w-0">
                      <p className="text-sm font-medium">{u.title}</p>
                      <p className="text-[11px] text-muted-foreground">
                        {KIND_LABEL[u.kind] ?? u.kind} · {u.status === "Published" && u.publishedAt
                          ? `publicada ${fmtDateTime(u.publishedAt)}` : `criada ${fmtDateTime(u.createdAt)}`} · {u.createdByName}
                      </p>
                    </div>
                    <span className={cn("rounded-full px-2 py-0.5 text-[11px] font-medium", STATUS_CLASS[u.status])}>
                      {STATUS_LABEL[u.status] ?? u.status}
                    </span>
                  </div>
                  <p className="whitespace-pre-line text-sm text-foreground/80">{u.body}</p>
                  {u.status !== "Archived" && (
                    <div className="flex flex-wrap justify-end gap-1">
                      <Button size="sm" variant="ghost" className="h-7 text-xs"
                        onClick={() => setDraft({ id: u.id, title: u.title, body: u.body, kind: u.kind })}>
                        <Pencil className="mr-1 h-3 w-3" /> Editar
                      </Button>
                      {u.status === "Draft" && (
                        <Button size="sm" variant="outline" className="h-7 text-xs" disabled={publishMut.isPending}
                          onClick={() => publishMut.mutate(u.id, {
                            onSuccess: () => toast.success("Atualização publicada no portal."),
                            onError: err("Não foi possível publicar."),
                          })}>
                          <Send className="mr-1 h-3 w-3" /> Publicar
                        </Button>
                      )}
                      <Button size="sm" variant="ghost" className="h-7 text-xs text-muted-foreground" disabled={archiveMut.isPending}
                        onClick={() => {
                          if (!confirm("Arquivar esta atualização? Ela sai do portal e não pode mais ser editada.")) return;
                          archiveMut.mutate(u.id, {
                            onSuccess: () => toast.success("Atualização arquivada."),
                            onError: err("Não foi possível arquivar."),
                          });
                        }}>
                        <Archive className="mr-1 h-3 w-3" /> Arquivar
                      </Button>
                    </div>
                  )}
                </>
              )}
            </li>
          ))}
        </ul>
      )}
    </section>
  );
}
