import { useState } from "react";
import {
  Dialog, DialogContent, DialogHeader, DialogTitle,
} from "@/components/ui/dialog";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Pencil, Trash2, Plus, Check, X } from "lucide-react";
import { cn } from "@/lib/utils";
import { toast } from "sonner";
import {
  useCategories,
  useCreateCategory,
  useUpdateCategory,
  useDeleteCategory,
} from "../hooks/use-products";
import type { CategoryDto } from "../types";

interface ManageCategoriesDialogProps {
  open: boolean;
  onClose: () => void;
}

// ── Inline edit row ───────────────────────────────────────────────────────────

function EditRow({
  category,
  onSave,
  onCancel,
  isSaving,
}: {
  category?: CategoryDto;        // undefined = new
  onSave: (name: string, description: string) => void;
  onCancel: () => void;
  isSaving: boolean;
}) {
  const [name, setName]   = useState(category?.name ?? "");
  const [desc, setDesc]   = useState(category?.description ?? "");

  const handleSave = () => {
    const trimmed = name.trim();
    if (!trimmed) { toast.error("Nome é obrigatório."); return; }
    onSave(trimmed, desc.trim());
  };

  return (
    <div className="flex items-center gap-2 py-2 px-3 rounded-lg bg-muted/40 border border-border">
      <div className="flex-1 flex flex-col gap-1.5">
        <Input
          autoFocus
          placeholder="Nome da categoria"
          value={name}
          onChange={e => setName(e.target.value)}
          onKeyDown={e => { if (e.key === "Enter") handleSave(); if (e.key === "Escape") onCancel(); }}
          className="h-8 text-sm"
        />
        <Input
          placeholder="Descrição (opcional)"
          value={desc}
          onChange={e => setDesc(e.target.value)}
          onKeyDown={e => { if (e.key === "Enter") handleSave(); if (e.key === "Escape") onCancel(); }}
          className="h-7 text-xs"
        />
      </div>
      <div className="flex gap-1 shrink-0">
        <button
          onClick={handleSave}
          disabled={isSaving}
          className="p-1.5 rounded text-green-600 hover:bg-green-500/10 transition-colors disabled:opacity-40"
          title="Salvar"
        >
          <Check className="h-4 w-4" />
        </button>
        <button
          onClick={onCancel}
          disabled={isSaving}
          className="p-1.5 rounded text-muted-foreground hover:bg-muted transition-colors"
          title="Cancelar"
        >
          <X className="h-4 w-4" />
        </button>
      </div>
    </div>
  );
}

// ── Main dialog ───────────────────────────────────────────────────────────────

export function ManageCategoriesDialog({ open, onClose }: ManageCategoriesDialogProps) {
  const { data: categories = [], isLoading } = useCategories();
  const createMut = useCreateCategory();
  const deleteMut = useDeleteCategory();

  const [editingId, setEditingId]   = useState<string | null>(null);
  const [addingNew, setAddingNew]   = useState(false);
  const [deleteConfirm, setDeleteConfirm] = useState<string | null>(null);

  // useUpdateCategory requires a stable id, so we instantiate it lazily via a
  // wrapper that calls the hook at the top level with the currently-editing id.
  const updateMut = useUpdateCategory(editingId ?? "");

  const handleCreate = (name: string, description: string) => {
    createMut.mutate(
      { name, description: description || null },
      {
        onSuccess: () => { toast.success("Categoria criada."); setAddingNew(false); },
        onError:   () => toast.error("Erro ao criar categoria."),
      }
    );
  };

  const handleUpdate = (name: string, description: string) => {
    if (!editingId) return;
    updateMut.mutate(
      { name, description: description || null },
      {
        onSuccess: () => { toast.success("Categoria atualizada."); setEditingId(null); },
        onError:   () => toast.error("Erro ao atualizar categoria."),
      }
    );
  };

  const handleDelete = (id: string) => {
    deleteMut.mutate(id, {
      onSuccess: () => { toast.success("Categoria removida."); setDeleteConfirm(null); },
      onError:   () => { toast.error("Erro ao remover categoria. Ela pode ter produtos vinculados."); setDeleteConfirm(null); },
    });
  };

  return (
    <Dialog open={open} onOpenChange={v => !v && onClose()}>
      <DialogContent className="max-w-sm">
        <DialogHeader>
          <DialogTitle>Gerenciar categorias</DialogTitle>
        </DialogHeader>

        <div className="space-y-1.5 max-h-[60vh] overflow-y-auto pr-1">
          {isLoading && (
            <p className="text-sm text-muted-foreground py-4 text-center">Carregando...</p>
          )}

          {!isLoading && categories.length === 0 && !addingNew && (
            <p className="text-sm text-muted-foreground py-4 text-center">
              Nenhuma categoria cadastrada.
            </p>
          )}

          {categories.map(cat => (
            <div key={cat.id}>
              {editingId === cat.id ? (
                <EditRow
                  category={cat}
                  onSave={handleUpdate}
                  onCancel={() => setEditingId(null)}
                  isSaving={updateMut.isPending}
                />
              ) : deleteConfirm === cat.id ? (
                <div className="flex items-center gap-2 py-2 px-3 rounded-lg bg-destructive/5 border border-destructive/20">
                  <p className="flex-1 text-sm text-destructive">
                    Remover <span className="font-medium">"{cat.name}"</span>?
                  </p>
                  <button
                    onClick={() => handleDelete(cat.id)}
                    disabled={deleteMut.isPending}
                    className="px-2 py-1 rounded text-xs font-medium text-white bg-destructive hover:bg-destructive/90 disabled:opacity-40 transition-colors"
                  >
                    Sim
                  </button>
                  <button
                    onClick={() => setDeleteConfirm(null)}
                    className="px-2 py-1 rounded text-xs text-muted-foreground hover:bg-muted transition-colors"
                  >
                    Não
                  </button>
                </div>
              ) : (
                <div className={cn(
                  "flex items-center gap-2 py-2.5 px-3 rounded-lg hover:bg-muted/40 transition-colors group"
                )}>
                  <div className="flex-1 min-w-0">
                    <p className="text-sm font-medium text-foreground truncate">{cat.name}</p>
                    {cat.description && (
                      <p className="text-[11px] text-muted-foreground truncate">{cat.description}</p>
                    )}
                  </div>
                  <div className="flex gap-1 opacity-0 group-hover:opacity-100 transition-opacity shrink-0">
                    <button
                      onClick={() => { setEditingId(cat.id); setAddingNew(false); }}
                      className="p-1.5 rounded text-muted-foreground hover:text-foreground hover:bg-muted transition-colors"
                      title="Editar"
                    >
                      <Pencil className="h-3.5 w-3.5" />
                    </button>
                    <button
                      onClick={() => setDeleteConfirm(cat.id)}
                      className="p-1.5 rounded text-muted-foreground hover:text-destructive hover:bg-muted transition-colors"
                      title="Remover"
                    >
                      <Trash2 className="h-3.5 w-3.5" />
                    </button>
                  </div>
                </div>
              )}
            </div>
          ))}

          {addingNew && (
            <EditRow
              onSave={handleCreate}
              onCancel={() => setAddingNew(false)}
              isSaving={createMut.isPending}
            />
          )}
        </div>

        {/* Footer */}
        <div className="flex items-center justify-between pt-2 border-t border-border">
          <Button
            variant="outline"
            size="sm"
            onClick={() => { setAddingNew(true); setEditingId(null); }}
            disabled={addingNew}
          >
            <Plus className="h-3.5 w-3.5 mr-1" />
            Nova categoria
          </Button>
          <Button variant="ghost" size="sm" onClick={onClose}>
            Fechar
          </Button>
        </div>
      </DialogContent>
    </Dialog>
  );
}
