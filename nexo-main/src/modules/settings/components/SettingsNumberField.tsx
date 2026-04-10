import { Input } from "@/components/ui/input";

interface SettingsNumberFieldProps {
  label: string;
  description?: string;
  value: number;
  onChange: (value: number) => void;
  min?: number;
  max?: number;
  step?: number;
  unit?: string;
  inputWidth?: string;
}

/**
 * A labeled number input row for numeric settings.
 * Renders as a full-width row with the label/description on the left
 * and the Input (+ optional unit label) on the right, separated by a
 * bottom border — matching the SettingsBooleanField visual rhythm.
 */
export function SettingsNumberField({
  label,
  description,
  value,
  onChange,
  min,
  max,
  step,
  unit,
  inputWidth = "w-24",
}: SettingsNumberFieldProps) {
  return (
    <div className="flex items-center justify-between gap-6 py-3.5 border-b border-border last:border-0">
      <div className="min-w-0">
        <p className="text-sm font-medium text-foreground">{label}</p>
        {description && (
          <p className="text-xs text-muted-foreground mt-0.5">{description}</p>
        )}
      </div>
      <div className="flex items-center gap-2 shrink-0">
        <Input
          type="number"
          min={min}
          max={max}
          step={step}
          value={value}
          onChange={(e) => onChange(Number(e.target.value) || 0)}
          className={`${inputWidth} text-center`}
        />
        {unit && (
          <span className="text-sm text-muted-foreground">{unit}</span>
        )}
      </div>
    </div>
  );
}
