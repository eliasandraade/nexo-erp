import { Tag } from "lucide-react";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { cn } from "@/lib/utils";
import type { DiscountMode } from "../hooks/usePosCart";

interface PosDiscountInputProps {
  mode: DiscountMode;
  value: number;
  onModeChange: (mode: DiscountMode) => void;
  onChange: (value: number) => void;
  subtotal: number;
}

export function PosDiscountInput({
  mode,
  value,
  onModeChange,
  onChange,
  subtotal,
}: PosDiscountInputProps) {
  function handleChange(raw: string) {
    const parsed = parseFloat(raw.replace(",", "."));
    if (isNaN(parsed) || parsed < 0) {
      onChange(0);
      return;
    }
    if (mode === "percentage") {
      onChange(Math.min(parsed, 100));
    } else {
      onChange(Math.min(parsed, subtotal));
    }
  }

  return (
    <div className="space-y-1.5">
      <div className="flex items-center justify-between">
        <Label className="text-xs flex items-center gap-1.5 text-muted-foreground">
          <Tag className="h-3.5 w-3.5" />
          Desconto
        </Label>

        {/* Segmented mode toggle: R$ | % */}
        <div className="flex rounded-md border border-border overflow-hidden text-xs">
          <button
            type="button"
            onClick={() => onModeChange("amount")}
            className={cn(
              "px-2.5 py-1 font-medium transition-colors",
              mode === "amount"
                ? "bg-primary text-primary-foreground"
                : "text-muted-foreground hover:bg-muted"
            )}
          >
            R$
          </button>
          <button
            type="button"
            onClick={() => onModeChange("percentage")}
            className={cn(
              "px-2.5 py-1 font-medium transition-colors border-l border-border",
              mode === "percentage"
                ? "bg-primary text-primary-foreground"
                : "text-muted-foreground hover:bg-muted"
            )}
          >
            %
          </button>
        </div>
      </div>

      <Input
        id="pos-discount"
        placeholder={mode === "percentage" ? "0" : "0,00"}
        value={value === 0 ? "" : String(value)}
        onChange={(e) => handleChange(e.target.value)}
        inputMode="decimal"
        className="h-8 text-sm"
      />
    </div>
  );
}
