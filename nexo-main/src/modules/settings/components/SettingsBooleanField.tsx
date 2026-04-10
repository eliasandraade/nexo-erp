import { Switch } from "@/components/ui/switch";

interface SettingsBooleanFieldProps {
  label: string;
  description?: string;
  checked: boolean;
  onCheckedChange: (value: boolean) => void;
  disabled?: boolean;
}

/**
 * A labeled switch row for boolean settings.
 * Renders as a full-width row with the label/description on the left
 * and the Switch control on the right, separated by a bottom border.
 */
export function SettingsBooleanField({
  label,
  description,
  checked,
  onCheckedChange,
  disabled,
}: SettingsBooleanFieldProps) {
  return (
    <div className="flex items-center justify-between gap-6 py-3.5 border-b border-border last:border-0">
      <div className="min-w-0">
        <p className="text-sm font-medium text-foreground">{label}</p>
        {description && (
          <p className="text-xs text-muted-foreground mt-0.5">{description}</p>
        )}
      </div>
      <Switch
        checked={checked}
        onCheckedChange={onCheckedChange}
        disabled={disabled}
        className="shrink-0"
      />
    </div>
  );
}
