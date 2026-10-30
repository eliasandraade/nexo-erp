import { useEffect, useState } from "react";
import { X } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Textarea } from "@/components/ui/textarea";
import {
  Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle,
} from "@/components/ui/dialog";
import { useStages } from "../../hooks/use-build";
import { useSaveNonConformity } from "../../hooks/use-build-quality";
import type { BuildNcSeverity, BuildNonConformityDto, SaveBuildNonConformityRequest } from "../../api/build-quality.api";
import { NC_SEVERITIES, ncRequestFrom } from "../../lib/quality";
import { parseDecimal } from "../../lib/format";
import { errorMessage } from "../commercial/errors";
import { PrivateUploadButton } from "./PrivateUploadButton";

const selectCls = "mt-0.5 h-9 w-full rounded-md border border-input bg-background px-2 text-sm";

interface Props {
  projectId: string;
  /** null = new; undefined = closed. */
  nc: BuildNonConformityDto | null | undefined;
  onClose: () => void;
  onSaved?: (nc: BuildNonConformityDto) => void;
}

export function NonConformityFormDialog({ projectId, nc, onClose, onSaved }: Props) {
  const open = nc !== undefined;
  const { data: stages = [] } = useStages(projectId);
  const mut = useSaveNonConformity(projectId);
  const [title, setTitle] = useState("");
  const [description, setDescription] = useState("");
  const [severity, setSeverity] = useState<BuildNcSeverity>("Medium");
  const [stageId, setStageId] = useState("");
  const [responsible, setResponsible] = useState("");
  const [due, setDue] = useState("");
  const [cost, setCost] = useState("");
  const [photos, setPhotos] = useState<Array<{ key: string; url: string | null }>>([]);

  useEffect(() => {
    if (!open) return;
    setTitle(nc?.title ?? "");
    setDescription(nc?.description ?? "");
    setSeverity(nc?.severity ?? "Medium");
    setStageId(nc?.stageId ?? "");
    setResponsible(nc?.responsibleName ?? "");
    setDue(nc?.dueDate ?? "");
    setCost(nc?.costImpact != null ? String(nc.costImpact) : "");
    const keys = Array.isArray(nc?.photoKeys) ? nc.photoKeys : [];
    setPhotos(keys.map((k, i) => ({ key: k, url: nc?.photoUrls?.[i] ?? null })));
  }, [open, nc]);

  const save = () => {
    if (!title.trim()) { toast.error("Informe o título da não conformidade."); return; }
    const c = parseDecimal(cost);
    if (c != null && (Number.isNaN(c) || c < 0)) { toast.error("Impacto de custo inválido."); return; }
    const base: SaveBuildNonConformityRequest = nc ? ncRequestFrom(nc) : { title: "" };
    mut.mutate({
      id: nc?.id,
      req: {
        ...base,
        title: title.trim(), description: description.trim() || null, severity, stageId: stageId || null,
        responsibleName: responsible.trim() || null, dueDate: due || null, costImpact: c,
        photoKeys: photos.map((p) => p.key),
      },
    }, {
      onSuccess: (data) => { toast.success("Não conformidade salva."); onSaved?.(data); onClose(); },
      onError:   (e) => toast.error(errorMessage(e, "Erro ao salvar a não conformidade.")),
    });
  };

  return (
    <Dialog open={open} onOpenChange={(v) => !v && onClose()}>
      <DialogContent className="max-h-[90vh] overflow-y-auto sm:max-w-lg">
        <DialogHeader>
          <DialogTitle>{nc ? `Editar NC #${nc.number}` : "Nova não conformidade"}</DialogTitle>
          <DialogDescription>
            Não conformidade crítica aberta impede concluir a etapa (100%) e a entrega do imóvel.
          </DialogDescription>
        </DialogHeader>
        <div className="space-y-3">
          <div>
            <Label htmlFor="nc-title" className="text-xs">Título</Label>
            <Input id="nc-title" value={title} onChange={(e) => setTitle(e.target.value)} maxLength={200} className="mt-0.5" />
          </div>
          <div>
            <Label htmlFor="nc-desc" className="text-xs">Descrição</Label>
            <Textarea id="nc-desc" value={description} onChange={(e) => setDescription(e.target.value)} rows={2} className="mt-0.5" />
          </div>
          <div className="grid grid-cols-2 gap-3">
            <div>
              <Label htmlFor="nc-severity" className="text-xs">Gravidade</Label>
              <select id="nc-severity" value={severity} onChange={(e) => setSeverity(e.target.value as BuildNcSeverity)} className={selectCls}>
                {NC_SEVERITIES.map((s) => <option key={s.value} value={s.value}>{s.label}</option>)}
              </select>
            </div>
            <div>
              <Label htmlFor="nc-stage" className="text-xs">Etapa</Label>
              <select id="nc-stage" value={stageId} onChange={(e) => setStageId(e.target.value)} className={selectCls}>
                <option value="">Nenhuma</option>
                {stages.map((s) => <option key={s.id} value={s.id}>{s.name}</option>)}
              </select>
            </div>
            <div>
              <Label htmlFor="nc-resp" className="text-xs">Responsável</Label>
              <Input id="nc-resp" value={responsible} onChange={(e) => setResponsible(e.target.value)} className="mt-0.5" />
            </div>
            <div>
              <Label htmlFor="nc-due" className="text-xs">Prazo</Label>
              <Input id="nc-due" type="date" value={due} onChange={(e) => setDue(e.target.value)} className="mt-0.5" />
            </div>
            <div className="col-span-2">
              <Label htmlFor="nc-cost" className="text-xs">Impacto de custo estimado (R$, informativo)</Label>
              <Input id="nc-cost" value={cost} onChange={(e) => setCost(e.target.value)} inputMode="decimal" className="mt-0.5" />
              <p className="mt-0.5 text-[11px] text-muted-foreground">Nada é lançado no financeiro: lance a despesa normalmente se quiser.</p>
            </div>
          </div>
          {severity === "Critical" && (
            <p className="rounded-md border border-red-200 bg-red-50 p-2 text-xs text-red-700 dark:border-red-900 dark:bg-red-900/20 dark:text-red-300">
              Crítica: enquanto aberta, a etapa não chega a 100% e o imóvel não pode ser entregue.
            </p>
          )}
          <div className="space-y-2">
            <p className="text-xs font-medium">Fotos</p>
            {photos.length > 0 && (
              <div className="flex flex-wrap gap-2">
                {photos.map((p, i) => (
                  <div key={p.key} className="relative h-16 w-16 overflow-hidden rounded-md border border-border bg-muted">
                    {p.url && <img src={p.url} alt={`Foto ${i + 1}`} className="h-full w-full object-cover" />}
                    <button type="button" aria-label={`Remover foto ${i + 1}`} onClick={() => setPhotos(photos.filter((x) => x.key !== p.key))}
                      className="absolute right-0.5 top-0.5 rounded bg-background/80 p-0.5 text-muted-foreground hover:text-destructive">
                      <X className="h-3 w-3" />
                    </button>
                  </div>
                ))}
              </div>
            )}
            <PrivateUploadButton context="build-quality" capture label="Adicionar foto" testId="nc-photo-input"
              onUploaded={({ key, url }) => setPhotos((ps) => [...ps, { key, url }])} />
          </div>
        </div>
        <DialogFooter>
          <Button variant="outline" onClick={onClose}>Cancelar</Button>
          <Button onClick={save} disabled={mut.isPending}>Salvar</Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
