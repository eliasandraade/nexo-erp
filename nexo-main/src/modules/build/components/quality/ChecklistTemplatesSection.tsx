import { useEffect, useState } from "react";
import { Camera, Pencil, Plus, Trash2 } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import {
  Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle,
} from "@/components/ui/dialog";
import { useChecklistTemplates, useDeleteChecklistTemplate, useSaveChecklistTemplate } from "../../hooks/use-build-quality";
import type { BuildChecklistTemplateDto } from "../../api/build-quality.api";
import { errorMessage } from "../commercial/errors";
import { ChecklistItemsEditor } from "./ChecklistItemsEditor";
import { newEditableItem, toItemRequests, type EditableItem } from "../../lib/quality";

/** Checklist templates of the company (shared by every obra): discipline, stage hint and ordered items. */
export function ChecklistTemplatesSection() {
  const { data, isLoading, isError } = useChecklistTemplates();
  const list = Array.isArray(data) ? data : [];
  const del = useDeleteChecklistTemplate();
  const [editing, setEditing] = useState<BuildChecklistTemplateDto | null | undefined>(undefined);

  const remove = (t: BuildChecklistTemplateDto) => {
    if (!confirm(`Excluir o checklist “${t.name}”? Vistorias já criadas não mudam.`)) return;
    del.mutate(t.id, {
      onSuccess: () => toast.success("Checklist excluído."),
      onError:   (e) => toast.error(errorMessage(e, "Não foi possível excluir.")),
    });
  };

  return (
    <div className="space-y-3">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <p className="text-xs text-muted-foreground">Modelos da empresa, usados em todas as obras.</p>
        <Button size="sm" onClick={() => setEditing(null)}><Plus className="mr-1 h-3.5 w-3.5" /> Novo checklist</Button>
      </div>
      {isLoading ? (
        <div className="h-20 animate-pulse rounded-md bg-muted" />
      ) : isError ? (
        <p className="rounded-md border border-destructive/20 bg-destructive/5 p-6 text-center text-sm text-destructive">
          Erro ao carregar os checklists.
        </p>
      ) : list.length === 0 ? (
        <p className="rounded-md border border-dashed border-border p-8 text-center text-sm text-muted-foreground">
          Nenhum checklist cadastrado.
        </p>
      ) : (
        <ul className="grid gap-2 sm:grid-cols-2">
          {list.map((t) => (
            <li key={t.id} className="flex items-start justify-between gap-2 rounded-md border border-border bg-card p-3">
              <div className="min-w-0">
                <p className="text-sm font-semibold">{t.name}</p>
                <p className="text-xs text-muted-foreground">
                  {[t.category, t.stageNameHint ? `etapa: ${t.stageNameHint}` : null].filter(Boolean).join(" · ") || "Sem disciplina"}
                </p>
                <p className="mt-1 flex items-center gap-1 text-xs text-muted-foreground">
                  {t.items.length} item(ns)
                  {t.items.some((i) => i.requiresPhoto) && (
                    <><Camera className="ml-1 h-3 w-3" /> {t.items.filter((i) => i.requiresPhoto).length} com foto</>
                  )}
                </p>
              </div>
              <div className="flex shrink-0 gap-1">
                <button aria-label={`Editar checklist ${t.name}`} onClick={() => setEditing(t)}
                  className="p-1 text-muted-foreground hover:text-foreground"><Pencil className="h-3.5 w-3.5" /></button>
                <button aria-label={`Excluir checklist ${t.name}`} onClick={() => remove(t)}
                  className="p-1 text-muted-foreground hover:text-destructive"><Trash2 className="h-3.5 w-3.5" /></button>
              </div>
            </li>
          ))}
        </ul>
      )}
      <ChecklistTemplateDialog template={editing} onClose={() => setEditing(undefined)} />
    </div>
  );
}

function ChecklistTemplateDialog({ template, onClose }: { template: BuildChecklistTemplateDto | null | undefined; onClose: () => void }) {
  const open = template !== undefined;
  const mut = useSaveChecklistTemplate();
  const [name, setName] = useState("");
  const [category, setCategory] = useState("");
  const [hint, setHint] = useState("");
  const [items, setItems] = useState<EditableItem[]>([]);

  useEffect(() => {
    if (!open) return;
    setName(template?.name ?? "");
    setCategory(template?.category ?? "");
    setHint(template?.stageNameHint ?? "");
    setItems(template
      ? [...template.items].sort((a, b) => a.order - b.order)
          .map((i) => newEditableItem({ text: i.text, required: i.required, requiresPhoto: i.requiresPhoto }))
      : [newEditableItem()]);
  }, [open, template]);

  const save = () => {
    if (!name.trim()) { toast.error("Informe o nome do checklist."); return; }
    const req = {
      name: name.trim(), category: category.trim() || null, stageNameHint: hint.trim() || null, items: toItemRequests(items),
    };
    if (req.items.length === 0) { toast.error("Inclua ao menos um item."); return; }
    mut.mutate({ id: template?.id, req }, {
      onSuccess: () => { toast.success("Checklist salvo."); onClose(); },
      onError:   (e) => toast.error(errorMessage(e, "Erro ao salvar o checklist.")),
    });
  };

  return (
    <Dialog open={open} onOpenChange={(v) => !v && onClose()}>
      <DialogContent className="max-h-[90vh] overflow-y-auto sm:max-w-lg">
        <DialogHeader>
          <DialogTitle>{template ? "Editar checklist" : "Novo checklist"}</DialogTitle>
          <DialogDescription>Salvar substitui os itens; vistorias já criadas mantêm a cópia delas.</DialogDescription>
        </DialogHeader>
        <div className="space-y-3">
          <div>
            <Label htmlFor="ct-name" className="text-xs">Nome</Label>
            <Input id="ct-name" value={name} onChange={(e) => setName(e.target.value)} maxLength={200} className="mt-0.5" />
          </div>
          <div className="grid grid-cols-2 gap-3">
            <div>
              <Label htmlFor="ct-category" className="text-xs">Disciplina</Label>
              <Input id="ct-category" value={category} onChange={(e) => setCategory(e.target.value)} placeholder="Ex: Estrutura" className="mt-0.5" />
            </div>
            <div>
              <Label htmlFor="ct-hint" className="text-xs">Etapa sugerida</Label>
              <Input id="ct-hint" value={hint} onChange={(e) => setHint(e.target.value)} placeholder="Ex: Fundação" className="mt-0.5" />
            </div>
          </div>
          <ChecklistItemsEditor idPrefix="ct" items={items} onChange={setItems} />
        </div>
        <DialogFooter>
          <Button variant="outline" onClick={onClose}>Cancelar</Button>
          <Button onClick={save} disabled={mut.isPending}>Salvar checklist</Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
