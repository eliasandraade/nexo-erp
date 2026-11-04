import { useState } from "react";
import { Pencil, Plus, Trash2 } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import {
  Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle,
} from "@/components/ui/dialog";
import { useBudgetTemplates, useDeleteBudgetTemplate } from "../../hooks/use-build-commercial";
import type { BuildBudgetTemplateDto } from "../../api/build-commercial.api";
import { fmtPercent } from "../../lib/format";
import { errorMessage } from "../commercial/errors";
import { BudgetTemplateEditor } from "./BudgetTemplateEditor";

interface Props {
  open: boolean;
  onClose: () => void;
}

/** "Modelos de orçamento": list, create, edit, delete (budgets made from a model stay as they are). */
export function BudgetTemplatesDialog({ open, onClose }: Props) {
  const { data: templates, isLoading } = useBudgetTemplates(open);
  const deleteMut = useDeleteBudgetTemplate();
  // undefined = list; null = new; a template = editing it
  const [editing, setEditing] = useState<BuildBudgetTemplateDto | null | undefined>(undefined);

  const remove = (t: BuildBudgetTemplateDto) => {
    if (!confirm(`Excluir o modelo “${t.name}”? Orçamentos criados a partir dele continuam como estão.`)) return;
    deleteMut.mutate(t.id, {
      onSuccess: () => toast.success("Modelo excluído."),
      onError:   (e) => toast.error(errorMessage(e, "Erro ao excluir o modelo.")),
    });
  };

  const close = () => { setEditing(undefined); onClose(); };

  return (
    <Dialog open={open} onOpenChange={(v) => !v && close()}>
      <DialogContent className="max-h-[90vh] overflow-y-auto sm:max-w-2xl">
        <DialogHeader>
          <DialogTitle>{editing === undefined ? "Modelos de orçamento" : editing ? "Editar modelo" : "Novo modelo"}</DialogTitle>
          <DialogDescription>
            Itens padrão para começar orçamentos mais rápido. Quantidades “por m²” são multiplicadas pela área do novo orçamento.
          </DialogDescription>
        </DialogHeader>

        {editing !== undefined ? (
          <BudgetTemplateEditor key={editing?.id ?? "new"} template={editing} onDone={() => setEditing(undefined)} />
        ) : (
          <div className="space-y-2">
            <div className="flex justify-end">
              <Button size="sm" onClick={() => setEditing(null)}><Plus className="mr-1 h-3.5 w-3.5" /> Novo modelo</Button>
            </div>
            {isLoading ? (
              <div className="h-16 animate-pulse rounded-md bg-muted" />
            ) : !templates?.length ? (
              <p className="py-6 text-center text-sm text-muted-foreground">
                Nenhum modelo ainda. Crie um aqui ou use “Salvar como modelo” num orçamento.
              </p>
            ) : (
              <ul className="divide-y divide-border rounded-md border border-border">
                {templates.map((t) => {
                  const perM2 = t.items.filter((i) => i.quantityPerM2 != null).length;
                  return (
                    <li key={t.id} className="flex items-center justify-between gap-2 p-3">
                      <div className="min-w-0">
                        <p className="truncate text-sm font-medium">{t.name}</p>
                        <p className="text-xs text-muted-foreground">
                          {t.items.length} {t.items.length === 1 ? "item" : "itens"}
                          {perM2 > 0 && ` (${perM2} por m²)`} · margem {fmtPercent(t.defaultMarginPercent)}
                          {t.description && ` · ${t.description}`}
                        </p>
                      </div>
                      <div className="flex shrink-0 gap-1">
                        <button onClick={() => setEditing(t)} aria-label={`Editar ${t.name}`}
                          className="p-1 text-muted-foreground hover:text-foreground"><Pencil className="h-3.5 w-3.5" /></button>
                        <button onClick={() => remove(t)} aria-label={`Excluir ${t.name}`} disabled={deleteMut.isPending}
                          className="p-1 text-muted-foreground hover:text-destructive"><Trash2 className="h-3.5 w-3.5" /></button>
                      </div>
                    </li>
                  );
                })}
              </ul>
            )}
          </div>
        )}
      </DialogContent>
    </Dialog>
  );
}
