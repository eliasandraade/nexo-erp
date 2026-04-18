import { Check } from "lucide-react";
import { cn } from "@/lib/utils";
import type { ModifierGroupDto } from "../types";

interface ModifierSelectorProps {
  groups: ModifierGroupDto[];
  selected: Record<string, string[]>; // groupId → [modifierId, ...]
  onToggle: (groupId: string, modifierId: string, maxSelections: number) => void;
  errors: Record<string, string>; // groupId → error message (set on submit attempt)
}

export function ModifierSelector({
  groups, selected, onToggle, errors
}: ModifierSelectorProps) {
  if (groups.length === 0) return null;

  return (
    <div className="space-y-4">
      {groups.map((group) => {
        const selCount  = selected[group.id]?.length ?? 0;
        const hasError  = !!errors[group.id];
        // Complete = required group that has at least one selection
        const isComplete = group.isRequired && selCount > 0;

        return (
          <div
            key={group.id}
            className={cn(
              "rounded-xl p-3 -mx-1 transition-colors",
              hasError  && "bg-destructive/5 ring-1 ring-destructive/40",
              isComplete && !hasError && "bg-green-500/5 ring-1 ring-green-500/30",
            )}
          >
            {/* Group header */}
            <div className="flex items-center gap-1.5 mb-2.5">
              <span className="text-sm font-medium">{group.name}</span>

              {group.isRequired && !isComplete && (
                <span className="text-[10px] font-semibold uppercase tracking-wide text-destructive/80 bg-destructive/10 px-1.5 py-0.5 rounded">
                  obrigatório
                </span>
              )}

              {isComplete && (
                <span className="flex items-center gap-0.5 text-[10px] font-semibold text-green-600 dark:text-green-400">
                  <Check className="h-3 w-3" />
                  ok
                </span>
              )}

              {group.maxSelections > 1 && (
                <span className="text-xs text-muted-foreground ml-auto">
                  {selCount}/{group.maxSelections}
                </span>
              )}
            </div>

            {/* Modifier pills */}
            <div className="flex flex-wrap gap-2">
              {group.modifiers.filter((m) => m.isActive).map((mod) => {
                const isSelected = (selected[group.id] ?? []).includes(mod.id);
                return (
                  <button
                    key={mod.id}
                    onClick={() => onToggle(group.id, mod.id, group.maxSelections)}
                    className={cn(
                      "rounded-full border px-4 py-2.5 text-sm transition-colors",
                      isSelected
                        ? "border-primary bg-primary/10 text-primary font-medium"
                        : "border-border text-muted-foreground hover:border-muted-foreground/60"
                    )}
                  >
                    {mod.name}
                    {mod.priceAdjustment !== 0 && (
                      <span className="ml-1 text-xs opacity-70">
                        {mod.priceAdjustment > 0 ? "+" : ""}
                        R$ {Math.abs(mod.priceAdjustment).toFixed(2)}
                      </span>
                    )}
                  </button>
                );
              })}
            </div>

            {/* Error message (only after submit attempt) */}
            {hasError && (
              <p className="text-xs text-destructive mt-2">{errors[group.id]}</p>
            )}
          </div>
        );
      })}
    </div>
  );
}
