import { useState } from "react";
import { Plus, Trash2 } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { useSaveBudgetTemplate } from "../../hooks/use-build-commercial";
import type { BuildBudgetTemplateDto, BuildBudgetTemplateItemRequest } from "../../api/build-commercial.api";
import type { BuildCostKind } from "../../api/build.api";
import { COST_KINDS } from "../../lib/commercial";
import { parseDecimal } from "../../lib/format";
import { errorMessage } from "../commercial/errors";

type QtyMode = "perM2" | "fixed";

interface Row {
  key: number;
  name: string;
  kind: BuildCostKind;
  category: string;
  unit: string;
  mode: QtyMode;
  qty: string;
  cost: string;
  stageName: string;
}

let rowSeq = 0;
const newRow = (): Row => ({
  key: ++rowSeq, name: "", kind: "Material", category: "", unit: "un", mode: "fixed", qty: "1", cost: "", stageName: "",
});

function toRows(t: BuildBudgetTemplateDto | null): Row[] {
  if (!t) return [newRow()];
  return t.items.map((i) => ({
    key: ++rowSeq, name: i.name, kind: i.kind, category: i.category, unit: i.unit,
    mode: i.quantityPerM2 != null ? "perM2" : "fixed",
    qty: String(i.quantityPerM2 ?? i.fixedQuantity ?? ""), cost: String(i.unitCost), stageName: i.stageName ?? "",
  }));
}

interface Props {
  template: BuildBudgetTemplateDto | null;
  onDone: () => void;
}

/** Create / edit a budget model; each line has a fixed quantity or a "quantidade por m²". */
export function BudgetTemplateEditor({ template, onDone }: Props) {
  const mut = useSaveBudgetTemplate();
  const [name, setName] = useState(template?.name ?? "");
  const [description, setDescription] = useState(template?.description ?? "");
  const [margin, setMargin] = useState(String(template?.defaultMarginPercent ?? 15));
  const [rows, setRows] = useState<Row[]>(() => toRows(template));

  const patch = (key: number, p: Partial<Row>) => setRows((rs) => rs.map((r) => (r.key === key ? { ...r, ...p } : r)));

  const save = () => {
    const m = parseDecimal(margin);
    if (!name.trim()) { toast.error("Informe o nome do modelo."); return; }
    if (m == null || Number.isNaN(m) || m < 0) { toast.error("Margem inválida."); return; }
    const items: BuildBudgetTemplateItemRequest[] = [];
    for (const r of rows) {
      if (!r.name.trim()) continue;
      const q = parseDecimal(r.qty);
      const c = parseDecimal(r.cost);
      if (q == null || Number.isNaN(q) || q <= 0) { toast.error(`Quantidade inválida em “${r.name}”.`); return; }
      if (c == null || Number.isNaN(c) || c < 0) { toast.error(`Custo inválido em “${r.name}”.`); return; }
      items.push({
        name: r.name.trim(), kind: r.kind, category: r.category.trim() || undefined, unit: r.unit.trim() || "un",
        quantityPerM2: r.mode === "perM2" ? q : null, fixedQuantity: r.mode === "fixed" ? q : null,
        unitCost: c, stageName: r.stageName.trim() || null,
      });
    }
    mut.mutate({
      id: template?.id,
      req: { name: name.trim(), description: description.trim() || null, defaultMarginPercent: m, items },
    }, {
      onSuccess: () => { toast.success("Modelo salvo."); onDone(); },
      onError:   (e) => toast.error(errorMessage(e, "Erro ao salvar o modelo.")),
    });
  };

  return (
    <div className="space-y-3">
      <div className="grid gap-2 sm:grid-cols-3">
        <div className="sm:col-span-2">
          <Label htmlFor="tpl-name" className="text-xs">Nome</Label>
          <Input id="tpl-name" value={name} onChange={(e) => setName(e.target.value)} className="mt-0.5" />
        </div>
        <div>
          <Label htmlFor="tpl-margin" className="text-xs">Margem padrão (%)</Label>
          <Input id="tpl-margin" value={margin} onChange={(e) => setMargin(e.target.value)} inputMode="decimal" className="mt-0.5" />
        </div>
      </div>
      <div>
        <Label htmlFor="tpl-desc" className="text-xs">Descrição (opcional)</Label>
        <Input id="tpl-desc" value={description} onChange={(e) => setDescription(e.target.value)} className="mt-0.5" />
      </div>

      <div className="space-y-2">
        <p className="text-xs font-semibold uppercase tracking-wide text-muted-foreground">Itens</p>
        {rows.map((r, idx) => (
          <div key={r.key} className="grid grid-cols-2 gap-1.5 rounded-md border border-border p-2 sm:grid-cols-6">
            <Input aria-label={`Item ${idx + 1} — descrição`} placeholder="Descrição" value={r.name}
              onChange={(e) => patch(r.key, { name: e.target.value })} className="col-span-2 h-8 text-sm" />
            <select aria-label={`Item ${idx + 1} — tipo`} value={r.kind}
              onChange={(e) => patch(r.key, { kind: e.target.value as BuildCostKind })}
              className="h-8 rounded-md border border-input bg-background px-2 text-sm">
              {COST_KINDS.map((k) => <option key={k.value} value={k.value}>{k.label}</option>)}
            </select>
            <Input aria-label={`Item ${idx + 1} — categoria`} placeholder="Categoria" value={r.category}
              onChange={(e) => patch(r.key, { category: e.target.value })} className="h-8 text-sm" />
            <Input aria-label={`Item ${idx + 1} — etapa`} placeholder="Etapa" value={r.stageName}
              onChange={(e) => patch(r.key, { stageName: e.target.value })} className="h-8 text-sm" />
            <Input aria-label={`Item ${idx + 1} — unidade`} placeholder="Unidade" value={r.unit}
              onChange={(e) => patch(r.key, { unit: e.target.value })} className="h-8 text-sm" />
            <select aria-label={`Item ${idx + 1} — quantidade`} value={r.mode}
              onChange={(e) => patch(r.key, { mode: e.target.value as QtyMode })}
              className="h-8 rounded-md border border-input bg-background px-2 text-sm sm:col-span-2">
              <option value="fixed">Quantidade fixa</option>
              <option value="perM2">Quantidade por m²</option>
            </select>
            <Input aria-label={`Item ${idx + 1} — qtd`} placeholder={r.mode === "perM2" ? "por m²" : "Qtd"} value={r.qty}
              onChange={(e) => patch(r.key, { qty: e.target.value })} inputMode="decimal" className="h-8 text-sm" />
            <Input aria-label={`Item ${idx + 1} — custo unit.`} placeholder="Custo unit. (R$)" value={r.cost}
              onChange={(e) => patch(r.key, { cost: e.target.value })} inputMode="decimal" className="h-8 text-sm" />
            <div className="flex items-center justify-end sm:col-span-2">
              <button type="button" aria-label={`Remover item ${idx + 1}`}
                onClick={() => setRows((rs) => rs.filter((x) => x.key !== r.key))}
                className="p-1 text-muted-foreground hover:text-destructive">
                <Trash2 className="h-3.5 w-3.5" />
              </button>
            </div>
          </div>
        ))}
        <Button type="button" variant="outline" size="sm" onClick={() => setRows((rs) => [...rs, newRow()])}>
          <Plus className="mr-1 h-3.5 w-3.5" /> Adicionar item
        </Button>
      </div>

      <div className="flex justify-end gap-2">
        <Button variant="outline" size="sm" onClick={onDone}>Voltar</Button>
        <Button size="sm" onClick={save} disabled={mut.isPending}>Salvar modelo</Button>
      </div>
    </div>
  );
}
