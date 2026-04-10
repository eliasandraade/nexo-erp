import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import type { Product } from "../types";

interface Props {
  data: Partial<Product>;
  onChange: (field: string, value: unknown) => void;
}

export function ProductCommercialSection({ data, onChange }: Props) {
  const margin =
    data.salePrice && data.costPrice && data.salePrice > 0
      ? (((data.salePrice - data.costPrice) / data.salePrice) * 100).toFixed(1)
      : null;

  return (
    <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-5">
      <div className="space-y-1.5">
        <Label>Custo (R$)</Label>
        <Input
          type="number"
          step="0.01"
          min="0"
          value={data.costPrice ?? ""}
          onChange={(e) => onChange("costPrice", parseFloat(e.target.value) || 0)}
        />
      </div>
      <div className="space-y-1.5">
        <Label>Preço de venda (R$)</Label>
        <Input
          type="number"
          step="0.01"
          min="0"
          value={data.salePrice ?? ""}
          onChange={(e) => onChange("salePrice", parseFloat(e.target.value) || 0)}
        />
      </div>
      {margin !== null && (
        <div className="space-y-1.5">
          <Label className="text-muted-foreground">Margem calculada</Label>
          <div className="h-9 flex items-center px-3 rounded-md bg-muted text-sm font-medium">
            {margin}%
          </div>
        </div>
      )}
    </div>
  );
}
