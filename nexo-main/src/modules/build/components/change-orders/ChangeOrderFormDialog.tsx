import { useEffect, useState } from "react";
import { Plus, Trash2 } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Textarea } from "@/components/ui/textarea";
import {
  Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle,
} from "@/components/ui/dialog";
import { useStages } from "../../hooks/use-build";
import { useSaveChangeOrder } from "../../hooks/use-build-commercial";
import type {
  BuildChangeOrderDto, BuildChangeOrderItemRequest, BuildChangeOrderReason,
} from "../../api/build-commercial.api";
import type { BuildCostKind } from "../../api/build.api";
import { CHANGE_ORDER_REASONS, COST_KINDS, fmtSignedBRL } from "../../lib/commercial";
import { parseDecimal } from "../../lib/format";
import { errorMessage } from "../commercial/errors";

interface ItemRow { key: number; name: string; kind: BuildCostKind; qty: string; unit: string; cost: string }

let seq = 0;
const blankItem = (): ItemRow => ({ key: ++seq, name: "", kind: "Material", qty: "1", unit: "un", cost: "" });

interface Props {
  projectId: string;
  /** null = new; a draft = editing it; undefined = closed. */
  changeOrder: BuildChangeOrderDto | null | undefined;
  onClose: () => void;
}

/** Create / edit a draft aditivo. Cost impact empty = Σ items; schedule impact ≠ 0 needs the affected stage. */
export function ChangeOrderFormDialog({ projectId, changeOrder, onClose }: Props) {
  const open = changeOrder !== undefined;
  const { data: stages = [] } = useStages(projectId);
  const saveMut = useSaveChangeOrder(projectId);
  const [title, setTitle] = useState("");
  const [description, setDescription] = useState("");
  const [reason, setReason] = useState<BuildChangeOrderReason>("ClientRequest");
  const [cost, setCost] = useState("");
  const [days, setDays] = useState("0");
  const [stageId, setStageId] = useState("");
  const [items, setItems] = useState<ItemRow[]>([]);

  useEffect(() => {
    if (!open) return;
    const c = changeOrder;
    setTitle(c?.title ?? "");
    setDescription(c?.description ?? "");
    setReason(c?.reason ?? "ClientRequest");
    // an impact equal to Σ items stays empty, so it keeps following the items
    const sumItems = c?.items.reduce((s, i) => s + i.totalCost, 0) ?? 0;
    const followsItems = !!c && c.items.length > 0 && Math.abs(c.costImpact - sumItems) < 0.005;
    setCost(c && !followsItems ? String(c.costImpact) : "");
    setDays(String(c?.scheduleImpactDays ?? 0));
    setStageId(c?.stageId ?? "");
    setItems((c?.items ?? []).map((i) => ({
      key: ++seq, name: i.name, kind: i.kind, qty: String(i.quantity), unit: i.unit, cost: String(i.unitCost),
    })));
  }, [open, changeOrder]);

  const patchItem = (key: number, p: Partial<ItemRow>) => setItems((xs) => xs.map((x) => (x.key === key ? { ...x, ...p } : x)));
  const itemsTotal = items.reduce((s, i) => s + (parseDecimal(i.qty) ?? 0) * (parseDecimal(i.cost) ?? 0), 0);

  const save = () => {
    const c = parseDecimal(cost);
    const d = Number(days.trim() || "0");
    if (!title.trim()) { toast.error("Informe o título."); return; }
    if (c != null && Number.isNaN(c)) { toast.error("Impacto no custo inválido."); return; }
    if (!Number.isInteger(d)) { toast.error("Impacto no prazo deve ser um número inteiro de dias."); return; }
    if (d !== 0 && !stageId) { toast.error("Impacto no prazo exige a etapa afetada."); return; }
    const reqItems: BuildChangeOrderItemRequest[] = [];
    for (const i of items) {
      if (!i.name.trim()) continue;
      const q = parseDecimal(i.qty);
      const u = parseDecimal(i.cost);
      if (q == null || Number.isNaN(q) || q <= 0 || u == null || Number.isNaN(u)) {
        toast.error(`Item “${i.name}” com quantidade ou custo inválido.`); return;
      }
      reqItems.push({ name: i.name.trim(), kind: i.kind, quantity: q, unit: i.unit.trim() || "un", unitCost: u });
    }
    saveMut.mutate({
      id: changeOrder?.id,
      req: {
        title: title.trim(), description: description.trim() || null, reason,
        costImpact: c, scheduleImpactDays: d, stageId: stageId || null, items: reqItems,
      },
    }, {
      onSuccess: (dto) => { toast.success(changeOrder ? "Aditivo atualizado." : `Aditivo #${dto.number} criado como rascunho.`); onClose(); },
      onError:   (e) => toast.error(errorMessage(e, "Erro ao salvar o aditivo.")),
    });
  };

  return (
    <Dialog open={open} onOpenChange={(v) => !v && onClose()}>
      <DialogContent className="max-h-[90vh] overflow-y-auto sm:max-w-xl">
        <DialogHeader>
          <DialogTitle>{changeOrder ? `Editar aditivo #${changeOrder.number}` : "Novo aditivo"}</DialogTitle>
          <DialogDescription>Fica como rascunho até você enviar ao cliente ou decidir internamente.</DialogDescription>
        </DialogHeader>
        <div className="space-y-3">
          <div>
            <Label htmlFor="co-title" className="text-xs">Título</Label>
            <Input id="co-title" value={title} onChange={(e) => setTitle(e.target.value)} className="mt-0.5" />
          </div>
          <div>
            <Label htmlFor="co-desc" className="text-xs">Descrição</Label>
            <Textarea id="co-desc" value={description} onChange={(e) => setDescription(e.target.value)} rows={2} className="mt-0.5" />
          </div>
          <div className="grid gap-3 sm:grid-cols-2">
            <div>
              <Label htmlFor="co-reason" className="text-xs">Motivo</Label>
              <select id="co-reason" value={reason} onChange={(e) => setReason(e.target.value as BuildChangeOrderReason)}
                className="mt-0.5 h-9 w-full rounded-md border border-input bg-background px-2 text-sm">
                {CHANGE_ORDER_REASONS.map((r) => <option key={r.value} value={r.value}>{r.label}</option>)}
              </select>
            </div>
            <div>
              <Label htmlFor="co-cost" className="text-xs">Impacto no custo (R$, negativo reduz)</Label>
              <Input id="co-cost" value={cost} onChange={(e) => setCost(e.target.value)} inputMode="decimal"
                placeholder={items.length ? `Soma dos itens: ${fmtSignedBRL(itemsTotal)}` : "0,00"} className="mt-0.5" />
            </div>
            <div>
              <Label htmlFor="co-days" className="text-xs">Impacto no prazo (dias)</Label>
              <Input id="co-days" value={days} onChange={(e) => setDays(e.target.value)} inputMode="numeric" className="mt-0.5" />
            </div>
            <div>
              <Label htmlFor="co-stage" className="text-xs">Etapa afetada</Label>
              <select id="co-stage" value={stageId} onChange={(e) => setStageId(e.target.value)}
                className="mt-0.5 h-9 w-full rounded-md border border-input bg-background px-2 text-sm">
                <option value="">Nenhuma</option>
                {stages.map((s) => <option key={s.id} value={s.id}>{s.name}</option>)}
              </select>
            </div>
          </div>
          <p className="text-[11px] text-muted-foreground">
            Ao aprovar, só a etapa afetada tem o fim previsto deslocado — as seguintes não são empurradas.
          </p>

          <div className="space-y-2">
            <p className="text-xs font-semibold uppercase tracking-wide text-muted-foreground">Itens (opcional)</p>
            {items.map((i, idx) => (
              <div key={i.key} className="grid grid-cols-2 gap-1.5 sm:grid-cols-6">
                <Input aria-label={`Item ${idx + 1} — descrição`} placeholder="Descrição" value={i.name}
                  onChange={(e) => patchItem(i.key, { name: e.target.value })} className="col-span-2 h-8 text-sm" />
                <select aria-label={`Item ${idx + 1} — tipo`} value={i.kind}
                  onChange={(e) => patchItem(i.key, { kind: e.target.value as BuildCostKind })}
                  className="h-8 rounded-md border border-input bg-background px-1 text-sm">
                  {COST_KINDS.map((k) => <option key={k.value} value={k.value}>{k.label}</option>)}
                </select>
                <Input aria-label={`Item ${idx + 1} — qtd`} value={i.qty} inputMode="decimal"
                  onChange={(e) => patchItem(i.key, { qty: e.target.value })} className="h-8 text-sm" />
                <Input aria-label={`Item ${idx + 1} — unidade`} value={i.unit}
                  onChange={(e) => patchItem(i.key, { unit: e.target.value })} className="h-8 text-sm" />
                <div className="flex gap-1">
                  <Input aria-label={`Item ${idx + 1} — custo unit.`} value={i.cost} inputMode="decimal" placeholder="R$"
                    onChange={(e) => patchItem(i.key, { cost: e.target.value })} className="h-8 text-sm" />
                  <button type="button" aria-label={`Remover item ${idx + 1}`}
                    onClick={() => setItems((xs) => xs.filter((x) => x.key !== i.key))}
                    className="p-1 text-muted-foreground hover:text-destructive"><Trash2 className="h-3.5 w-3.5" /></button>
                </div>
              </div>
            ))}
            <Button type="button" variant="outline" size="sm" onClick={() => setItems((xs) => [...xs, blankItem()])}>
              <Plus className="mr-1 h-3.5 w-3.5" /> Adicionar item
            </Button>
            {items.length > 0 && (
              <p className="text-[11px] text-muted-foreground">Deixe o impacto no custo vazio para usar a soma dos itens.</p>
            )}
          </div>
        </div>
        <DialogFooter>
          <Button variant="outline" onClick={onClose}>Cancelar</Button>
          <Button onClick={save} disabled={saveMut.isPending}>Salvar rascunho</Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
