import { ArrowUp, ArrowDown, Trash2, Plus } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import type { PrepStepDto } from "../types/recipe-card.types";

interface Props {
  steps: PrepStepDto[];
  onChange: (steps: PrepStepDto[]) => void;
}

export function PrepStepsEditor({ steps, onChange }: Props) {
  const add = () =>
    onChange([...steps, { order: steps.length + 1, description: "", durationMinutes: null }]);

  const update = (index: number, patch: Partial<PrepStepDto>) =>
    onChange(steps.map((s, i) => (i === index ? { ...s, ...patch } : s)));

  const remove = (index: number) =>
    onChange(steps.filter((_, i) => i !== index).map((s, i) => ({ ...s, order: i + 1 })));

  const move = (from: number, to: number) => {
    if (to < 0 || to >= steps.length) return;
    const next = [...steps];
    [next[from], next[to]] = [next[to], next[from]];
    onChange(next.map((s, i) => ({ ...s, order: i + 1 })));
  };

  const totalMin = steps.reduce((acc, s) => acc + (s.durationMinutes ?? 0), 0);

  return (
    <div className="space-y-2">
      {steps.map((step, i) => (
        <div key={i} className="flex gap-2 items-start">
          <span className="text-xs text-muted-foreground w-5 pt-2.5 shrink-0">{i + 1}.</span>
          <Input
            className="flex-1 text-sm"
            placeholder="Descrição do passo"
            value={step.description}
            onChange={(e) => update(i, { description: e.target.value })}
          />
          <Input
            className="w-20 text-sm"
            type="number" min="0" placeholder="min"
            value={step.durationMinutes ?? ""}
            onChange={(e) =>
              update(i, { durationMinutes: e.target.value ? parseInt(e.target.value) : null })
            }
          />
          <div className="flex gap-1 shrink-0">
            <Button size="icon" variant="ghost" className="h-8 w-7" onClick={() => move(i, i - 1)}>
              <ArrowUp className="h-3.5 w-3.5" />
            </Button>
            <Button size="icon" variant="ghost" className="h-8 w-7" onClick={() => move(i, i + 1)}>
              <ArrowDown className="h-3.5 w-3.5" />
            </Button>
            <Button size="icon" variant="ghost" className="h-8 w-7 text-destructive" onClick={() => remove(i)}>
              <Trash2 className="h-3.5 w-3.5" />
            </Button>
          </div>
        </div>
      ))}
      <div className="flex items-center justify-between pt-1">
        <Button size="sm" variant="outline" onClick={add}>
          <Plus className="h-3.5 w-3.5 mr-1" /> Adicionar passo
        </Button>
        {totalMin > 0 && (
          <span className="text-xs text-muted-foreground">Tempo total: {totalMin} min</span>
        )}
      </div>
    </div>
  );
}
