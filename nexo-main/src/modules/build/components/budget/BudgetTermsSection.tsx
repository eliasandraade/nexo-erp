import { useState } from "react";
import { Lock, Pencil } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Textarea } from "@/components/ui/textarea";
import { useSetBudgetTerms } from "../../hooks/use-build-commercial";
import type { BuildBudgetDto } from "../../api/build.api";
import { fmtDate, parseDecimal } from "../../lib/format";
import { errorMessage } from "../commercial/errors";

interface Props {
  budget: BuildBudgetDto;
  editable: boolean;
}

/** Validity, area, notes for the client (PDF) and internal notes (never leave the company). */
export function BudgetTermsSection({ budget, editable }: Props) {
  const termsMut = useSetBudgetTerms();
  const [editing, setEditing] = useState(false);
  const [validUntil, setValidUntil] = useState("");
  const [area, setArea] = useState("");
  const [notes, setNotes] = useState("");
  const [internal, setInternal] = useState("");

  const startEdit = () => {
    setValidUntil(budget.validUntil ?? "");
    setArea(budget.areaM2 != null ? String(budget.areaM2) : "");
    setNotes(budget.notes ?? "");
    setInternal(budget.internalNotes ?? "");
    setEditing(true);
  };

  const save = () => {
    const a = parseDecimal(area);
    if (a != null && (Number.isNaN(a) || a <= 0)) { toast.error("Área inválida."); return; }
    termsMut.mutate({
      id: budget.id,
      req: { validUntil: validUntil || null, notes: notes.trim() || null, internalNotes: internal.trim() || null, areaM2: a },
    }, {
      onSuccess: () => { setEditing(false); toast.success("Condições salvas."); },
      onError:   (e) => toast.error(errorMessage(e, "Erro ao salvar as condições.")),
    });
  };

  if (editing) {
    return (
      <div className="space-y-3 rounded-md border border-dashed border-primary/40 p-3">
        <div className="grid gap-3 sm:grid-cols-2">
          <div>
            <Label htmlFor="bt-valid" className="text-xs">Válido até</Label>
            <Input id="bt-valid" type="date" value={validUntil} onChange={(e) => setValidUntil(e.target.value)} className="mt-0.5" />
          </div>
          <div>
            <Label htmlFor="bt-area" className="text-xs">Área (m²)</Label>
            <Input id="bt-area" value={area} onChange={(e) => setArea(e.target.value)} inputMode="decimal"
              placeholder="Ex: 120" className="mt-0.5" />
          </div>
        </div>
        <div>
          <Label htmlFor="bt-notes" className="text-xs">Observações para o cliente (vão no PDF)</Label>
          <Textarea id="bt-notes" value={notes} onChange={(e) => setNotes(e.target.value)} rows={3} className="mt-0.5" />
        </div>
        <div>
          <Label htmlFor="bt-internal" className="flex items-center gap-1 text-xs">
            <Lock className="h-3 w-3" /> Observações internas — não aparece para o cliente
          </Label>
          <Textarea id="bt-internal" value={internal} onChange={(e) => setInternal(e.target.value)} rows={2}
            className="mt-0.5 border-amber-300 bg-amber-50/40 dark:border-amber-800 dark:bg-amber-900/10" />
        </div>
        <div className="flex justify-end gap-2">
          <Button variant="outline" size="sm" onClick={() => setEditing(false)}>Cancelar</Button>
          <Button size="sm" onClick={save} disabled={termsMut.isPending}>Salvar condições</Button>
        </div>
      </div>
    );
  }

  return (
    <div className="space-y-2 rounded-md border border-border p-3 text-sm">
      <div className="flex items-start justify-between gap-2">
        <div className="flex flex-wrap gap-x-6 gap-y-1">
          <p><span className="text-muted-foreground">Validade: </span>{budget.validUntil ? fmtDate(budget.validUntil) : "sem validade"}</p>
          <p><span className="text-muted-foreground">Área: </span>{budget.areaM2 != null ? `${budget.areaM2.toLocaleString("pt-BR")} m²` : "—"}</p>
        </div>
        {editable && (
          <button onClick={startEdit} className="flex items-center gap-1 text-xs text-primary hover:underline">
            <Pencil className="h-3 w-3" /> Editar condições
          </button>
        )}
      </div>
      {budget.notes && (
        <div>
          <p className="text-[10px] font-semibold uppercase tracking-wide text-muted-foreground">Observações para o cliente</p>
          <p className="whitespace-pre-wrap">{budget.notes}</p>
        </div>
      )}
      {budget.internalNotes && (
        <div className="rounded-md border border-amber-200 bg-amber-50/50 p-2 dark:border-amber-800 dark:bg-amber-900/10">
          <p className="flex items-center gap-1 text-[10px] font-semibold uppercase tracking-wide text-amber-700 dark:text-amber-400">
            <Lock className="h-3 w-3" /> Interno — não aparece para o cliente
          </p>
          <p className="whitespace-pre-wrap">{budget.internalNotes}</p>
        </div>
      )}
    </div>
  );
}
