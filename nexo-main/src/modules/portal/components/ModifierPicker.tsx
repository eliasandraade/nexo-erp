import { cn } from "@/lib/utils";
import type { PublicModifierGroupDto, CartModifier } from "../types";

interface ModifierPickerProps {
  groups:   PublicModifierGroupDto[];
  selected: CartModifier[];
  onChange: (modifiers: CartModifier[]) => void;
}

export function ModifierPicker({ groups, selected, onChange }: ModifierPickerProps) {
  const selectedIds = new Set(selected.map((m) => m.modifierId));

  const toggle = (groupId: string, optId: string, label: string, price: number, maxSel: number) => {
    const alreadySelected = selectedIds.has(optId);
    if (alreadySelected) {
      onChange(selected.filter((m) => m.modifierId !== optId));
      return;
    }
    const groupSelected = selected.filter((m) =>
      groups.find((g) => g.id === groupId)?.options.some((o) => o.id === m.modifierId)
    );
    if (maxSel === 1) {
      // radio: replace group selection
      const withoutGroup = selected.filter(
        (m) => !groups.find((g) => g.id === groupId)?.options.some((o) => o.id === m.modifierId)
      );
      onChange([...withoutGroup, { modifierId: optId, label, price }]);
    } else if (groupSelected.length < maxSel) {
      onChange([...selected, { modifierId: optId, label, price }]);
    }
  };

  if (groups.length === 0) return null;

  return (
    <div className="flex flex-col gap-4">
      {groups.map((g) => (
        <div key={g.id}>
          <div className="flex items-center gap-2 mb-2">
            <span className="text-sm font-semibold">{g.name}</span>
            {g.isRequired && (
              <span className="text-[10px] bg-primary/15 text-primary rounded-full px-2 py-0.5 font-medium">
                Obrigatório
              </span>
            )}
            {g.maxSelections > 1 && (
              <span className="text-xs text-muted-foreground ml-auto">
                Até {g.maxSelections}
              </span>
            )}
          </div>
          <div className="flex flex-col gap-1.5">
            {g.options.map((opt) => {
              const isOn = selectedIds.has(opt.id);
              return (
                <button
                  key={opt.id}
                  type="button"
                  onClick={() => toggle(g.id, opt.id, opt.name, opt.priceAdjustment, g.maxSelections)}
                  className={cn(
                    "flex items-center justify-between rounded-lg border px-3 py-2.5 text-left transition-colors",
                    isOn
                      ? "border-primary bg-primary/10 text-foreground"
                      : "border-border bg-card hover:border-primary/40 hover:bg-primary/5"
                  )}
                >
                  <span className="text-sm">{opt.name}</span>
                  {opt.priceAdjustment !== 0 && (
                    <span className={cn("text-sm tabular-nums", isOn ? "text-primary" : "text-muted-foreground")}>
                      {opt.priceAdjustment > 0 ? "+" : ""}R$ {opt.priceAdjustment.toFixed(2)}
                    </span>
                  )}
                </button>
              );
            })}
          </div>
        </div>
      ))}
    </div>
  );
}
