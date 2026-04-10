import { SectionCard } from "@/components/shared/SectionCard";
import { Checkbox } from "@/components/ui/checkbox";
import { Label } from "@/components/ui/label";
import type { PermissionAction, PermissionModule, PermissionMatrix } from "../types";
import { actionLabels } from "../types";

interface Props {
  module: PermissionModule;
  enabled: PermissionAction[];
  onToggle: (moduleKey: string, action: PermissionAction, checked: boolean) => void;
}

export function PermissionGroupCard({ module, enabled, onToggle }: Props) {
  return (
    <SectionCard title={module.label}>
      <div className="flex flex-wrap gap-x-6 gap-y-2">
        {module.actions.map((action) => {
          const checked = enabled.includes(action);
          return (
            <div key={action} className="flex items-center gap-2">
              <Checkbox
                id={`${module.key}-${action}`}
                checked={checked}
                onCheckedChange={(v) => onToggle(module.key, action, !!v)}
              />
              <Label htmlFor={`${module.key}-${action}`} className="cursor-pointer text-sm">
                {actionLabels[action]}
              </Label>
            </div>
          );
        })}
      </div>
    </SectionCard>
  );
}

interface SelectorProps {
  roles: { value: string; label: string }[];
  selected: string;
  onSelect: (v: string) => void;
}

export function RolePresetSelector({ roles, selected, onSelect }: SelectorProps) {
  return (
    <div className="space-y-1">
      {roles.map((r) => (
        <button
          key={r.value}
          onClick={() => onSelect(r.value)}
          className={`w-full text-left px-3 py-2 rounded-md text-sm font-medium transition-colors ${
            selected === r.value
              ? "bg-primary text-primary-foreground"
              : "hover:bg-muted text-foreground"
          }`}
        >
          {r.label}
        </button>
      ))}
    </div>
  );
}
