import { useState } from "react";
import { Check, Pencil, Trash2, X } from "lucide-react";
import { toast } from "sonner";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { useAddBudgetItem, useRemoveBudgetItem, useUpdateBudgetItem } from "../../hooks/use-build";
import type { BuildBudgetDto, BuildBudgetItemDto, BuildCostKind } from "../../api/build.api";
import { COST_KINDS, costKindLabel } from "../../lib/commercial";
import { fmtBRL, parseDecimal } from "../../lib/format";
import { errorMessage } from "../commercial/errors";

interface Props {
  budget: BuildBudgetDto;
  editable: boolean;
}

const EMPTY = { name: "", kind: "Material" as BuildCostKind, category: "Materiais", qty: "1", unit: "un", cost: "", stageName: "" };

/** Budget lines with their kind (Material, Mão de obra…), add / edit / remove while Draft or Sent. */
export function BudgetItemsSection({ budget, editable }: Props) {
  const addMut    = useAddBudgetItem(budget.id);
  const updateMut = useUpdateBudgetItem(budget.id);
  const removeMut = useRemoveBudgetItem(budget.id);

  const [open, setOpen] = useState(false);
  const [editing, setEditing] = useState<BuildBudgetItemDto | null>(null);
  const [f, setF] = useState(EMPTY);
  const set = (patch: Partial<typeof EMPTY>) => setF((prev) => ({ ...prev, ...patch }));

  const reset = () => { setOpen(false); setEditing(null); setF(EMPTY); };

  const startEdit = (item: BuildBudgetItemDto) => {
    setEditing(item);
    setF({
      name: item.name, kind: item.kind ?? "Other", category: item.category, qty: String(item.quantity),
      unit: item.unit, cost: String(item.unitCost), stageName: item.stageName ?? "",
    });
    setOpen(true);
  };

  const qty  = parseDecimal(f.qty);
  const cost = parseDecimal(f.cost);
  const valid = !!f.name.trim() && !!f.category.trim() && !!f.unit.trim()
    && qty != null && !Number.isNaN(qty) && qty > 0 && cost != null && !Number.isNaN(cost) && cost >= 0;

  const save = () => {
    if (!valid || qty == null || cost == null) return;
    const base = {
      name: f.name.trim(), category: f.category.trim(), quantity: qty, unit: f.unit.trim(), unitCost: cost, kind: f.kind,
    };
    if (editing) {
      updateMut.mutate({
        id: editing.id,
        req: { ...base, stageId: editing.stageId ?? undefined, stageName: f.stageName.trim() },
      }, {
        onSuccess: () => { reset(); toast.success("Item atualizado!"); },
        onError:   (e) => toast.error(errorMessage(e, "Erro ao atualizar item.")),
      });
    } else {
      addMut.mutate({ ...base, stageName: f.stageName.trim() || undefined }, {
        onSuccess: () => { reset(); toast.success("Item adicionado!"); },
        onError:   (e) => toast.error(errorMessage(e, "Erro ao adicionar item.")),
      });
    }
  };

  const remove = (id: string) => {
    if (!confirm("Remover este item do orçamento?")) return;
    removeMut.mutate(id, {
      onSuccess: () => toast.success("Item removido."),
      onError:   (e) => toast.error(errorMessage(e, "Erro ao remover item.")),
    });
  };

  return (
    <div className="space-y-2">
      <div className="flex items-center justify-between">
        <p className="text-xs font-semibold uppercase tracking-wide text-muted-foreground">Itens</p>
        {editable && !open && (
          <button onClick={() => { reset(); setOpen(true); }} className="text-xs text-primary hover:underline">
            + Adicionar item
          </button>
        )}
      </div>

      {open && (
        <div className="space-y-2 rounded-md border border-dashed border-primary/40 p-3">
          <div className="grid gap-2 sm:grid-cols-3">
            <div className="sm:col-span-2">
              <Label htmlFor="bi-name" className="text-xs">Descrição</Label>
              <Input id="bi-name" value={f.name} onChange={(e) => set({ name: e.target.value })}
                placeholder="Ex: Cimento CP II" className="mt-0.5 h-8 text-sm" autoFocus />
            </div>
            <div>
              <Label htmlFor="bi-kind" className="text-xs">Tipo</Label>
              <select id="bi-kind" value={f.kind} onChange={(e) => set({ kind: e.target.value as BuildCostKind })}
                className="mt-0.5 h-8 w-full rounded-md border border-input bg-background px-2 text-sm">
                {COST_KINDS.map((k) => <option key={k.value} value={k.value}>{k.label}</option>)}
              </select>
            </div>
          </div>
          <div className="grid gap-2 sm:grid-cols-2">
            <div>
              <Label htmlFor="bi-cat" className="text-xs">Categoria</Label>
              <Input id="bi-cat" value={f.category} onChange={(e) => set({ category: e.target.value })}
                placeholder="Materiais" className="mt-0.5 h-8 text-sm" />
            </div>
            <div>
              <Label htmlFor="bi-stage" className="text-xs">Etapa (nome, opcional)</Label>
              <Input id="bi-stage" value={f.stageName} onChange={(e) => set({ stageName: e.target.value })}
                placeholder="Ex: Fundação" className="mt-0.5 h-8 text-sm" />
            </div>
          </div>
          <div className="grid grid-cols-3 gap-2">
            <div>
              <Label htmlFor="bi-qty" className="text-xs">Qtd</Label>
              <Input id="bi-qty" value={f.qty} onChange={(e) => set({ qty: e.target.value })}
                inputMode="decimal" className="mt-0.5 h-8 text-sm" />
            </div>
            <div>
              <Label htmlFor="bi-unit" className="text-xs">Unidade</Label>
              <Input id="bi-unit" value={f.unit} onChange={(e) => set({ unit: e.target.value })}
                placeholder="m³, kg, un…" className="mt-0.5 h-8 text-sm" />
            </div>
            <div>
              <Label htmlFor="bi-cost" className="text-xs">Custo unit. (R$)</Label>
              <Input id="bi-cost" value={f.cost} onChange={(e) => set({ cost: e.target.value })}
                inputMode="decimal" placeholder="0,00" className="mt-0.5 h-8 text-sm" />
            </div>
          </div>
          <div className="flex justify-end gap-1.5">
            <button onClick={save} aria-label="Salvar item"
              disabled={!valid || addMut.isPending || updateMut.isPending} className="text-primary disabled:opacity-40">
              <Check className="h-4 w-4" />
            </button>
            <button onClick={reset} aria-label="Cancelar item" className="text-muted-foreground">
              <X className="h-4 w-4" />
            </button>
          </div>
        </div>
      )}

      {budget.items.length === 0 ? (
        <p className="py-2 text-center text-sm text-muted-foreground">Nenhum item no orçamento.</p>
      ) : (
        <div className="overflow-x-auto rounded-md border border-border">
          <table className="w-full min-w-[560px] text-sm">
            <thead className="border-b border-border bg-muted/40">
              <tr>
                <th className="px-3 py-2 text-left text-xs font-medium text-muted-foreground">Descrição</th>
                <th className="px-3 py-2 text-left text-xs font-medium text-muted-foreground">Tipo</th>
                <th className="px-3 py-2 text-right text-xs font-medium text-muted-foreground">Qtd</th>
                <th className="px-3 py-2 text-right text-xs font-medium text-muted-foreground">Unit.</th>
                <th className="px-3 py-2 text-right text-xs font-medium text-muted-foreground">Total</th>
                <th className="px-3 py-2" />
              </tr>
            </thead>
            <tbody className="divide-y divide-border">
              {budget.items.map((item) => (
                <tr key={item.id} className="hover:bg-muted/20">
                  <td className="px-3 py-2">
                    <p className="font-medium">{item.name}</p>
                    <p className="text-xs text-muted-foreground">
                      {item.category}{item.stageName ? ` · ${item.stageName}` : ""}
                    </p>
                  </td>
                  <td className="px-3 py-2 text-xs">{costKindLabel(item.kind)}</td>
                  <td className="px-3 py-2 text-right tabular-nums">{item.quantity} {item.unit}</td>
                  <td className="px-3 py-2 text-right tabular-nums">{fmtBRL(item.unitCost)}</td>
                  <td className="px-3 py-2 text-right font-medium tabular-nums">{fmtBRL(item.totalCost)}</td>
                  <td className="whitespace-nowrap px-2 py-2 text-right">
                    {editable && (
                      <div className="flex items-center justify-end gap-1">
                        <button onClick={() => startEdit(item)} aria-label={`Editar ${item.name}`}
                          className="p-1 text-muted-foreground hover:text-foreground">
                          <Pencil className="h-3.5 w-3.5" />
                        </button>
                        <button onClick={() => remove(item.id)} aria-label={`Remover ${item.name}`}
                          disabled={removeMut.isPending}
                          className="p-1 text-muted-foreground hover:text-destructive disabled:opacity-40">
                          <Trash2 className="h-3.5 w-3.5" />
                        </button>
                      </div>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}
