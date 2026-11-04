import { useEffect, useState } from "react";
import { Plus, Star, Wrench } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Textarea } from "@/components/ui/textarea";
import {
  Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle,
} from "@/components/ui/dialog";
import { useAssistance, useOpenAssistance, useWarranty } from "../../hooks/use-build-quality";
import type { BuildAssistanceDto, BuildAssistanceStatus } from "../../api/build-quality.api";
import { ASSISTANCE_STATUS, OTHER_CATEGORY, channelLabel, coverageLabel } from "../../lib/quality";
import { fmtDate } from "../../lib/format";
import { StatusPill } from "../commercial/StatusPill";
import { errorMessage } from "../commercial/errors";
import { AssistanceDetailDialog } from "./AssistanceDetailDialog";

/** Technical assistance requests of the obra (opened on the portal or here). */
export function AssistanceSection({ projectId }: { projectId: string }) {
  const [status, setStatus] = useState<BuildAssistanceStatus | "">("");
  const { data, isLoading, isError } = useAssistance(projectId, { status: status || undefined });
  const list = Array.isArray(data) ? data : [];
  const [creating, setCreating] = useState(false);
  const [detail, setDetail] = useState<BuildAssistanceDto | null>(null);

  return (
    <div className="space-y-3">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <select aria-label="Filtrar chamados por status" value={status}
          onChange={(e) => setStatus(e.target.value as BuildAssistanceStatus | "")}
          className="h-8 rounded-md border border-input bg-background px-2 text-sm">
          <option value="">Todos os status</option>
          {(Object.keys(ASSISTANCE_STATUS) as BuildAssistanceStatus[]).map((s) => (
            <option key={s} value={s}>{ASSISTANCE_STATUS[s].label}</option>
          ))}
        </select>
        <Button size="sm" onClick={() => setCreating(true)}><Plus className="mr-1 h-3.5 w-3.5" /> Novo chamado</Button>
      </div>

      {isLoading ? (
        <div className="h-24 animate-pulse rounded-md bg-muted" />
      ) : isError ? (
        <p className="rounded-md border border-destructive/20 bg-destructive/5 p-6 text-center text-sm text-destructive">
          Erro ao carregar os chamados.
        </p>
      ) : list.length === 0 ? (
        <p className="rounded-md border border-dashed border-border p-8 text-center text-sm text-muted-foreground">
          Nenhum chamado de assistência técnica {status ? "com esse status" : "nesta obra"}.
        </p>
      ) : (
        <ul className="space-y-2">
          {list.map((r) => {
            const st = ASSISTANCE_STATUS[r.status] ?? { label: r.status, tone: "gray" as const };
            const cov = coverageLabel(r.warrantyCovered);
            return (
              <li key={r.id}>
                <button type="button" onClick={() => setDetail(r)}
                  className="flex w-full items-start gap-3 rounded-md border border-border bg-card p-3 text-left hover:bg-muted/40">
                  <Wrench className="mt-0.5 h-4 w-4 shrink-0 text-muted-foreground" />
                  <div className="min-w-0 flex-1 space-y-1">
                    <p className="text-sm font-semibold"><span className="mr-1 text-muted-foreground">#{r.number}</span>{r.title}</p>
                    <p className="text-xs text-muted-foreground">
                      {r.category}{r.location ? ` · ${r.location}` : ""} · aberto em {fmtDate(r.openedOn)} por {r.openedByName}
                    </p>
                    <div className="flex flex-wrap gap-1">
                      <StatusPill label={st.label} tone={st.tone} />
                      <StatusPill label={channelLabel(r.openedVia)} tone={r.openedVia === "Portal" ? "blue" : "gray"} />
                      {r.triagedAt && <StatusPill label={cov.label} tone={cov.tone} />}
                      {r.clientRating != null && (
                        <span className="inline-flex items-center gap-0.5 text-xs text-amber-600">
                          <Star className="h-3 w-3 fill-current" /> {r.clientRating}/5
                        </span>
                      )}
                    </div>
                  </div>
                </button>
              </li>
            );
          })}
        </ul>
      )}

      <AssistanceFormDialog projectId={projectId} open={creating} onClose={() => setCreating(false)} />
      <AssistanceDetailDialog projectId={projectId} request={detail} onChange={setDetail} onClose={() => setDetail(null)} />
    </div>
  );
}

function AssistanceFormDialog({ projectId, open, onClose }: { projectId: string; open: boolean; onClose: () => void }) {
  const { data: termsRaw } = useWarranty(projectId);
  const systems = Array.from(new Set((Array.isArray(termsRaw) ? termsRaw : []).map((t) => t.system)));
  const mut = useOpenAssistance(projectId);
  const [category, setCategory] = useState(OTHER_CATEGORY);
  const [title, setTitle] = useState("");
  const [description, setDescription] = useState("");
  const [location, setLocation] = useState("");
  const [openedBy, setOpenedBy] = useState("");
  const [phone, setPhone] = useState("");

  useEffect(() => {
    if (!open) return;
    setCategory(OTHER_CATEGORY); setTitle(""); setDescription(""); setLocation(""); setOpenedBy(""); setPhone("");
  }, [open]);

  const save = () => {
    if (!title.trim()) { toast.error("Informe o título do chamado."); return; }
    if (!description.trim()) { toast.error("Descreva o problema."); return; }
    mut.mutate({
      category, title: title.trim(), description: description.trim(), location: location.trim() || null,
      openedByName: openedBy.trim() || null, contactPhone: phone.trim() || null,
    }, {
      onSuccess: () => { toast.success("Chamado aberto."); onClose(); },
      onError:   (e) => toast.error(errorMessage(e, "Erro ao abrir o chamado.")),
    });
  };

  return (
    <Dialog open={open} onOpenChange={(v) => !v && onClose()}>
      <DialogContent className="max-h-[90vh] overflow-y-auto sm:max-w-md">
        <DialogHeader>
          <DialogTitle>Novo chamado</DialogTitle>
          <DialogDescription>Registre um chamado recebido por telefone, e-mail ou pessoalmente.</DialogDescription>
        </DialogHeader>
        <div className="space-y-3">
          <div>
            <Label htmlFor="as-category" className="text-xs">Categoria</Label>
            <select id="as-category" value={category} onChange={(e) => setCategory(e.target.value)}
              className="mt-0.5 h-9 w-full rounded-md border border-input bg-background px-2 text-sm">
              {systems.filter((s) => s !== OTHER_CATEGORY).map((s) => <option key={s} value={s}>{s}</option>)}
              <option value={OTHER_CATEGORY}>{OTHER_CATEGORY}</option>
            </select>
            <p className="mt-0.5 text-[11px] text-muted-foreground">As categorias são os sistemas das garantias desta obra.</p>
          </div>
          <div>
            <Label htmlFor="as-title" className="text-xs">Título</Label>
            <Input id="as-title" value={title} onChange={(e) => setTitle(e.target.value)} maxLength={200} className="mt-0.5" />
          </div>
          <div>
            <Label htmlFor="as-description" className="text-xs">Descrição</Label>
            <Textarea id="as-description" value={description} onChange={(e) => setDescription(e.target.value)} rows={3} className="mt-0.5" />
          </div>
          <div>
            <Label htmlFor="as-location" className="text-xs">Local no imóvel</Label>
            <Input id="as-location" value={location} onChange={(e) => setLocation(e.target.value)} placeholder="Ex: banheiro da suíte" className="mt-0.5" />
          </div>
          <div className="grid grid-cols-2 gap-3">
            <div>
              <Label htmlFor="as-opened-by" className="text-xs">Solicitante</Label>
              <Input id="as-opened-by" value={openedBy} onChange={(e) => setOpenedBy(e.target.value)} className="mt-0.5" />
            </div>
            <div>
              <Label htmlFor="as-phone" className="text-xs">Telefone</Label>
              <Input id="as-phone" value={phone} onChange={(e) => setPhone(e.target.value)} inputMode="tel" className="mt-0.5" />
            </div>
          </div>
        </div>
        <DialogFooter>
          <Button variant="outline" onClick={onClose}>Cancelar</Button>
          <Button onClick={save} disabled={mut.isPending}>Abrir chamado</Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
