import { useEffect, useState } from "react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Textarea } from "@/components/ui/textarea";
import {
  Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle,
} from "@/components/ui/dialog";
import { useStages } from "../../hooks/use-build";
import { useChecklistTemplates } from "../../hooks/use-build-quality";
import type { CreateBuildInspectionRequest } from "../../api/build-quality.api";
import { ChecklistItemsEditor } from "./ChecklistItemsEditor";
import { toItemRequests, type EditableItem } from "../../lib/quality";

const selectCls = "mt-0.5 h-9 w-full rounded-md border border-input bg-background px-2 text-sm";

interface Props {
  open: boolean;
  projectId: string;
  /** Client walkthrough: no stage, no internal notes, default title. */
  walkthrough?: boolean;
  pending?: boolean;
  onSubmit: (req: CreateBuildInspectionRequest) => void;
  onClose: () => void;
}

/** New inspection: from a checklist template (items copied) and/or ad-hoc items. */
export function InspectionFormDialog({ open, projectId, walkthrough, pending, onSubmit, onClose }: Props) {
  const { data: stages = [] } = useStages(projectId);
  const { data: templatesRaw } = useChecklistTemplates();
  const templates = Array.isArray(templatesRaw) ? templatesRaw : [];
  const [title, setTitle] = useState("");
  const [templateId, setTemplateId] = useState("");
  const [stageId, setStageId] = useState("");
  const [date, setDate] = useState("");
  const [inspector, setInspector] = useState("");
  const [notes, setNotes] = useState("");
  const [items, setItems] = useState<EditableItem[]>([]);

  useEffect(() => {
    if (!open) return;
    setTitle(walkthrough ? "Vistoria de entrega com o cliente" : "");
    setTemplateId(""); setStageId(""); setDate(""); setInspector(""); setNotes(""); setItems([]);
  }, [open, walkthrough]);

  const template = templates.find((t) => t.id === templateId);

  const pickTemplate = (id: string) => {
    setTemplateId(id);
    const t = templates.find((x) => x.id === id);
    if (!t) return;
    if (!title.trim() || title === "Vistoria de entrega com o cliente") setTitle(t.name);
    if (!stageId && t.stageNameHint) {
      const hint = t.stageNameHint.toLowerCase();
      const match = stages.find((s) => s.name.toLowerCase().includes(hint));
      if (match) setStageId(match.id);
    }
  };

  const save = () => {
    const extra = toItemRequests(items);
    if (!title.trim()) { toast.error("Informe o título da vistoria."); return; }
    if (!templateId && extra.length === 0) { toast.error("Escolha um checklist ou inclua ao menos um item."); return; }
    onSubmit({
      title:         title.trim(),
      templateId:    templateId || null,
      stageId:       walkthrough ? undefined : stageId || null,
      scheduledFor:  date || null,
      inspectorName: inspector.trim() || null,
      notes:         walkthrough ? undefined : notes.trim() || null,
      items:         extra,
    });
  };

  return (
    <Dialog open={open} onOpenChange={(v) => !v && onClose()}>
      <DialogContent className="max-h-[90vh] overflow-y-auto sm:max-w-lg">
        <DialogHeader>
          <DialogTitle>{walkthrough ? "Vistoria com o cliente" : "Nova vistoria"}</DialogTitle>
          <DialogDescription>
            Os itens do checklist são copiados: mudar o modelo depois não altera esta vistoria.
          </DialogDescription>
        </DialogHeader>
        <div className="space-y-3">
          <div>
            <Label htmlFor="insp-title" className="text-xs">Título</Label>
            <Input id="insp-title" value={title} onChange={(e) => setTitle(e.target.value)} maxLength={200} className="mt-0.5" />
          </div>
          <div>
            <Label htmlFor="insp-template" className="text-xs">Checklist</Label>
            <select id="insp-template" value={templateId} onChange={(e) => pickTemplate(e.target.value)} className={selectCls}>
              <option value="">Em branco (só itens avulsos)</option>
              {templates.map((t) => (
                <option key={t.id} value={t.id}>{t.name}{t.category ? ` — ${t.category}` : ""}</option>
              ))}
            </select>
            {template && (
              <p className="mt-1 text-[11px] text-muted-foreground">
                {template.items.length} item(ns), {template.items.filter((i) => i.requiresPhoto).length} com foto obrigatória.
              </p>
            )}
          </div>
          <div className="grid grid-cols-2 gap-3">
            {!walkthrough && (
              <div>
                <Label htmlFor="insp-stage" className="text-xs">Etapa</Label>
                <select id="insp-stage" value={stageId} onChange={(e) => setStageId(e.target.value)} className={selectCls}>
                  <option value="">Nenhuma</option>
                  {stages.map((s) => <option key={s.id} value={s.id}>{s.name}</option>)}
                </select>
              </div>
            )}
            <div>
              <Label htmlFor="insp-date" className="text-xs">Data prevista</Label>
              <Input id="insp-date" type="date" value={date} onChange={(e) => setDate(e.target.value)} className="mt-0.5" />
            </div>
            <div className={walkthrough ? "" : "col-span-2"}>
              <Label htmlFor="insp-inspector" className="text-xs">Vistoriador</Label>
              <Input id="insp-inspector" value={inspector} onChange={(e) => setInspector(e.target.value)} maxLength={200} className="mt-0.5" />
            </div>
          </div>
          {!walkthrough && (
            <div>
              <Label htmlFor="insp-notes" className="text-xs">Observações (interno)</Label>
              <Textarea id="insp-notes" value={notes} onChange={(e) => setNotes(e.target.value)} rows={2} className="mt-0.5" />
            </div>
          )}
          <div className="space-y-1">
            <p className="text-xs font-medium">Itens avulsos {templateId ? "(além do checklist)" : ""}</p>
            <ChecklistItemsEditor idPrefix="insp" items={items} onChange={setItems} />
          </div>
        </div>
        <DialogFooter>
          <Button variant="outline" onClick={onClose}>Cancelar</Button>
          <Button onClick={save} disabled={pending}>Criar vistoria</Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
