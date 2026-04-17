import { cn } from "@/lib/utils";
import type { ModifierGroupDto } from "../types";

interface ModifierSelectorProps {
  groups: ModifierGroupDto[];
  selected: Record<string, string[]>; // groupId → [modifierId, ...]
  onToggle: (groupId: string, modifierId: string, maxSelections: number) => void;
  errors: Record<string, string>; // groupId → error message
}

export function ModifierSelector({
  groups, selected, onToggle, errors
}: ModifierSelectorProps) {
  if (groups.length === 0) return null;

  return (
    <div className="space-y-4">
      {groups.map((group) => (
        <div key={group.id}>
          <div className="flex items-center gap-1 mb-2">
            <span className="text-sm font-medium">{group.name}</span>
            {group.isRequired && (
              <span className="text-xs text-destructive font-medium">*</span>
            )}
            {group.maxSelections > 1 && (
              <span className="text-xs text-muted-foreground ml-1">
                (até {group.maxSelections})
              </span>
            )}
          </div>
          <div className="flex flex-wrap gap-2">
            {group.modifiers.filter((m) => m.isActive).map((mod) => {
              const isSelected = (selected[group.id] ?? []).includes(mod.id);
              return (
                <button
                  key={mod.id}
                  onClick={() => onToggle(group.id, mod.id, group.maxSelections)}
                  className={cn(
                    "rounded-full border px-3 py-1 text-sm transition-colors",
                    isSelected
                      ? "border-primary bg-primary/10 text-primary font-medium"
                      : "border-border text-muted-foreground"
                  )}
                >
                  {mod.name}
                  {mod.priceAdjustment !== 0 && (
                    <span className="ml-1 text-xs">
                      {mod.priceAdjustment > 0 ? "+" : ""}
                      R$ {Math.abs(mod.priceAdjustment).toFixed(2)}
                    </span>
                  )}
                </button>
              );
            })}
          </div>
          {errors[group.id] && (
            <p className="text-xs text-destructive mt-1">{errors[group.id]}</p>
          )}
        </div>
      ))}
    </div>
  );
}
