import { ArrowDown, ArrowUp, Plus, Trash2 } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { newEditableItem, type EditableItem } from "../../lib/quality";

interface Props {
  items: EditableItem[];
  onChange: (items: EditableItem[]) => void;
  /** Prefix of the ids / labels (two editors may be on the page). */
  idPrefix: string;
}

/** Ordered list of checklist items: text, required, requires photo; move up/down and remove. */
export function ChecklistItemsEditor({ items, onChange, idPrefix }: Props) {
  const patch = (i: number, p: Partial<EditableItem>) => onChange(items.map((it, j) => (j === i ? { ...it, ...p } : it)));
  const move = (i: number, d: -1 | 1) => {
    const t = i + d;
    if (t < 0 || t >= items.length) return;
    const next = [...items];
    [next[i], next[t]] = [next[t], next[i]];
    onChange(next);
  };

  return (
    <div className="space-y-2">
      {items.length === 0 && <p className="text-xs text-muted-foreground">Nenhum item.</p>}
      <ol className="space-y-2">
        {items.map((it, i) => (
          <li key={it.key} className="space-y-1.5 rounded-md border border-border p-2">
            <div className="flex items-center gap-1">
              <span className="w-5 shrink-0 text-xs tabular-nums text-muted-foreground">{i + 1}.</span>
              <Input aria-label={`Item ${i + 1}`} id={`${idPrefix}-item-${i}`} value={it.text} maxLength={500}
                onChange={(e) => patch(i, { text: e.target.value })} placeholder="Ex: Prumo das paredes conferido"
                className="h-9 text-sm" />
              <button type="button" aria-label={`Subir item ${i + 1}`} onClick={() => move(i, -1)} disabled={i === 0}
                className="p-1 text-muted-foreground hover:text-foreground disabled:opacity-30"><ArrowUp className="h-3.5 w-3.5" /></button>
              <button type="button" aria-label={`Descer item ${i + 1}`} onClick={() => move(i, 1)} disabled={i === items.length - 1}
                className="p-1 text-muted-foreground hover:text-foreground disabled:opacity-30"><ArrowDown className="h-3.5 w-3.5" /></button>
              <button type="button" aria-label={`Remover item ${i + 1}`} onClick={() => onChange(items.filter((_, j) => j !== i))}
                className="p-1 text-muted-foreground hover:text-destructive"><Trash2 className="h-3.5 w-3.5" /></button>
            </div>
            <div className="flex flex-wrap gap-4 pl-6 text-xs">
              <label className="flex items-center gap-1.5">
                <input type="checkbox" checked={it.required} onChange={(e) => patch(i, { required: e.target.checked })} />
                Obrigatório
              </label>
              <label className="flex items-center gap-1.5">
                <input type="checkbox" checked={it.requiresPhoto} onChange={(e) => patch(i, { requiresPhoto: e.target.checked })} />
                Exige foto
              </label>
            </div>
          </li>
        ))}
      </ol>
      <Button type="button" size="sm" variant="outline" onClick={() => onChange([...items, newEditableItem()])}>
        <Plus className="mr-1 h-3.5 w-3.5" /> Adicionar item
      </Button>
    </div>
  );
}
