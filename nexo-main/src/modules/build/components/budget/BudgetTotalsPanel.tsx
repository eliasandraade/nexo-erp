import { useState } from "react";
import { Check, Pencil, X } from "lucide-react";
import { toast } from "sonner";
import { Input } from "@/components/ui/input";
import { useSetBudgetMargin } from "../../hooks/use-build";
import type { BuildBudgetDto } from "../../api/build.api";
import { budgetTotals } from "../../lib/commercial";
import { fmtBRL, fmtPercent, parseDecimal } from "../../lib/format";
import { errorMessage } from "../commercial/errors";

interface Props {
  budget: BuildBudgetDto;
  editable: boolean;
}

/** Totals per kind of line, subtotal (cost), margin, total (price) and per m². */
export function BudgetTotalsPanel({ budget, editable }: Props) {
  const t = budgetTotals(budget);
  const marginMut = useSetBudgetMargin();
  const [editing, setEditing] = useState(false);
  const [input, setInput] = useState("");

  const saveMargin = () => {
    const m = parseDecimal(input);
    if (m == null || Number.isNaN(m) || m < 0) { toast.error("Margem inválida."); return; }
    marginMut.mutate({ id: budget.id, req: { marginPercent: m } }, {
      onSuccess: () => { setEditing(false); toast.success("Margem atualizada!"); },
      onError:   (e) => toast.error(errorMessage(e, "Erro ao atualizar margem.")),
    });
  };

  const kinds: Array<[string, number]> = [
    ["Material", t.material], ["Mão de obra", t.labor], ["Serviço", t.service],
    ["Equipamento", t.equipment], ["Outros", t.other],
  ];

  return (
    <div className="grid gap-3 md:grid-cols-2" data-testid="budget-totals">
      <dl className="space-y-1 rounded-md border border-border p-3 text-sm">
        <p className="mb-1 text-[10px] font-semibold uppercase tracking-wide text-muted-foreground">Custo por tipo</p>
        {kinds.map(([label, value]) => (
          <div key={label} className="flex justify-between gap-2">
            <dt className="text-muted-foreground">{label}</dt>
            <dd className="tabular-nums" aria-label={`Total ${label}`}>{fmtBRL(value)}</dd>
          </div>
        ))}
      </dl>
      <dl className="space-y-1 rounded-md border border-border p-3 text-sm">
        <div className="flex justify-between gap-2">
          <dt className="text-muted-foreground">Subtotal (custo)</dt>
          <dd className="tabular-nums font-medium" aria-label="Subtotal">{fmtBRL(t.subtotal)}</dd>
        </div>
        <div className="flex items-center justify-between gap-2">
          <dt className="text-muted-foreground">Margem</dt>
          <dd className="flex items-center gap-1 tabular-nums">
            {editing ? (
              <>
                <Input value={input} onChange={(e) => setInput(e.target.value)} aria-label="Margem (%)"
                  inputMode="decimal" className="h-7 w-16 text-sm" autoFocus />
                <button onClick={saveMargin} disabled={marginMut.isPending} aria-label="Salvar margem"
                  className="text-primary disabled:opacity-40"><Check className="h-4 w-4" /></button>
                <button onClick={() => setEditing(false)} aria-label="Cancelar margem"
                  className="text-muted-foreground"><X className="h-4 w-4" /></button>
              </>
            ) : (
              <>
                {fmtPercent(t.marginPercent)} · {fmtBRL(t.marginValue)}
                {editable && (
                  <button onClick={() => { setInput(String(budget.marginPercent)); setEditing(true); }}
                    aria-label="Editar margem" className="text-muted-foreground hover:text-foreground">
                    <Pencil className="h-3 w-3" />
                  </button>
                )}
              </>
            )}
          </dd>
        </div>
        <div className="flex justify-between gap-2 border-t border-border pt-1">
          <dt className="font-semibold">Total</dt>
          <dd className="font-bold tabular-nums text-primary" aria-label="Total do orçamento">{fmtBRL(t.total)}</dd>
        </div>
        <div className="flex justify-between gap-2 text-xs">
          <dt className="text-muted-foreground">Total por m²</dt>
          <dd className="tabular-nums" aria-label="Total por m²">
            {t.totalPerM2 != null ? `${fmtBRL(t.totalPerM2)}/m²` : "informe a área"}
          </dd>
        </div>
        {t.costPerM2 != null && (
          <div className="flex justify-between gap-2 text-xs">
            <dt className="text-muted-foreground">Custo por m²</dt>
            <dd className="tabular-nums">{fmtBRL(t.costPerM2)}/m²</dd>
          </div>
        )}
      </dl>
    </div>
  );
}
